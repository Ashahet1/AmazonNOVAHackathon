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
    // --------------------------------------------------------------------//  DashboardApiServer
    //
    //  Minimal HTTP API so the React dashboard can trigger real
    //  inspections and read live data from the knowledge graph.
    //
    //  Endpoints (all on http://localhost:5174):
    //    GET  /api/status   -> { running, caseId, lastUpdated }
    //    GET  /api/images   -> [ { path, label, category } ]
    //    GET  /api/stats    -> defect statistics from knowledge graph
    //    GET  /api/insights -> Nova-generated insights from knowledge graph
    //    POST /api/inspect  -> { imagePath } -> runs pipeline, returns CaseFile JSON
    //    POST /api/batch    -> { perCategory } -> batch run
    //
    //  CORS: dynamically reflects any localhost origin so Vite/CRA/etc
    //  work on any port (5173, 3000, 3001, etc.)
    // --------------------------------------------------------------------
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
        public const int FallbackPort = 5175;   // used if 5174 is already bound

        private int _boundPort = Port;

        public DashboardApiServer(KnowledgeGraph graph, string datasetRoot)
        {
            _graph = graph;
            _datasetRoot = datasetRoot;
        }

        public void Start(CancellationToken ct)
        {
            // Try primary port first, then fallback
            bool started = TryStartListener(Port, ct);
            if (!started)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ->?  Port {Port} in use -> trying fallback port {FallbackPort}...");
                Console.ResetColor();
                started = TryStartListener(FallbackPort, ct);
            }

            if (!started)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ERROR: Dashboard API could not bind to port {Port} or {FallbackPort}.");
                Console.WriteLine($"     Run 'netstat -ano | findstr :{Port}' to see what's using the port.");
                Console.WriteLine($"     Dashboard RUN button will be disabled until the port is free.");
                Console.ResetColor();
            }
        }

        private bool TryStartListener(int port, CancellationToken ct)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                _boundPort = port;
            }
            catch
            {
                return false;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  ? Dashboard API listening on http://localhost:{port}/");
            Console.WriteLine($"     Open your React app and it will automatically connect.");
            Console.ResetColor();

            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && listener.IsListening)
                {
                    try
                    {
                        var ctx = await listener.GetContextAsync();
                        _ = Task.Run(() => HandleRequest(ctx), ct);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch { /* continue */ }
                }
            }, ct);

            return true;
        }

        public void Stop()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }

        // ---- CORS helper ----
        //  Reflects any localhost origin so Vite / CRA work on any port.

        private static void AddCorsHeaders(HttpListenerRequest req, HttpListenerResponse res)
        {
            var origin = req.Headers["Origin"] ?? "";

            // Allow any localhost or 127.0.0.1 origin (any port)
            if (origin.StartsWith("http://localhost:") ||
                origin.StartsWith("http://127.0.0.1:") ||
                origin == "http://localhost" ||
                origin == "http://127.0.0.1")
            {
                res.Headers["Access-Control-Allow-Origin"] = origin;
            }
            else if (string.IsNullOrEmpty(origin))
            {
                // Non-browser request (curl, Postman, etc.) -> allow
                res.Headers["Access-Control-Allow-Origin"] = "*";
            }
            // Requests from non-localhost origins are not given CORS headers (blocked by browser)

            res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Accept";
            res.Headers["Access-Control-Max-Age"] = "86400";  // cache preflight for 24 h
        }

        // ---- Request router ----
        private async Task HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            // CORS headers on every response
            AddCorsHeaders(req, res);

            // Handle CORS preflight (OPTIONS)
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
                    await WriteJson(res, new { running = _running, caseId = _currentCaseId, lastUpdated = _lastUpdated, port = _boundPort });

                else if (req.HttpMethod == "GET" && path == "/api/images")
                    await WriteJson(res, GetAvailableImages());

                else if (req.HttpMethod == "POST" && path == "/api/inspect")
                    await HandleInspect(req, res);

                else if ((req.HttpMethod == "GET" || req.HttpMethod == "POST") && path == "/api/insights")
                    await HandleInsights(req, res);

                else if (req.HttpMethod == "GET" && path == "/api/stats")
                    await HandleStats(res);

                else if (req.HttpMethod == "POST" && path == "/api/batch")
                    await HandleBatch(req, res);

                else
                {
                    await ServeStaticFile(res, path);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ERROR: API error [{req.HttpMethod} {path}]: {ex.Message}");
                Console.ResetColor();
                res.StatusCode = 500;
                await WriteJson(res, new { error = ex.Message, path, method = req.HttpMethod });
            }
        }

        // ---- POST /api/inspect ----
        private async Task HandleInspect(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!await _inspectLock.WaitAsync(0))
            {
                res.StatusCode = 409;
                await WriteJson(res, new { error = "Inspection already in progress. Wait for current one to complete." });
                return;
            }

            try
            {
                string body;
                using (var sr = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = await sr.ReadToEndAsync();

                string imagePath = "";
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    imagePath = doc.RootElement.GetProperty("imagePath").GetString() ?? "";
                }
                catch { /* imagePath stays empty -> random sample */ }

                // Pick random sample if no path given
                if (string.IsNullOrEmpty(imagePath))
                {
                    var images = GetAvailableImages();
                    if (images.Count == 0)
                    {
                        res.StatusCode = 400;
                        await WriteJson(res, new { error = "No images found in datasets/ folder. Ensure DeepPCB dataset is extracted to datasets/PCBData/" });
                        return;
                    }
                    imagePath = images[new Random().Next(images.Count)].Path;
                }

                if (!File.Exists(imagePath))
                {
                    res.StatusCode = 400;
                    await WriteJson(res, new { error = $"Image file not found: {imagePath}" });
                    return;
                }

                _running = true;
                _currentCaseId = "";

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n  ? Dashboard triggered inspection: {Path.GetFileName(imagePath)}");
                Console.ResetColor();

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
                        humanReview = caseFile.HumanReviewRequired,
                        agentActions = caseFile.AgentActions.Count,
                        rootCause = caseFile.RootCause?.ProbableCause ?? "",
                        compliance = caseFile.Compliance != null ? new
                        {
                            standard = caseFile.Compliance.ApplicableStandard,
                            disposition = caseFile.Compliance.Disposition,
                            classification = caseFile.Compliance.Classification
                        } : null,
                        imageName = Path.GetFileName(imagePath)
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

        // ---- GET /api/insights  (general graph) ----
        // ---- POST /api/insights (case-specific — sends last inspection result in body) ----
        // Calls Nova Lite with context that is UNIQUE to the current image so insights differ per run.

        private async Task HandleInsights(HttpListenerRequest req, HttpListenerResponse res)
        {
            var defects      = _graph.GetNodesByType("defect");
            var images       = _graph.GetNodesByType("image");
            var products     = images
                .Select(n => n.Properties.ContainsKey("product") ? n.Properties["product"]?.ToString() ?? "" : "")
                .Where(p => p.Length > 0).Distinct().ToList();
            var similarities  = _graph.FindSimilarDefectsAcrossProducts();
            var equipmentRecs = _graph.GetEquipmentRecommendations();
            var defectFreq    = _graph.GetDefectFrequency();

            if (defects.Count == 0)
            {
                await WriteJson(res, new { ok = false, error = "Knowledge graph is empty. Run at least one inspection first.", insights = Array.Empty<object>() });
                return;
            }

            // --- Read optional case-specific context from POST body ---
            string caseJson = "";
            if (req.HttpMethod == "POST" && req.ContentLength64 > 0)
            {
                using var sr = new StreamReader(req.InputStream, req.ContentEncoding);
                caseJson = await sr.ReadToEndAsync();
            }

            // --- Parse last inspection data if provided ---
            string imageName       = "";
            string defectType      = "";
            string severity        = "";
            string disposition     = "";
            string probableCause   = "";
            string rootReasoning   = "";
            string confidence      = "";
            List<string> factors   = new();
            List<string> ipcFailed = new();
            List<string> equipment = new();

            bool hasCaseContext = false;
            if (!string.IsNullOrEmpty(caseJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(caseJson);
                    var r = doc.RootElement;
                    imageName     = r.TryGetProperty("imagePath",  out var ip)  ? System.IO.Path.GetFileName(ip.GetString() ?? "") : "";
                    defectType    = r.TryGetProperty("normalizedDefect", out var nd) && nd.TryGetProperty("defectType",  out var dt)  ? dt.GetString() ?? "" : "";
                    severity      = r.TryGetProperty("normalizedDefect", out var nd2) && nd2.TryGetProperty("severity",    out var sv)  ? sv.GetString() ?? "" : "";
                    disposition   = r.TryGetProperty("compliance",       out var co)  && co.TryGetProperty("disposition",  out var dp)  ? dp.GetString() ?? "" : "";
                    probableCause = r.TryGetProperty("rootCause",        out var rc) && rc.TryGetProperty("probableCause", out var pc)  ? pc.GetString() ?? "" : "";
                    rootReasoning = r.TryGetProperty("rootCause",        out var rc2) && rc2.TryGetProperty("reasoning",    out var rs) ? rs.GetString() ?? "" : "";
                    confidence    = r.TryGetProperty("rootCause",        out var rc3) && rc3.TryGetProperty("confidence",   out var cf) ? cf.GetDouble().ToString("P0") : "";

                    if (r.TryGetProperty("rootCause", out var rc4) && rc4.TryGetProperty("contributingFactors", out var cf2) && cf2.ValueKind == JsonValueKind.Array)
                        foreach (var f in cf2.EnumerateArray()) { var s = f.GetString(); if (!string.IsNullOrEmpty(s)) factors.Add(s); }

                    if (r.TryGetProperty("compliance", out var comp) && comp.TryGetProperty("checklist", out var cl) && cl.ValueKind == JsonValueKind.Array)
                        foreach (var item in cl.EnumerateArray())
                            if (item.TryGetProperty("addressed", out var addr) && !addr.GetBoolean() && item.TryGetProperty("sectionRef", out var sr2))
                                ipcFailed.Add(sr2.GetString() ?? "");

                    if (r.TryGetProperty("graphContext", out var gc) && gc.TryGetProperty("equipmentIds", out var eq) && eq.ValueKind == JsonValueKind.Array)
                        foreach (var e in eq.EnumerateArray()) { var s = e.GetString(); if (!string.IsNullOrEmpty(s)) equipment.Add(s); }

                    hasCaseContext = !string.IsNullOrEmpty(defectType);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [WARN] /api/insights: Could not parse case context - {ex.Message}");
                }
            }

            // --- Build the defect frequency context (top 5 most common defects in full graph) ---
            var topDefects = defectFreq.OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => $"{kv.Key}({kv.Value})").ToList();

            // --- Build the Nova prompt ---
            string systemPrompt =
                "You are a PCB manufacturing quality engineer specialising in root-cause analysis and IPC compliance. " +
                "Your job is to generate ACTIONABLE insights that are SPECIFIC to the current inspection result — " +
                "not generic advice. Reference the exact defect name, severity, equipment, and IPC sections. " +
                "Format your response as a valid JSON array with exactly 4 objects: " +
                "[{\"num\":1,\"title\":\"...\",\"body\":\"...\",\"action\":\"...\"}, ...]. " +
                "Return ONLY the JSON array. No markdown, no extra text.";

            string userPrompt;
            if (hasCaseContext)
            {
                // Case-specific prompt — different per image
                userPrompt =
                    $"Current inspection result:\n" +
                    $"  Image:         {imageName}\n" +
                    $"  Defect type:   {defectType}\n" +
                    $"  Severity:      {severity}\n" +
                    $"  Disposition:   {disposition}\n" +
                    $"  Root cause:    {probableCause}\n" +
                    $"  Confidence:    {confidence}\n" +
                    $"  Reasoning:     {rootReasoning}\n" +
                    $"  Contributing:  {string.Join("; ", factors)}\n" +
                    $"  Equipment:     {string.Join(", ", equipment)}\n" +
                    $"  IPC failures:  {(ipcFailed.Count > 0 ? string.Join(", ", ipcFailed) : "none")}\n\n" +
                    $"Full graph context:\n" +
                    $"  Total defects: {defects.Count} across {images.Count} images\n" +
                    $"  Defect frequency (top 5): {string.Join(", ", topDefects)}\n" +
                    $"  Cross-product patterns: {similarities.Count}\n" +
                    $"  Equipment nodes: {equipmentRecs.Count}\n\n" +
                    $"Generate 4 insights: " +
                    $"(1) what this specific defect ({defectType}, {severity}) means for THIS batch given the graph trends, " +
                    $"(2) root cause confidence analysis — is {probableCause} supported by graph patterns?, " +
                    $"(3) equipment action — which equipment to prioritise and why based on this case, " +
                    $"(4) IPC compliance risk — given {(ipcFailed.Count > 0 ? $"{ipcFailed.Count} failed sections" : "no failures")}, what is the compliance posture.";
            }
            else
            {
                // General graph summary prompt (no current inspection)
                userPrompt =
                    $"Knowledge graph summary:\n" +
                    $"  Total defects: {defects.Count} across {images.Count} images and {products.Count} product categories\n" +
                    $"  Defect frequency (top 5): {string.Join(", ", topDefects)}\n" +
                    $"  Cross-product patterns: {similarities.Count}\n" +
                    $"  Equipment nodes: {equipmentRecs.Count}\n\n" +
                    $"Generate 4 insights covering: cross-product transfer opportunities, top equipment to maintain, " +
                    $"knowledge graph ROI vs manual inspection, and IPC compliance posture across the dataset.";
            }

            string rawInsight = "";
            string novaError  = "";
            try
            {
                Console.WriteLine($"  [INFO] /api/insights: Calling Nova Lite ({(hasCaseContext ? $"case-specific: {imageName}, {defectType}" : "general graph")})...");
                var nova = new BedrockNovaClient();
                rawInsight = await nova.InvokeTextAsync(systemPrompt, userPrompt, false, "dashboard_insights");
                Console.WriteLine("  [OK] /api/insights: Nova responded successfully.");
            }
            catch (Exception ex)
            {
                novaError = ex.Message;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [WARN] /api/insights: Nova call failed - {ex.Message}. Using static fallback.");
                Console.ResetColor();
            }

            // --- Parse Nova response ---
            List<object> insights = new();
            bool usedNova = false;

            if (!string.IsNullOrEmpty(rawInsight))
            {
                var cleaned = rawInsight.Trim();
                if (cleaned.StartsWith("```"))
                    cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"```[\w]*", "").Trim().TrimEnd('`').Trim();
                // Find first '[' to skip any preamble Nova adds
                int bracket = cleaned.IndexOf('[');
                if (bracket > 0) cleaned = cleaned[bracket..];
                try
                {
                    using var doc = JsonDocument.Parse(cleaned);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                            insights.Add(JsonSerializer.Deserialize<object>(el.GetRawText())!);
                        usedNova = insights.Count > 0;
                    }
                }
                catch (JsonException parseEx)
                {
                    Console.WriteLine($"  [WARN] /api/insights: Nova JSON parse failed - {parseEx.Message}");
                    insights.Add(new { num = 1, title = "AI Analysis", body = rawInsight.Replace("**", "").Replace("##", "").Trim(), action = "Review the full analysis above." });
                    usedNova = true;
                }
            }

            // --- Static fallback (uses case data if available for specificity) ---
            if (insights.Count == 0)
            {
                if (hasCaseContext)
                {
                    int sameDefectCount = defectFreq.TryGetValue(defectType, out var cnt) ? cnt : 0;
                    double pct = defects.Count > 0 ? Math.Round(sameDefectCount * 100.0 / defects.Count, 1) : 0;
                    insights.Add(new { num = 1, title = $"Batch Risk: {defectType}", body = $"This {severity} '{defectType}' defect accounts for {sameDefectCount} of {defects.Count} total defects ({pct}%) in the dataset. {(pct > 20 ? "Systemic pattern — not an isolated event." : "Low frequency — may be isolated.")}", action = $"Quarantine this board and check adjacent boards in the same batch for {defectType}." });
                    insights.Add(new { num = 2, title = "Root Cause Confidence", body = $"Root cause '{probableCause}' identified with {confidence} confidence. {(factors.Count > 0 ? $"Contributing factors: {string.Join(", ", factors)}." : "")}", action = "Raise a P1 work order to investigate the root cause before the next production shift." });
                    insights.Add(new { num = 3, title = "Equipment Priority", body = $"Equipment involved: {(equipment.Count > 0 ? string.Join(", ", equipment) : "none identified")}. The knowledge graph links this defect type to {equipmentRecs.Count} equipment nodes.", action = "Schedule immediate calibration check on the identified equipment." });
                    insights.Add(new { num = 4, title = "IPC Compliance Risk", body = $"Disposition: {disposition}. {(ipcFailed.Count > 0 ? $"Failed IPC sections: {string.Join(", ", ipcFailed)}. Non-conformance requires documented corrective action." : "All checked IPC sections passed for this board.")}", action = ipcFailed.Count > 0 ? "File corrective action report per IPC-A-600J within 24 hours." : "Mark board as compliant and update the quality log." });
                }
                else
                {
                    insights.Add(new { num = 1, title = "Cross-Product Patterns", body = $"{similarities.Count} cross-product defect patterns found across {products.Count} PCB categories.", action = "Standardize etching bath chemistry SOP across all production lines." });
                    insights.Add(new { num = 2, title = "Equipment Optimization", body = $"{equipmentRecs.Count} defect types have AI-inferred equipment links. Top defects: {string.Join(", ", topDefects)}.", action = "Schedule monthly calibration for highest-betweenness equipment nodes." });
                    insights.Add(new { num = 3, title = "Knowledge Base Value", body = $"{defects.Count} defects indexed from {images.Count} images automatically.", action = "Expand dataset to increase root-cause confidence above 90%." });
                    insights.Add(new { num = 4, title = "IPC Compliance Posture", body = $"{products.Count} product categories linked to IPC-A-600J compliance sections.", action = "Auto-generate IPC reports on every batch completion." });
                }
            }

            await WriteJson(res, new
            {
                ok = true,
                generatedAt = DateTime.UtcNow,
                source = usedNova ? "nova" : "static",
                caseSpecific = hasCaseContext,
                imageName,
                defectType,
                novaError = string.IsNullOrEmpty(novaError) ? null : novaError,
                insights
            });
        }

        // ---- GET /api/stats ----
        private async Task HandleStats(HttpListenerResponse res)
        {
            // Uses the EXACT same KnowledgeGraph methods as ShowCompleteDashboard (Program.cs case "3")
            var defects   = _graph.GetNodesByType("defect");
            var images    = _graph.GetNodesByType("image");
            var equipment = _graph.GetNodesByType("equipment");
            var standards = _graph.GetNodesByType("standard");

            // If graph is empty, return zeros with a helpful flag
            if (defects.Count == 0 && images.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  [WARN] /api/stats: Knowledge graph is empty.");
                Console.ResetColor();
                await WriteJson(res, new
                {
                    ok = false, empty = true,
                    message = "Knowledge graph is empty. At startup choose option 1 (load from cache) or option 2 (rebuild from dataset).",
                    totalNodes = 0, totalEdges = 0, totalImages = 0, totalDefects = 0,
                    totalEquipment = 0, totalStandards = 0, totalProducts = 0,
                    severityHigh = 0, severityMedium = 0, severityLow = 0, crossProductPatterns = 0,
                    byCat = Array.Empty<object>(), byProduct = Array.Empty<object>(),
                    equipmentHubs = Array.Empty<object>(), insights = Array.Empty<string>()
                });
                return;
            }

            // Same calls as ShowCompleteDashboard
            var defectFreq    = _graph.GetDefectFrequency();
            var severityDist  = _graph.GetSeverityDistribution();
            var productCounts = _graph.GetProductDefectCounts();
            var equipUsage    = _graph.GetEquipmentUsage();
            var similarities  = _graph.FindSimilarDefectsAcrossProducts();
            var insights      = _graph.GenerateInsights();
            var (heatProds, _, heatData) = _graph.GetQualityHeatmap();
            var allRels       = _graph.AllRelationships;

            var byCat = defectFreq
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new { category = kv.Key, defects = kv.Value })
                .ToList();

            var byProduct = productCounts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new { product = kv.Key, count = kv.Value })
                .ToList();

            var equipmentHubs = equipUsage
                .Select(kv => new { name = kv.Key, edges = kv.Value })
                .ToList();

            int sevHigh   = severityDist.Where(kv => kv.Key.StartsWith("High",   StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
            int sevMedium = severityDist.Where(kv => kv.Key.StartsWith("Medium", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
            int sevLow    = severityDist.Where(kv => kv.Key.StartsWith("Low",    StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);

            var heatmap = Enumerable.Range(0, heatProds.Length).Select(i => new
            {
                product = heatProds[i],
                low     = heatData[i, 0],
                medium  = heatData[i, 1],
                high    = heatData[i, 2],
                total   = heatData[i, 0] + heatData[i, 1] + heatData[i, 2],
            }).ToList();

            await WriteJson(res, new
            {
                ok = true, empty = false,
                totalNodes           = images.Count + defects.Count + equipment.Count + standards.Count,
                totalEdges           = allRels.Count,
                totalImages          = images.Count,
                totalDefects         = defects.Count,
                totalEquipment       = equipment.Count,
                totalStandards       = standards.Count,
                totalProducts        = byProduct.Count,
                avgDefectsPerProduct = byProduct.Count > 0 ? Math.Round((double)defects.Count / byProduct.Count, 1) : 0,
                severityHigh         = sevHigh,
                severityMedium       = sevMedium,
                severityLow          = sevLow,
                crossProductPatterns = similarities.Count,
                mostCommonDefect     = byCat.FirstOrDefault()?.category ?? "",
                mostCommonCount      = byCat.FirstOrDefault()?.defects  ?? 0,
                byCat,
                byProduct,
                equipmentHubs,
                heatmap,
                insights,
            });

            await Task.CompletedTask;
        }
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
                string body;
                using (var sr = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = await sr.ReadToEndAsync();

                int perCategory = 1;
                try { using var doc = JsonDocument.Parse(body); perCategory = Math.Clamp(doc.RootElement.GetProperty("perCategory").GetInt32(), 1, 5); } catch { }

                var pcbDataDir = Path.Combine(Environment.CurrentDirectory, "datasets", "PCBData");
                if (!Directory.Exists(pcbDataDir))
                    pcbDataDir = Path.Combine(Environment.CurrentDirectory, "datasets");

                var defectNames = new Dictionary<string, string>
                { ["0"] = "open", ["1"] = "short", ["2"] = "mousebite", ["3"] = "spur", ["4"] = "pin_hole", ["5"] = "spurious_copper" };

                var allImages = Directory.GetFiles(pcbDataDir, "*_test.jpg", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(pcbDataDir, "*_test.png", SearchOption.AllDirectories))
                    .ToList();
                if (!allImages.Any())
                    allImages = Directory.GetFiles(pcbDataDir, "*.jpg", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(pcbDataDir, "*.png", SearchOption.AllDirectories))
                        .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("_not"))
                        .ToList();

                var byCategory = defectNames.Values.ToDictionary(v => v, _ => new List<string>());
                byCategory["unknown"] = new List<string>();

                foreach (var img in allImages)
                {
                    var annot = img.Replace("_test.jpg", ".txt").Replace("_test.png", ".txt");
                    if (!File.Exists(annot))
                    {
                        var parent  = Path.GetDirectoryName(Path.GetDirectoryName(img) ?? "") ?? "";
                        var groupId = Path.GetFileName(Path.GetDirectoryName(img) ?? "");
                        var notDir  = Path.Combine(parent, groupId + "_not");
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
                            category    = cat,
                            image       = Path.GetFileName(imgPath),
                            caseId      = cf.CaseId,
                            defectType  = cf.NormalizedDefect?.DefectType ?? "",
                            severity    = cf.NormalizedDefect?.Severity ?? "",
                            status      = cf.Status.ToString(),
                            humanReview = cf.HumanReviewRequired,
                            actions     = cf.AgentActions.Count,
                            violations  = cf.PolicyViolations.Count,
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

        // ---- GET /api/images ----
        private List<ImageEntry> GetAvailableImages()
        {
            var results = new List<ImageEntry>();
            if (!Directory.Exists(_datasetRoot)) return results;

            var testJpgs = Directory.GetFiles(_datasetRoot, "*_test.jpg", SearchOption.AllDirectories).Take(120).ToArray();
            foreach (var p in testJpgs)
            {
                var category = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(p)) ?? "") ?? "";
                results.Add(new ImageEntry { Path = p, Label = Path.GetFileName(p), Category = category });
            }

            // MVTec fallback
            if (results.Count == 0)
            {
                foreach (var catDir in Directory.GetDirectories(_datasetRoot).Take(16))
                {
                    var testDir = Path.Combine(catDir, "test");
                    if (!Directory.Exists(testDir)) continue;
                    var pngs = Directory.GetFiles(testDir, "*.png", SearchOption.AllDirectories).Take(5);
                    foreach (var p in pngs)
                        results.Add(new ImageEntry { Path = p, Label = Path.GetFileName(p), Category = Path.GetFileName(catDir) });
                }
            }

            return results.Take(100).ToList();
        }

        // ---- Static file serving (dashboard/dist/) ----

        private static readonly string _distRoot = FindDistRoot();

        private static string FindDistRoot()
        {
            // 1. CWD (works with `dotnet run` from project root)
            var cwd = Path.Combine(Environment.CurrentDirectory, "dashboard", "dist");
            if (Directory.Exists(cwd)) return cwd;

            // 2. Walk up from the exe location (works when published)
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, "dashboard", "dist");
                if (Directory.Exists(candidate)) return candidate;
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            return cwd; // return CWD-based path as default (will show helpful error)
        }

        private static readonly Dictionary<string, string> _mimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".html"]  = "text/html; charset=utf-8",
            [".js"]    = "application/javascript; charset=utf-8",
            [".mjs"]   = "application/javascript; charset=utf-8",
            [".css"]   = "text/css; charset=utf-8",
            [".json"]  = "application/json; charset=utf-8",
            [".png"]   = "image/png",
            [".jpg"]   = "image/jpeg",
            [".jpeg"]  = "image/jpeg",
            [".svg"]   = "image/svg+xml",
            [".ico"]   = "image/x-icon",
            [".woff"]  = "font/woff",
            [".woff2"] = "font/woff2",
            [".txt"]   = "text/plain; charset=utf-8",
        };

        private async Task ServeStaticFile(HttpListenerResponse res, string urlPath)
        {
            if (!Directory.Exists(_distRoot))
            {
                res.StatusCode = 503;
                await WriteJson(res, new
                {
                    error = "Dashboard not built yet.",
                    fix   = "Run: cd dashboard && npm install && npm run build",
                    distPath = _distRoot
                });
                return;
            }

            // Map URL path to a file inside dist/
            var relative = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(relative))
                relative = "index.html";

            var filePath = Path.GetFullPath(Path.Combine(_distRoot, relative));
            var distRootFull = Path.GetFullPath(_distRoot);

            // Security: prevent path traversal outside dist/
            if (!filePath.StartsWith(distRootFull, StringComparison.OrdinalIgnoreCase))
            {
                res.StatusCode = 403;
                await WriteJson(res, new { error = "Forbidden" });
                return;
            }

            // SPA fallback: unknown paths serve index.html so React Router works
            if (!File.Exists(filePath))
                filePath = Path.Combine(_distRoot, "index.html");

            if (!File.Exists(filePath))
            {
                res.StatusCode = 404;
                await WriteJson(res, new { error = "Not found", path = urlPath });
                return;
            }

            var ext = Path.GetExtension(filePath);
            res.ContentType = _mimeTypes.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
            res.StatusCode = 200;

            var bytes = await File.ReadAllBytesAsync(filePath);
            res.ContentLength64 = bytes.Length;
            try
            {
                await res.OutputStream.WriteAsync(bytes);
                res.OutputStream.Close();
            }
            catch { /* client disconnected */ }
        }

        // ---- JSON writer ----
        private static async Task WriteJson(HttpListenerResponse res, object obj)
        {
            res.ContentType = "application/json; charset=utf-8";
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = bytes.Length;
            try
            {
                await res.OutputStream.WriteAsync(bytes);
                res.OutputStream.Close();
            }
            catch { /* client disconnected */ }
        }

        public class ImageEntry
        {
            public string Path     { get; set; } = "";
            public string Label    { get; set; } = "";
            public string Category { get; set; } = "";
        }
    }
}
