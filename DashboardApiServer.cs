using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  DashboardApiServer
    //
    //  Minimal HTTP API so the React dashboard can trigger real
    //  inspections without needing the console.
    //
    //  Endpoints (all on http://localhost:5173):
    //    GET  /api/status   → { running, caseId, lastUpdated }
    //    GET  /api/images   → [ { path, label, category } ]
    //    POST /api/inspect  → { imagePath } — runs pipeline, returns CaseFile JSON
    //
    //  CORS is set to allow http://localhost:5173 (Vite dev server).
    // ───────────────────────────────────────────────────────────────────

    public class DashboardApiServer
    {
        private readonly KnowledgeGraph _graph;
        private readonly string _datasetRoot;
        private readonly HttpListener _listener = new();
        private bool _running = false;
        private string _currentCaseId = "";
        private DateTime _lastUpdated = DateTime.MinValue;
        private readonly SemaphoreSlim _inspectLock = new(1, 1);

        public const int Port = 5174;

        public DashboardApiServer(KnowledgeGraph graph, string datasetRoot)
        {
            _graph = graph;
            _datasetRoot = datasetRoot;
        }

        public void Start(CancellationToken ct)
        {
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            try { _listener.Start(); }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⚠️  Dashboard API could not bind to port {Port} (already in use). Dashboard RUN button will be disabled.");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  🌐 Dashboard API listening on http://localhost:{Port}/");
            Console.ResetColor();

            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && _listener.IsListening)
                {
                    try
                    {
                        var ctx = await _listener.GetContextAsync();
                        _ = Task.Run(() => HandleRequest(ctx), ct);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch { /* continue */ }
                }
            }, ct);
        }

        public void Stop()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }

        // ─── Request router ───────────────────────────────────────────

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            // CORS headers — allow Vite dev server
            res.Headers.Add("Access-Control-Allow-Origin", "http://localhost:5173");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            var path = req.Url?.AbsolutePath?.TrimEnd('/') ?? "";

            try
            {
                if (req.HttpMethod == "GET" && path == "/api/status")
                    await WriteJson(res, new { running = _running, caseId = _currentCaseId, lastUpdated = _lastUpdated });

                else if (req.HttpMethod == "GET" && path == "/api/images")
                    await WriteJson(res, GetAvailableImages());

                else if (req.HttpMethod == "POST" && path == "/api/inspect")
                    await HandleInspect(req, res);

                else if (req.HttpMethod == "GET" && path == "/api/insights")
                    await HandleInsights(res);

                else if (req.HttpMethod == "GET" && path == "/api/stats")
                    await HandleStats(res);

                else if (req.HttpMethod == "POST" && path == "/api/batch")
                    await HandleBatch(req, res);

                else
                {
                    res.StatusCode = 404;
                    await WriteJson(res, new { error = "Not found" });
                }
            }
            catch (Exception ex)
            {
                res.StatusCode = 500;
                await WriteJson(res, new { error = ex.Message });
            }
        }

        // ─── POST /api/inspect ────────────────────────────────────────

        private async Task HandleInspect(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!await _inspectLock.WaitAsync(0))
            {
                res.StatusCode = 409;
                await WriteJson(res, new { error = "Inspection already in progress" });
                return;
            }

            try
            {
                // Read request body
                string body;
                using (var sr = new System.IO.StreamReader(req.InputStream, req.ContentEncoding))
                    body = await sr.ReadToEndAsync();

                string imagePath = "";
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    imagePath = doc.RootElement.GetProperty("imagePath").GetString() ?? "";
                }
                catch { }

                // If no path given, pick a random sample
                if (string.IsNullOrEmpty(imagePath))
                {
                    var images = GetAvailableImages();
                    if (images.Count == 0)
                    {
                        res.StatusCode = 400;
                        await WriteJson(res, new { error = "No images found in datasets/ folder" });
                        return;
                    }
                    imagePath = images[new Random().Next(images.Count)].Path;
                }

                if (!File.Exists(imagePath))
                {
                    res.StatusCode = 400;
                    await WriteJson(res, new { error = $"Image not found: {imagePath}" });
                    return;
                }

                _running = true;
                _currentCaseId = "";

                try
                {
                    var orchestrator = new McpOrchestrator(_graph);
                    var caseFile = await orchestrator.RunInspectionPipeline(imagePath);
                    _currentCaseId = caseFile.CaseId;
                    _lastUpdated = DateTime.UtcNow;

                    res.StatusCode = 200;
                    await WriteJson(res, new
                    {
                        ok = true,
                        caseId = caseFile.CaseId,
                        defectType = caseFile.NormalizedDefect?.DefectType ?? "",
                        severity = caseFile.NormalizedDefect?.Severity ?? "",
                        status = caseFile.Status.ToString(),
                    });
                }
                finally
                {
                    _running = false;
                }
            }
            finally
            {
                _inspectLock.Release();
            }
        }
        // ─── GET /api/insights ───────────────────────────────────────────
        // Calls Nova Lite with the real knowledge graph summary

        private async Task HandleInsights(HttpListenerResponse res)
        {
            var defects  = _graph.GetNodesByType("defect");
            var images   = _graph.GetNodesByType("image");
            var products = images
                .Select(n => n.Properties.ContainsKey("product") ? n.Properties["product"]?.ToString() ?? "" : "")
                .Where(p => p.Length > 0).Distinct().ToList();
            var similarities  = _graph.FindSimilarDefectsAcrossProducts();
            var equipmentRecs = _graph.GetEquipmentRecommendations();

            var summaryData = new
            {
                TotalProducts = products.Count,
                ProductNames  = products,
                TotalDefects  = defects.Count,
                CrossProductPatterns = similarities.Count,
                EquipmentTypes = equipmentRecs.Count,
                SampleDefects = defects.Take(10).Select(d => new
                {
                    Name     = d.Properties.ContainsKey("name")     ? d.Properties["name"]?.ToString()     : "unknown",
                    Product  = d.Properties.ContainsKey("product")  ? d.Properties["product"]?.ToString()  : "unknown",
                    Severity = d.Properties.ContainsKey("severity") ? d.Properties["severity"]?.ToString() : "unknown",
                }),
            };

            string rawInsight = "";
            try
            {
                var nova = new BedrockNovaClient();
                rawInsight = await nova.InvokeTextAsync(
                    "You are a manufacturing quality expert. Analyze the knowledge graph data and provide exactly 4 insights. " +
                    "Format each insight AS VALID JSON in this array: [{\"num\":1,\"title\":\"...\",\"body\":\"...\",\"action\":\"...\"}]. " +
                    "Return ONLY the JSON array, no markdown, no extra text.",
                    $"Analyze this knowledge graph and give 4 insights (cross-product transfer, equipment optimization, knowledge base value, compliance):\n\n{JsonSerializer.Serialize(summaryData)}",
                    false, "dashboard_insights");
            }
            catch { }

            // Try to parse as JSON array of insights
            List<object> insights = new();
            if (!string.IsNullOrEmpty(rawInsight))
            {
                var cleaned = rawInsight.Trim();
                // Strip markdown fences if present
                if (cleaned.StartsWith("```")) cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"```[\w]*", "").Trim();
                try
                {
                    using var doc = JsonDocument.Parse(cleaned);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                            insights.Add(JsonSerializer.Deserialize<object>(el.GetRawText())!);
                    }
                }
                catch
                {
                    // Fallback: return as raw text in a single insight
                    insights.Add(new { num = 1, title = "AI Analysis", body = rawInsight.Replace("**", "").Replace("##", ""), action = "" });
                }
            }

            if (insights.Count == 0)
            {
                // Fallback static insights from real graph numbers
                insights.Add(new { num = 1, title = "Cross-Product Patterns",     body = $"{similarities.Count} cross-product defect patterns found across {products.Count} categories.",  action = "Standardize the etching bath chemistry SOP across all production lines." });
                insights.Add(new { num = 2, title = "Equipment Optimization",     body = $"{equipmentRecs.Count} defect types have AI-inferred equipment links.",                         action = "Schedule monthly calibration for highest-betweenness equipment nodes." });
                insights.Add(new { num = 3, title = "Knowledge Base Value",       body = $"{defects.Count} defects indexed from {images.Count} images, fully automatic.",                 action = "Expand dataset to increase root-cause confidence." });
                insights.Add(new { num = 4, title = "IPC Compliance Posture",     body = $"{products.Count} product categories linked to IPC compliance sections.",                       action = "Auto-generate IPC reports on every batch completion." });
            }

            await WriteJson(res, new { ok = true, generatedAt = DateTime.UtcNow, insights });
        }

        // ─── GET /api/stats ─────────────────────────────────────────────
        // Returns real defect statistics from the knowledge graph

        private async Task HandleStats(HttpListenerResponse res)
        {
            var defects   = _graph.GetNodesByType("defect");
            var images    = _graph.GetNodesByType("image");
            var equipment = _graph.GetNodesByType("equipment");
            var standards = _graph.GetNodesByType("standard");

            var defectNames = new[] { "open", "short", "mousebite", "spur", "pin_hole", "spurious_copper" };

            var byCat = defectNames.Select(cat =>
            {
                var matching = defects.Where(d =>
                    d.Properties.ContainsKey("name") &&
                    (d.Properties["name"]?.ToString() ?? "").Equals(cat, StringComparison.OrdinalIgnoreCase)).ToList();
                var high = matching.Count(d =>
                    d.Properties.ContainsKey("severity") &&
                    (d.Properties["severity"]?.ToString() ?? "").Equals("high", StringComparison.OrdinalIgnoreCase));
                return new
                {
                    category     = cat,
                    defects      = matching.Count,
                    highSeverity = matching.Count > 0 ? $"{(high * 100 / matching.Count)}%" : "0%",
                    highCount    = high,
                    images       = images.Count(i => i.Properties.ContainsKey("product") &&
                                       (i.Properties["product"]?.ToString() ?? "").Contains(cat, StringComparison.OrdinalIgnoreCase)),
                };
            }).OrderByDescending(c => c.defects).ToList();

            // Severity totals across all defects
            int sevHigh   = defects.Count(d => (d.Properties.ContainsKey("severity") ? d.Properties["severity"]?.ToString() ?? "" : "").Equals("high",   StringComparison.OrdinalIgnoreCase));
            int sevMedium = defects.Count(d => (d.Properties.ContainsKey("severity") ? d.Properties["severity"]?.ToString() ?? "" : "").Equals("medium", StringComparison.OrdinalIgnoreCase));
            int sevLow    = defects.Count(d => (d.Properties.ContainsKey("severity") ? d.Properties["severity"]?.ToString() ?? "" : "").Equals("low",    StringComparison.OrdinalIgnoreCase));

            // Defects grouped by product
            var productGroups = defects
                .GroupBy(d => d.Properties.ContainsKey("product") ? d.Properties["product"]?.ToString() ?? "pcb" : "pcb")
                .Select(g => new { product = g.Key, count = g.Count() })
                .OrderByDescending(p => p.count).ToList();

            // Equipment hubs — sort by betweenness approximation (inbound edge count)
            var allRels = _graph.AllRelationships;
            var eqHubs = equipment.Select(eq => new
            {
                name   = eq.Properties.ContainsKey("name") ? eq.Properties["name"]?.ToString() ?? eq.Id : eq.Id,
                edges  = allRels.Count(r => r.FromNodeId == eq.Id || r.ToNodeId == eq.Id),
            }).OrderByDescending(e => e.edges).Take(10).ToList();

            // Top insights
            string mostCommon = byCat.FirstOrDefault()?.category ?? "—";
            int mostCommonCount = byCat.FirstOrDefault()?.defects ?? 0;

            await WriteJson(res, new
            {
                ok = true,
                totalNodes      = _graph.GetNodesByType("image").Count + defects.Count + equipment.Count + standards.Count,
                totalEdges      = allRels.Count,
                totalImages     = images.Count,
                totalDefects    = defects.Count,
                totalEquipment  = equipment.Count,
                totalStandards  = standards.Count,
                totalProducts   = productGroups.Count,
                avgDefectsPerProduct = productGroups.Count > 0 ? Math.Round((double)defects.Count / productGroups.Count, 1) : 0,
                severityHigh    = sevHigh,
                severityMedium  = sevMedium,
                severityLow     = sevLow,
                byCat,
                byProduct       = productGroups,
                equipmentHubs   = eqHubs,
                mostCommonDefect = mostCommon,
                mostCommonCount  = mostCommonCount,
            });

            await Task.CompletedTask;
        }

        // ─── POST /api/batch ─────────────────────────────────────────────
        // Runs real pipeline on N images per category, streams results

        private async Task HandleBatch(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!await _inspectLock.WaitAsync(0))
            {
                res.StatusCode = 409;
                await WriteJson(res, new { error = "An inspection is already in progress" });
                return;
            }
            try
            {
                // Read perCategory from body
                string body;
                using (var sr = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = await sr.ReadToEndAsync();

                int perCategory = 1;
                try { using var doc = JsonDocument.Parse(body); perCategory = Math.Clamp(doc.RootElement.GetProperty("perCategory").GetInt32(), 1, 5); } catch { }

                var pcbDataDir = Path.Combine(Environment.CurrentDirectory, "datasets", "PCBData");
                if (!Directory.Exists(pcbDataDir))
                {
                    // Try datasets/flowchart or mvtec as fallback
                    pcbDataDir = Path.Combine(Environment.CurrentDirectory, "datasets");
                }

                var defectNames = new Dictionary<string, string>
                { ["0"] = "open", ["1"] = "short", ["2"] = "mousebite", ["3"] = "spur", ["4"] = "pin_hole", ["5"] = "spurious_copper" };

                var allImages = Directory.GetFiles(pcbDataDir, "*_test.jpg",  SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(pcbDataDir, "*_test.png", SearchOption.AllDirectories))
                    .ToList();
                if (!allImages.Any())
                    allImages = Directory.GetFiles(pcbDataDir, "*.jpg", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(pcbDataDir, "*.png", SearchOption.AllDirectories))
                        .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("_not"))
                        .ToList();

                // Group by category
                var byCategory = defectNames.Values.ToDictionary(v => v, _ => new List<string>());
                byCategory["unknown"] = new List<string>();

                foreach (var img in allImages)
                {
                    var annot = img.Replace("_test.jpg", ".txt").Replace("_test.png", ".txt");
                    if (!File.Exists(annot))
                    {
                        var parent = Path.GetDirectoryName(Path.GetDirectoryName(img) ?? "") ?? "";
                        var groupId = Path.GetFileName(Path.GetDirectoryName(img) ?? "");
                        var notDir = Path.Combine(parent, groupId + "_not");
                        var candidate = Path.Combine(notDir, Path.GetFileNameWithoutExtension(img).Replace("_test", "") + ".txt");
                        if (File.Exists(candidate)) annot = candidate;
                    }
                    if (File.Exists(annot))
                    {
                        var parts = (File.ReadLines(annot).FirstOrDefault()?.Trim() ?? "").Split(' ');
                        if (parts.Length >= 5 && defectNames.TryGetValue(parts[4], out var cat)) { byCategory[cat].Add(img); continue; }
                    }
                    byCategory["unknown"].Add(img);
                }

                var selected = new List<(string cat, string path)>();
                var rng = new Random();
                foreach (var kv in byCategory.Where(kv => kv.Value.Any()).ToList())
                    selected.AddRange(kv.Value.OrderBy(_ => rng.Next()).Take(perCategory).Select(p => (kv.Key, p)));

                if (!selected.Any())
                {
                    res.StatusCode = 400;
                    await WriteJson(res, new { error = "No images found in datasets/PCBData" });
                    return;
                }

                _running = true;
                var results = new List<object>();
                var orch = new McpOrchestrator(_graph);

                foreach (var (cat, imgPath) in selected)
                {
                    try
                    {
                        var cf = await orch.RunInspectionPipeline(imgPath);
                        results.Add(new
                        {
                            category   = cat,
                            image      = Path.GetFileName(imgPath),
                            caseId     = cf.CaseId,
                            defectType = cf.NormalizedDefect?.DefectType ?? "",
                            severity   = cf.NormalizedDefect?.Severity ?? "",
                            status     = cf.Status.ToString(),
                            humanReview = cf.HumanReviewRequired,
                            actions    = cf.AgentActions.Count,
                            violations = cf.PolicyViolations.Count,
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { category = cat, image = Path.GetFileName(imgPath), error = ex.Message });
                    }
                }

                _running = false;
                await WriteJson(res, new { ok = true, total = results.Count, results });
            }
            finally
            {
                _running = false;
                _inspectLock.Release();
            }
        }
        // ─── GET /api/images ──────────────────────────────────────────

        private List<ImageEntry> GetAvailableImages()
        {
            var results = new List<ImageEntry>();

            if (!Directory.Exists(_datasetRoot)) return results;

            // DeepPCB: *_test.jpg
            var testJpgs = Directory.GetFiles(_datasetRoot, "*_test.jpg", SearchOption.AllDirectories)
                .Take(120).ToArray();

            foreach (var p in testJpgs)
            {
                var category = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(p)) ?? "") ?? "";
                results.Add(new ImageEntry
                {
                    Path = p,
                    Label = Path.GetFileName(p),
                    Category = category,
                });
            }

            // MVTec: test/**/*.png (first 5 per category)
            if (results.Count == 0)
            {
                foreach (var catDir in Directory.GetDirectories(_datasetRoot).Take(16))
                {
                    var testDir = Path.Combine(catDir, "test");
                    if (!Directory.Exists(testDir)) continue;
                    var pngs = Directory.GetFiles(testDir, "*.png", SearchOption.AllDirectories).Take(5);
                    foreach (var p in pngs)
                    {
                        results.Add(new ImageEntry
                        {
                            Path = p,
                            Label = Path.GetFileName(p),
                            Category = Path.GetFileName(catDir),
                        });
                    }
                }
            }

            return results.Take(100).ToList();
        }

        // ─── Helpers ──────────────────────────────────────────────────

        private static async Task WriteJson(HttpListenerResponse res, object obj)
        {
            res.ContentType = "application/json";
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
            res.OutputStream.Close();
        }

        public class ImageEntry
        {
            public string Path { get; set; } = "";
            public string Label { get; set; } = "";
            public string Category { get; set; } = "";
        }
    }
}
