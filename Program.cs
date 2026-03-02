using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ManufacturingKnowledgeGraph
{
    class Program
    {
        private static KnowledgeGraph graph;

        static async Task Main(string[] args)
        {
            PrintBanner();

            // ===== CONFIGURATION (from appsettings.json / env vars) =====
            string azureEndpoint = AppConfig.VisionEndpoint;
            string azureKey = AppConfig.VisionKey;

            string mvtecPath = args.Length > 0
                ? args[0]
                : GetDatasetPath();

            string cacheFile = "knowledge_graph.json";

            Console.WriteLine($"📍 Azure Endpoint: {azureEndpoint.Substring(0, Math.Min(50, azureEndpoint.Length))}...");
            Console.WriteLine($"📂 Dataset Path: {mvtecPath}");
            Console.WriteLine($"💾 Cache File: {cacheFile}\n");

            // ===== CHECK FOR CACHED GRAPH =====
            if (KnowledgeGraph.CacheExists(cacheFile))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ Found cached knowledge graph!");
                Console.ResetColor();
                Console.WriteLine("\nOptions:");
                Console.WriteLine("  1. Load from cache (instant) ⚡");
                Console.WriteLine("  2. Rebuild from scratch (10-15 min) 🔄");
                Console.WriteLine("  3. Exit");
                Console.Write("\nSelect option (1-3): ");

                var choice = Console.ReadLine();

                if (choice == "1")
                {
                    // Load from cache
                    graph = KnowledgeGraph.LoadFromFile(cacheFile);

                    if (graph == null)
                    {
                        Console.WriteLine("❌ Failed to load cache. Rebuilding...");
                        await BuildNewGraph(azureEndpoint, azureKey, mvtecPath, cacheFile);
                    }
                }
                else if (choice == "2")
                {
                    // Rebuild
                    await BuildNewGraph(azureEndpoint, azureKey, mvtecPath, cacheFile);
                }
                else
                {
                    Console.WriteLine("👋 Goodbye!");
                    return;
                }
            }
            else
            {
                // No cache exists - must build
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️  No cached graph found. Will build from scratch.");
                Console.ResetColor();
                Console.WriteLine("⏳ This takes 10-15 minutes due to API rate limits");
                Console.Write("\nContinue? (y/n): ");

                if (Console.ReadLine()?.ToLower() != "y")
                {
                    Console.WriteLine("Exiting...");
                    return;
                }

                await BuildNewGraph(azureEndpoint, azureKey, mvtecPath, cacheFile);
            }

            Console.WriteLine("\n✅ Knowledge graph ready!\n");

            // ===== INTERACTIVE MENU =====
            await RunInteractiveMenu();
        }

        // Helper method to build graph — supports both DeepPCB and MVTec datasets
        static async Task BuildNewGraph(string endpoint, string key, string datasetPath, string cacheFile)
        {
            graph = new KnowledgeGraph();

            // Detect dataset type from path
            bool isDeepPCB = datasetPath.IndexOf("DeepPCB", StringComparison.OrdinalIgnoreCase) >= 0
                          || datasetPath.IndexOf("PCBData", StringComparison.OrdinalIgnoreCase) >= 0
                          || Directory.Exists(Path.Combine(datasetPath, "PCBData"));

            if (isDeepPCB)
            {
                Console.WriteLine("\n🔍 Building knowledge graph from DeepPCB dataset...\n");
                var processor = new DeepPCBProcessor(graph);
                await processor.ProcessDataset(datasetPath, maxImages: 50);
            }
            else
            {
                Console.WriteLine("\n🔍 Building knowledge graph from MVTec dataset...\n");
                var analyzer = new AzureVisionAnalyzer(endpoint, key);
                var builder = new GraphBuilder(graph, analyzer);
                await builder.ProcessMVTecDataset(datasetPath, maxImagesPerProduct: 2);
            }

            // Save to cache
            graph.SaveToFile(cacheFile);
        }

        static async Task RunInteractiveMenu()
        {
            while (true)
            {
                Console.WriteLine("\n" + new string('═', 70));

                // Show statistics
                Console.WriteLine("📊 KNOWLEDGE GRAPH STATISTICS");
                Console.WriteLine(new string('═', 70));
                graph.PrintGraph();

                Console.WriteLine("\n\n🔍 INTERACTIVE QUERY MENU  [DeepPCB · 1500 images · 6 defect types]");
                Console.WriteLine(new string('═', 70));
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  ── 📊 KNOWLEDGE GRAPH ──────────────────────────────────────────");
                Console.ResetColor();
                Console.WriteLine("  1.  📊 PCB defect statistics (by category)");
                Console.WriteLine("  2.  🔗 Find co-occurring defects  (NOVEL!)");
                Console.WriteLine("  3.  🔧 Equipment recommendations by defect type");
                Console.WriteLine("  4.  🗂️  Browse defects by category");
                Console.WriteLine("  5.  🎯 Custom defect search");
                Console.WriteLine("  6.  📈 Generate visual diagram");
                Console.WriteLine("  7.  💾 Export graph to file");
                Console.WriteLine("  8.  🤖 AI-generated insights (GPT-4.1)");
                Console.WriteLine("  9.  📊 VIEW COMPLETE DASHBOARD WITH VISUALIZATIONS ⭐");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  ── 💾 CACHE MANAGEMENT ─────────────────────────────────────────");
                Console.ResetColor();
                Console.WriteLine("  10. 💾 Save graph to cache");
                Console.WriteLine("  11. 🔄 Rebuild graph from DeepPCB dataset");
                Console.WriteLine("  12. 🗑️  Delete cache");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  ── 🏭 MCP INSPECTION PIPELINE ──────────────────────────────────");
                Console.ResetColor();
                Console.WriteLine("  13. 🏭 MCP Pipeline — single image  ⭐");
                Console.WriteLine("  14. 🔁 MCP Pipeline — batch (N images per category)  🆕");
                Console.WriteLine("  15. 📊 Run full evaluation suite");
                Console.WriteLine("  16. 📂 View / export case report");
                Console.WriteLine("  17. 📋 Compare evaluation results  🆕");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  ── ❌ ────────────────────────────────────────────────────────────");
                Console.ResetColor();
                Console.WriteLine("  18. ❌ Exit");
                Console.WriteLine(new string('═', 70));

                Console.Write("\n👉 Select option (1-18): ");
                var choice = Console.ReadLine();

                Console.WriteLine();

                switch (choice)
                {
                    case "1":
                        await QueryDefectsByCategory();
                        break;
                    case "2":
                        await FindCoOccurringDefects();
                        break;
                    case "3":
                        await ShowEquipmentRecommendations();
                        break;
                    case "4":
                        await BrowseDefectCategories();
                        break;
                    case "5":
                        await CustomDefectSearch();
                        break;
                    case "6":
                        await GenerateVisualDiagram();
                        break;
                    case "7":
                        await ExportResults();
                        break;
                    case "8":
                        await ShowSampleInsights();
                        break;
                    case "9":
                        await ShowCompleteDashboard();
                        break;
                    case "10":
                        await SaveGraphCache();
                        break;
                    case "11":
                        await RebuildGraph();
                        break;
                    case "12":
                        await DeleteCache();
                        break;
                    case "13":
                        await RunMcpInspection();
                        break;
                    case "14":
                        await RunBatchMcpInspection();
                        break;
                    case "15":
                        await RunEvaluationSuite();
                        break;
                    case "16":
                        await ViewOrExportCase();
                        break;
                    case "17":
                        await CompareEvaluationResults();
                        break;
                    case "18":
                        Console.WriteLine("👋 Goodbye!");
                        return;
                    default:
                        Console.WriteLine("❌ Invalid option. Try again.");
                        break;
                }

                Console.WriteLine("\n\nPress any key to return to menu...");
                Console.ReadKey();
            }
        }

        // Kept for compatibility — redirects to new PCB-aware method
        static async Task QueryDefectsByProduct() => await QueryDefectsByCategory();

        static async Task QueryDefectsByCategory()
        {
            Console.WriteLine("📊 QUERY: PCB Defect Statistics by Category");
            Console.WriteLine(new string('─', 70));
            Console.WriteLine("DeepPCB categories: open, short, mousebite, spur, pin_hole, spurious_copper");
            Console.WriteLine("(Press Enter to show ALL categories)");
            Console.Write("\nEnter category name (or leave blank for all): ");
            var categoryName = Console.ReadLine()?.Trim().ToLower();

            List<Node> defects;
            if (string.IsNullOrEmpty(categoryName))
            {
                defects = graph.GetNodesByType("defect");
            }
            else
            {
                defects = graph.QueryDefectsByProduct(categoryName);
                if (!defects.Any())
                {
                    // Try as defect name substring
                    defects = graph.GetNodesByType("defect")
                        .Where(d => d.Properties.ContainsKey("name") &&
                                    d.Properties["name"].ToString()!.ToLower().Contains(categoryName))
                        .ToList();
                }
            }

            if (!defects.Any())
            {
                if (!string.IsNullOrEmpty(categoryName))
                {
                    Console.WriteLine($"\n❌ No '{categoryName}' defects in the current sample ({graph.GetNodesByType("image").Count} images loaded).");
                    Console.WriteLine("💡 This defect type is absent from the loaded sample — not a bug.");
                    Console.WriteLine("   Rebuild the graph with more images (option 11) for broader coverage.");
                    Console.WriteLine("   Valid DeepPCB categories: open, short, mousebite, spur, pin_hole, spurious_copper");
                }
                return;
            }

            // All 6 canonical DeepPCB defect types — always shown even if count = 0
            var allPcbCategories = new[] { "open", "short", "mousebite", "spur", "pin_hole", "spurious_copper" };

            // Group by category
            var grouped = defects
                .GroupBy(d => d.Properties.ContainsKey("product") ? d.Properties["product"]?.ToString() ?? "unknown" : "unknown")
                .ToDictionary(g => g.Key, g => g.ToList());

            // Build display rows
            var totalShown = defects.Count;
            Console.WriteLine($"\n✅ {(string.IsNullOrEmpty(categoryName) ? "All categories" : $"Category '{categoryName}'")} — {totalShown} defect record(s) in {graph.GetNodesByType("image").Count} loaded images:\n");

            var rows = new List<string[]>();

            // Decide ordered keys: requested category only, or all 6 + any extras
            IEnumerable<string> keys = string.IsNullOrEmpty(categoryName)
                ? allPcbCategories.Concat(grouped.Keys.Except(allPcbCategories)).ToList()
                : grouped.Keys.ToList();

            foreach (var key in keys)
            {
                if (!grouped.TryGetValue(key, out var grpItems) || grpItems == null || !grpItems.Any())
                {
                    // Show 0-count row only when showing all (blank input)
                    if (string.IsNullOrEmpty(categoryName))
                        rows.Add(new[] { key, "0", "— not in current sample —" });
                    continue;
                }
                var severityBreakdown = grpItems
                    .GroupBy(d => d.Properties.ContainsKey("severity") ? d.Properties["severity"]?.ToString() ?? "?" : "?")
                    .OrderByDescending(s => s.Count())
                    .Select(s => $"{s.Key}:{s.Count()}")
                    .ToList();
                rows.Add(new[] { key, grpItems.Count.ToString(), string.Join(", ", severityBreakdown) });
            }

            PrintTable(new[] { "Defect Category", "Count", "Severity Breakdown" }, rows);

            await Task.CompletedTask;
        }

        static async Task ShowEquipmentRecommendations()
        {
            Console.WriteLine("🔧 QUERY: Equipment Recommendations for Defects");
            Console.WriteLine(new string('─', 70));

            var equipment = graph.GetEquipmentRecommendations();

            if (!equipment.Any())
            {
                Console.WriteLine("❌ No equipment recommendations available.");
                return;
            }

            Console.WriteLine($"\n✅ Equipment recommendations for {equipment.Count} defect types:\n");

            var rows = new List<string[]>();
            foreach (var kvp in equipment.Take(15))
            {
                rows.Add(new[]
                {
                    kvp.Key,
                    string.Join(", ", kvp.Value.Distinct())
                });
            }

            PrintTable(new[] { "Defect Type", "Required Equipment" }, rows);

            await Task.CompletedTask;
        }

        // Kept for compatibility
        static async Task FindSimilarDefects() => await FindCoOccurringDefects();

        static async Task FindCoOccurringDefects()
        {
            Console.WriteLine("🔗 QUERY: Co-Occurring Defects in PCB Inspection (CROSS-CATEGORY INTELLIGENCE)");
            Console.WriteLine(new string('─', 70));
            Console.WriteLine("Finds PCB defect pairs that tend to appear together — enables combined repair protocols!\n");

            // Build co-occurrence map: for each image, collect its distinct defect types
            var images = graph.GetNodesByType("image");
            var allRels = graph.AllRelationships;
            var allNodes = graph.AllNodes;

            // pair → count of images containing both defect types
            var coOccurrenceCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var pairExamples = new Dictionary<string, (string a, string b)>(StringComparer.OrdinalIgnoreCase);

            foreach (var img in images)
            {
                // Get all defect types in this image
                var defectTypes = allRels
                    .Where(r => r.FromNodeId == img.Id && r.RelationType == "has_defect")
                    .Select(r => allNodes.FirstOrDefault(n => n.Id == r.ToNodeId))
                    .Where(n => n != null && n!.Properties.ContainsKey("name"))
                    .Select(n => n!.Properties["name"]?.ToString() ?? "")
                    .Where(s => s.Length > 0)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                // Generate all pairs
                for (int i = 0; i < defectTypes.Count; i++)
                    for (int j = i + 1; j < defectTypes.Count; j++)
                    {
                        var key = $"{defectTypes[i]} + {defectTypes[j]}";
                        coOccurrenceCount[key] = coOccurrenceCount.GetValueOrDefault(key, 0) + 1;
                        pairExamples[key] = (defectTypes[i], defectTypes[j]);
                    }
            }

            if (!coOccurrenceCount.Any())
            {
                Console.WriteLine("❌ No co-occurring defect pairs found in this sample.");
                Console.WriteLine("💡 Try rebuilding with more images (option 11) to see patterns.");
                return;
            }

            var topPairs = coOccurrenceCount
                .OrderByDescending(kv => kv.Value)
                .Take(8)
                .ToList();

            Console.WriteLine($"✅ Found {coOccurrenceCount.Count} co-occurring defect pairs across {images.Count} PCB images:\n");

            // Get AI insight for top pairs
            var endpoint   = AppConfig.OpenAIEndpoint;
            var apiKey     = AppConfig.OpenAIKey;
            var deployment = AppConfig.OpenAIDeployment;
            var apiVersion = AppConfig.OpenAIApiVersion;

            var rows = new List<string[]>();
            foreach (var kv in topPairs)
            {
                var (typeA, typeB) = pairExamples[kv.Key];
                string insight = "";
                try
                {
                    var requestBody = new
                    {
                        messages = new[]
                        {
                            new { role = "system", content = "You are a PCB manufacturing quality expert. Give a ONE sentence insight about why two defect types co-occur and what single process fix addresses both. Plain text only, no markdown." },
                            new { role = "user", content = $"PCB defects '{typeA}' and '{typeB}' appear together in {kv.Value} boards. Why do they co-occur and what is the combined fix?" }
                        },
                        max_tokens = 80,
                        temperature = 0.5
                    };
                    var reqJson = JsonSerializer.Serialize(requestBody);
                    var reqUrl  = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                    client.DefaultRequestHeaders.Add("api-key", apiKey);
                    var resp = await client.PostAsync(reqUrl, new StringContent(reqJson, Encoding.UTF8, "application/json"));
                    if (resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(body);
                        insight = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                        insight = insight.Replace("**","").Replace("##","").Trim();
                    }
                    else insight = $"API {resp.StatusCode}";
                }
                catch (Exception ex) { insight = ex.Message[..Math.Min(40, ex.Message.Length)]; }

                rows.Add(new[] { typeA, typeB, kv.Value.ToString(), insight });
            }

            PrintTable(new[] { "Defect A", "Defect B", "Co-occur in N images", "Combined Fix Insight" }, rows);

            Console.WriteLine("\n💡 KEY INSIGHT: Co-occurring PCB defects (e.g. open + short) share root causes!");
            Console.WriteLine("   A single solder process fix can eliminate multiple defect types simultaneously.");

            await Task.CompletedTask;
        }

        // Kept for compatibility
        static async Task ShowAllProducts() => await BrowseDefectCategories();

        static async Task BrowseDefectCategories()
        {
            Console.WriteLine("🗂️  BROWSE: PCB Defect Categories (DeepPCB Dataset)");
            Console.WriteLine(new string('─', 70));

            var images = graph.GetNodesByType("image");
            var defects = graph.GetNodesByType("defect");

            // Group by PCB defect category (stored in "product" property by DeepPCBProcessor)
            var categories = defects
                .Select(d => d.Properties.ContainsKey("product") ? d.Properties["product"]?.ToString() ?? "unknown" : "unknown")
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            Console.WriteLine($"\n✅ Found {categories.Count} defect categories across {images.Count} PCB images:\n");

            var rows = new List<string[]>();
            foreach (var cat in categories)
            {
                var catDefects = defects.Where(d =>
                    d.Properties.ContainsKey("product") && d.Properties["product"]?.ToString() == cat).ToList();
                var catImages = images.Where(img =>
                    img.Properties.ContainsKey("product") && img.Properties["product"]?.ToString() == cat).ToList();

                var highSev = catDefects.Count(d =>
                    d.Properties.ContainsKey("severity") && d.Properties["severity"]?.ToString() == "high");
                var pct = catDefects.Count > 0 ? (double)highSev / catDefects.Count * 100 : 0;

                rows.Add(new[]
                {
                    cat,
                    catImages.Count.ToString(),
                    catDefects.Count.ToString(),
                    $"{pct:F0}% high"
                });
            }

            PrintTable(new[] { "Defect Category", "PCB Images", "Defect Records", "High Severity" }, rows);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  DeepPCB defect codes: open=0  short=1  mousebite=2  spur=3  pin_hole=4  spurious_copper=5");
            Console.ResetColor();

            await Task.CompletedTask;
        }

        static async Task CustomDefectSearch()
        {
            Console.WriteLine("🎯 QUERY: Custom Defect Search");
            Console.WriteLine(new string('─', 70));

            Console.Write("Enter defect type (e.g., crack, scratch, bent, hole): ");
            var defectType = Console.ReadLine()?.ToLower();

            var allDefects = graph.GetNodesByType("defect");
            var matches = allDefects
                .Where(d => d.Properties["name"].ToString().ToLower().Contains(defectType))
                .ToList();

            if (!matches.Any())
            {
                Console.WriteLine($"\n❌ No defects matching '{defectType}' found.");
                return;
            }

            Console.WriteLine($"\n✅ Found {matches.Count} defects matching '{defectType}':\n");

            var rows = matches.Select(d => new[]
            {
                d.Properties["name"].ToString(),
                d.Properties["product"].ToString(),
                d.Properties["severity"].ToString()
            }).ToList();

            PrintTable(new[] { "Defect Type", "Product", "Severity" }, rows);

            await Task.CompletedTask;
        }

        static async Task GenerateVisualDiagram()
        {
            Console.WriteLine("📊 VISUAL DIAGRAM: Knowledge Graph — Real Nodes & Edges");
            Console.WriteLine(new string('─', 70));

            var allNodes = graph.AllNodes;
            var allRels = graph.AllRelationships;

            // ── Summary bar ──
            var nodesByType = allNodes.GroupBy(n => n.Type)
                .ToDictionary(g => g.Key, g => g.Count());
            var relsByType = allRels.GroupBy(r => r.RelationType)
                .ToDictionary(g => g.Key, g => g.Count());

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│                 KNOWLEDGE GRAPH — LIVE DATA                     │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
            Console.ResetColor();
            Console.WriteLine($"  Nodes: {allNodes.Count}  |  Edges: {allRels.Count}");
            Console.Write("  Types: ");
            foreach (var kvp in nodesByType.OrderByDescending(x => x.Value))
                Console.Write($"{kvp.Key}({kvp.Value})  ");
            Console.WriteLine();
            Console.Write("  Edges: ");
            foreach (var kvp in relsByType.OrderByDescending(x => x.Value))
                Console.Write($"{kvp.Key}({kvp.Value})  ");
            Console.WriteLine("\n");

            // ── Per-product subgraph: image → defects → equipment → standards ──
            var images = graph.GetNodesByType("image");
            var products = images
                .Select(img => img.Properties.ContainsKey("product") ? img.Properties["product"]?.ToString() ?? "" : "")
                .Where(p => p.Length > 0)
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"─── NODE → EDGE → NODE (by product, showing real data) ───");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var product in products)
            {
                var productImages = images
                    .Where(n => n.Properties.ContainsKey("product") && n.Properties["product"]?.ToString() == product)
                    .ToList();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  📦 Product: {product.ToUpper()} ({productImages.Count} image(s))");
                Console.ResetColor();

                foreach (var img in productImages)
                {
                    var imgName = img.Properties.ContainsKey("source") ? img.Properties["source"]?.ToString() : img.Id;
                    Console.WriteLine($"  │");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"  ├─📷 {imgName}");
                    Console.ResetColor();

                    // Find all outgoing edges from this image node
                    var outEdges = allRels.Where(r => r.FromNodeId == img.Id).ToList();

                    for (int e = 0; e < outEdges.Count; e++)
                    {
                        var edge = outEdges[e];
                        var targetNode = graph.GetNodeById(edge.ToNodeId);
                        if (targetNode == null) continue;

                        var isLast = (e == outEdges.Count - 1);
                        var branch = isLast ? "└" : "├";
                        var cont = isLast ? " " : "│";

                        // Icon by target type
                        var icon = targetNode.Type switch
                        {
                            "defect" => "🔴",
                            "equipment" => "🔧",
                            "standard" => "📋",
                            _ => "⬜"
                        };

                        var targetName = targetNode.Properties.ContainsKey("name")
                            ? targetNode.Properties["name"]?.ToString() ?? targetNode.Id
                            : targetNode.Id;

                        var severity = targetNode.Properties.ContainsKey("severity")
                            ? $" [{targetNode.Properties["severity"]}]" : "";

                        var conf = edge.Confidence > 0 ? $" (conf: {edge.Confidence:F2})" : "";

                        Console.Write($"  │  {branch}──");
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write($"{edge.RelationType}");
                        Console.ResetColor();
                        Console.WriteLine($"──► {icon} {targetName}{severity}{conf}");

                        // Second-level edges from the target (defect → equipment, defect → standard)
                        var subEdges = allRels.Where(r => r.FromNodeId == targetNode.Id).ToList();
                        foreach (var sub in subEdges)
                        {
                            var subTarget = graph.GetNodeById(sub.ToNodeId);
                            if (subTarget == null) continue;

                            var subIcon = subTarget.Type switch
                            {
                                "equipment" => "🔧",
                                "standard" => "📋",
                                "defect" => "🔴",
                                _ => "⬜"
                            };
                            var subName = subTarget.Properties.ContainsKey("name")
                                ? subTarget.Properties["name"]?.ToString() ?? subTarget.Id
                                : subTarget.Id;

                            Console.Write($"  │  {cont}       └──");
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write($"{sub.RelationType}");
                            Console.ResetColor();
                            Console.WriteLine($"──► {subIcon} {subName}");
                        }
                    }
                }
                Console.WriteLine();
            }

            // ── Cross-product similar_defect edges ──
            var crossEdges = graph.FindSimilarDefectsAcrossProducts();
            if (crossEdges.Any())
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"─── CROSS-PRODUCT EDGES ({crossEdges.Count} similar-defect links) ───");
                Console.ResetColor();
                Console.WriteLine();

                foreach (var (d1, d2, relType) in crossEdges.Take(15))
                {
                    var name1 = d1.Properties.ContainsKey("name") ? d1.Properties["name"]?.ToString() : d1.Id;
                    var name2 = d2.Properties.ContainsKey("name") ? d2.Properties["name"]?.ToString() : d2.Id;
                    var prod1 = d1.Properties.ContainsKey("product") ? d1.Properties["product"]?.ToString() : "?";
                    var prod2 = d2.Properties.ContainsKey("product") ? d2.Properties["product"]?.ToString() : "?";

                    Console.Write($"  🔴 {name1}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" ({prod1})");
                    Console.ResetColor();
                    Console.Write($"  ◄──{relType}──►  ");
                    Console.Write($"🔴 {name2}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($" ({prod2})");
                    Console.ResetColor();
                }
                if (crossEdges.Count > 15)
                    Console.WriteLine($"  ... and {crossEdges.Count - 15} more");
                Console.WriteLine();
            }

            // ── Equipment hub: which equipment node has most inbound edges ──
            var equipmentNodes = graph.GetNodesByType("equipment");
            if (equipmentNodes.Any())
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("─── EQUIPMENT HUBS (most-connected nodes) ───");
                Console.ResetColor();
                Console.WriteLine();

                var eqWithCounts = equipmentNodes
                    .Select(eq => new
                    {
                        Node = eq,
                        Name = eq.Properties.ContainsKey("name") ? eq.Properties["name"]?.ToString() : eq.Id,
                        InboundCount = allRels.Count(r => r.ToNodeId == eq.Id)
                    })
                    .OrderByDescending(x => x.InboundCount)
                    .ToList();

                foreach (var eq in eqWithCounts)
                {
                    var bar = new string('█', Math.Min(eq.InboundCount, 40));
                    Console.Write($"  🔧 {eq.Name,-35}");
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write($" {bar}");
                    Console.ResetColor();
                    Console.WriteLine($" ({eq.InboundCount} edges)");
                }
                Console.WriteLine();
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│  All data above is live from the knowledge graph — not static! │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
            Console.ResetColor();

            await Task.CompletedTask;
        }

        static async Task ExportResults()
        {
            Console.WriteLine("💾 EXPORT: Save Results to File");
            Console.WriteLine(new string('─', 70));

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"KnowledgeGraph_Export_{timestamp}.txt";

            var sb = new StringBuilder();
            sb.AppendLine("MANUFACTURING KNOWLEDGE GRAPH - EXPORT REPORT");
            sb.AppendLine($"Generated: {DateTime.Now}");
            sb.AppendLine(new string('=', 70));
            sb.AppendLine();

            // Statistics
            var images = graph.GetNodesByType("image");
            var defects = graph.GetNodesByType("defect");
            var equipment = graph.GetNodesByType("equipment");

            sb.AppendLine("STATISTICS:");
            sb.AppendLine($"  Total Images Analyzed: {images.Count}");
            sb.AppendLine($"  Total Defects Found: {defects.Count}");
            sb.AppendLine($"  Equipment Types: {equipment.Count}");
            sb.AppendLine();

            // Products
            var products = images
                .Select(img => img.Properties["product"].ToString())
                .Distinct()
                .OrderBy(p => p);

            sb.AppendLine("PRODUCTS ANALYZED:");
            foreach (var product in products)
            {
                var defectCount = graph.QueryDefectsByProduct(product).Count;
                sb.AppendLine($"  - {product}: {defectCount} defect types");
            }
            sb.AppendLine();

            // Equipment recommendations
            var equipmentRecs = graph.GetEquipmentRecommendations();
            sb.AppendLine("EQUIPMENT RECOMMENDATIONS:");
            foreach (var kvp in equipmentRecs.Take(10))
            {
                sb.AppendLine($"  {kvp.Key} → {string.Join(", ", kvp.Value.Distinct())}");
            }
            sb.AppendLine();

            // Similar defects
            var similarities = graph.FindSimilarDefectsAcrossProducts();
            sb.AppendLine("CROSS-PRODUCT PATTERNS (NOVEL INSIGHT):");
            foreach (var (d1, d2, _) in similarities.Take(10))
            {
                sb.AppendLine($"  {d1.Properties["name"]} ({d1.Properties["product"]}) ↔ " +
                              $"{d2.Properties["name"]} ({d2.Properties["product"]})");
            }

            File.WriteAllText(filename, sb.ToString());

            Console.WriteLine($"\n✅ Results exported to: {filename}");
            Console.WriteLine($"📂 Location: {Path.GetFullPath(filename)}");

            await Task.CompletedTask;
        }

        static async Task ShowSampleInsights()
        {
            Console.WriteLine("🔬 SAMPLE INSIGHTS: AI-Generated Manufacturing Intelligence");
            Console.WriteLine(new string('─', 70));
            Console.WriteLine();

            var similarities = graph.FindSimilarDefectsAcrossProducts();
            var equipment = graph.GetEquipmentRecommendations();
            var products = graph.GetNodesByType("image")
                .Select(img => img.Properties["product"].ToString())
                .Distinct()
                .ToList();
            var defects = graph.GetNodesByType("defect");

            // Build a summary of the knowledge graph for Azure OpenAI
            var summaryData = new
            {
                TotalProducts = products.Count,
                ProductNames = products,
                TotalDefects = defects.Count,
                CrossProductPatterns = similarities.Count,
                EquipmentTypes = equipment.Count,
                SampleDefects = defects.Take(10).Select(d => new
                {
                    Name = d.Properties.ContainsKey("name") ? d.Properties["name"]?.ToString() : "unknown",
                    Product = d.Properties.ContainsKey("product") ? d.Properties["product"]?.ToString() : "unknown",
                    Severity = d.Properties.ContainsKey("severity") ? d.Properties["severity"]?.ToString() : "unknown"
                })
            };

            Console.WriteLine("🤖 Querying Azure OpenAI for manufacturing insights...\n");

            var endpoint = AppConfig.OpenAIEndpoint;
            var apiKey = AppConfig.OpenAIKey;
            var deploymentName = AppConfig.OpenAIDeployment;
            var apiVersion = AppConfig.OpenAIApiVersion;

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "system", content = "You are a manufacturing quality expert. Analyze the knowledge graph data and provide 4 actionable business insights. Format each as: INSIGHT #N: Title, then a brief explanation with an actionable recommendation. Do not use markdown formatting like asterisks, bold, or headers. Use plain text only." },
                    new { role = "user", content = $"Analyze this manufacturing knowledge graph and provide 4 key insights:\n\n{JsonSerializer.Serialize(summaryData)}\n\nProvide insights about: 1) Cross-product knowledge transfer 2) Equipment optimization 3) Manufacturing knowledge base value 4) Compliance & standards" }
                },
                max_tokens = 1000,
                temperature = 0.7
            };

            var jsonData = JsonSerializer.Serialize(requestBody);
            var requestUrl = $"{endpoint}/openai/deployments/{deploymentName}/chat/completions?api-version={apiVersion}";

            try
            {
                using var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
                client.DefaultRequestHeaders.Add("api-key", apiKey);

                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(requestUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(apiResponse);
                    var insight = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString() ?? "No insights available";

                    // Strip any residual markdown formatting
                    insight = insight.Replace("**", "").Replace("##", "").Replace("###", "");
                    Console.WriteLine(insight);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"⚠️ API Error: {errorBody}");

                    // Fallback to static insights
                    Console.WriteLine("\n📋 Fallback (static) insights:");
                    Console.WriteLine($"💡 INSIGHT #1: Found {similarities.Count} cross-product defect patterns.");
                    Console.WriteLine($"💡 INSIGHT #2: {equipment.Count} defect types require specific equipment.");
                    Console.WriteLine($"💡 INSIGHT #3: Analyzed {products.Count} product categories automatically.");
                    Console.WriteLine($"💡 INSIGHT #4: All defects linked to ISO 9001 quality standards.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Connection error: {ex.Message}");
                Console.WriteLine("\n📋 Fallback (static) insights:");
                Console.WriteLine($"💡 INSIGHT #1: Found {similarities.Count} cross-product defect patterns.");
                Console.WriteLine($"💡 INSIGHT #2: {equipment.Count} defect types require specific equipment.");
                Console.WriteLine($"💡 INSIGHT #3: Analyzed {products.Count} product categories automatically.");
                Console.WriteLine($"💡 INSIGHT #4: All defects linked to ISO 9001 quality standards.");
            }

            Console.WriteLine("\n" + new string('─', 70));
            Console.WriteLine("✅ Insights generation complete.");
        }

        static async Task RunFlowchartFolderModeAsync()
        {
            Console.WriteLine("\n🧭 Flowchart/Diagram Folder Mode");
            Console.WriteLine("Enter folder path (or press Enter for default: ./datasets/flowcharts):");
            var folder = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(folder))
                folder = Path.Combine(Environment.CurrentDirectory, "datasets", "flowcharts");

            // Also accept "flowchart" (singular) as fallback
            if (!Directory.Exists(folder))
            {
                var altFolder = Path.Combine(Environment.CurrentDirectory, "datasets", "flowchart");
                if (Directory.Exists(altFolder))
                    folder = altFolder;
            }

            if (!Directory.Exists(folder))
            {
                Console.WriteLine($"❌ Folder not found: {folder}");
                return;
            }

            var outputDir = Path.Combine(Environment.CurrentDirectory, "outputs", "flowcharts");
            Directory.CreateDirectory(outputDir);

            var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

            var files = Directory.GetFiles(folder)
                .Where(f => supported.Contains(Path.GetExtension(f)))
                .OrderBy(f => f)
                .ToList();

            if (files.Count == 0)
            {
                Console.WriteLine($"⚠️ No images found in: {folder}");
                return;
            }

            Console.WriteLine($"Found {files.Count} image(s). Processing...\n");

            // Azure OpenAI config (centralized)
            var aiEndpoint = AppConfig.OpenAIEndpoint;
            var aiKey = AppConfig.OpenAIKey;
            var deploymentName = AppConfig.OpenAIDeployment;
            var aiApiVersion = AppConfig.OpenAIApiVersion;

            int ok = 0, fail = 0;
            foreach (var file in files)
            {
                try
                {
                    var result = await FlowchartFolderProcessor.ProcessSingleImageAsync(file);

                    // ── Send merged blocks to Azure OpenAI for classification & detailed caption ──
                    string aiDescription = "";
                    string aiInsights = "";
                    try
                    {
                        // Build a clean summary of merged blocks for the AI
                        var mergedTexts = result.MergedBlocks
                            .Select((b, idx) => $"  Block {idx + 1}: \"{b.Text}\" (x={b.BoundingBoxLeft:F0}, y={b.BoundingBoxTop:F0})")
                            .ToList();

                        var mergedSummary = string.Join("\n", mergedTexts);

                        var requestBody = new
                        {
                            messages = new[]
                            {
                                new { role = "system", content = @"You are a manufacturing process expert. You receive spatially-merged OCR text blocks extracted from a flowchart image. Each block is one box/shape in the diagram, with its (x,y) position.

Your job:
1. Generate a detailed CAPTION describing what this flowchart is about.
2. Classify each block as STEP, DECISION, BRANCH_LABEL, or TERMINAL (start/end).
3. List the properly ordered STEPS (action boxes only).
4. List the complete DECISION questions.
5. List BRANCH_LABELS (Yes/No/etc).

Rules:
- Steps are action/process boxes (rectangles).
- Decisions are diamond-shaped question boxes (usually contain a question mark or verification/check language).
- Branch labels are short connectors like Yes, No.
- Terminals are Start/End/Begin/Stop boxes at the very top or bottom.
- Output plain text only. No markdown, no asterisks, no bold, no headers." },
                                new { role = "user", content = $"Flowchart image: {result.ImageName}\nAzure Vision caption: {result.Caption}\nTags: {string.Join(", ", result.Tags)}\n\nSpatially-merged text blocks (one per flowchart shape):\n{mergedSummary}\n\nRespond in this exact format:\nCAPTION: <detailed description of this flowchart>\nSTEPS:\n1. <step text>\n2. <step text>\n...\nDECISIONS:\n1. <full decision question>\n...\nBRANCH_LABELS: <comma-separated>\nINSIGHTS:\n- <insight 1>\n- <insight 2>\n- <insight 3>" }
                            },
                            max_tokens = 800,
                            temperature = 0.3
                        };

                        var jsonPayload = JsonSerializer.Serialize(requestBody);
                        var requestUrl = $"{aiEndpoint}/openai/deployments/{deploymentName}/chat/completions?api-version={aiApiVersion}";

                        using var httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
                        httpClient.DefaultRequestHeaders.Add("api-key", aiKey);
                        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                        var httpResponse = await httpClient.PostAsync(requestUrl, httpContent);

                        if (httpResponse.IsSuccessStatusCode)
                        {
                            var apiResp = await httpResponse.Content.ReadAsStringAsync();
                            using var aiDoc = JsonDocument.Parse(apiResp);
                            var aiText = aiDoc.RootElement
                                .GetProperty("choices")[0]
                                .GetProperty("message")
                                .GetProperty("content")
                                .GetString() ?? "";

                            // Strip any residual markdown
                            aiText = aiText.Replace("**", "").Replace("##", "").Replace("###", "");

                            // Parse structured AI response
                            string currentSection = "";
                            foreach (var line in aiText.Split('\n'))
                            {
                                var trimmed = line.Trim();
                                if (trimmed.StartsWith("CAPTION:", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.DetailedCaption = trimmed.Substring(8).Trim();
                                    currentSection = "caption";
                                }
                                else if (trimmed.StartsWith("STEPS:", StringComparison.OrdinalIgnoreCase))
                                    currentSection = "steps";
                                else if (trimmed.StartsWith("DECISIONS:", StringComparison.OrdinalIgnoreCase))
                                    currentSection = "decisions";
                                else if (trimmed.StartsWith("BRANCH_LABELS:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var labels = trimmed.Substring(14).Trim();
                                    result.BranchLabels = labels.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                                    currentSection = "branch";
                                }
                                else if (trimmed.StartsWith("INSIGHTS:", StringComparison.OrdinalIgnoreCase))
                                    currentSection = "insights";
                                else if (!string.IsNullOrWhiteSpace(trimmed))
                                {
                                    // Remove leading numbering like "1. " or "- "
                                    var clean = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^\d+\.\s*", "");
                                    clean = clean.TrimStart('-').Trim();

                                    if (currentSection == "steps" && clean.Length > 0)
                                        result.ProcessSteps.Add(clean);
                                    else if (currentSection == "decisions" && clean.Length > 0)
                                        result.DecisionPoints.Add(clean);
                                    else if (currentSection == "insights" && clean.Length > 0)
                                        aiInsights += "- " + clean + "\n";
                                }
                            }

                            aiDescription = result.DetailedCaption ?? result.Caption ?? "";

                            // Display enhanced output
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"\n📊 {result.ImageName}");
                            Console.ResetColor();
                            Console.WriteLine($"   📛 Caption: {aiDescription}");

                            if (result.ProcessSteps.Count > 0)
                            {
                                Console.WriteLine($"   📋 Steps ({result.ProcessSteps.Count}):");
                                for (int s = 0; s < result.ProcessSteps.Count; s++)
                                    Console.WriteLine($"      {s + 1}. {result.ProcessSteps[s]}");
                            }
                            if (result.DecisionPoints.Count > 0)
                            {
                                Console.WriteLine($"   ❓ Decisions ({result.DecisionPoints.Count}):");
                                foreach (var d in result.DecisionPoints)
                                    Console.WriteLine($"      - {d}");
                            }
                            if (result.BranchLabels.Count > 0)
                                Console.WriteLine($"   🔀 Branch Labels: {string.Join(", ", result.BranchLabels)}");

                            if (!string.IsNullOrWhiteSpace(aiInsights))
                            {
                                Console.WriteLine($"   💡 Insights:");
                                foreach (var ins in aiInsights.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                                    Console.WriteLine($"      {ins.Trim()}");
                            }
                        }
                        else
                        {
                            var errBody = await httpResponse.Content.ReadAsStringAsync();
                            Console.WriteLine($"   ⚠️ AI insight error: {httpResponse.StatusCode}");
                        }
                    }
                    catch (Exception aiEx)
                    {
                        Console.WriteLine($"   ⚠️ AI insight skipped: {aiEx.Message}");
                    }

                    var outPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(file) + ".json");
                    var json = System.Text.Json.JsonSerializer.Serialize(
                            result,
                            new System.Text.Json.JsonSerializerOptions
                            {
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            });

                    await File.WriteAllTextAsync(outPath, json);

                    Console.WriteLine($"   ✅ Saved → {Path.GetFileName(outPath)}");
                    ok++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ {Path.GetFileName(file)} failed: {ex.Message}");
                    fail++;
                }
            }

            Console.WriteLine($"\nDone. Success: {ok}, Failed: {fail}");
            Console.WriteLine($"Outputs: {outputDir}");

            // Summary insights across all flowcharts
            if (ok > 0)
            {
                Console.WriteLine($"\n🤖 Generating cross-flowchart insights...\n");
                try
                {
                    var allJsonFiles = Directory.GetFiles(outputDir, "*.json").ToList();
                    var allSteps = new List<string>();
                    var allDecisions = new List<string>();
                    var flowchartNames = new List<string>();

                    foreach (var jf in allJsonFiles)
                    {
                        var jContent = await File.ReadAllTextAsync(jf);
                        using var jDoc = JsonDocument.Parse(jContent);
                        var imgName = jDoc.RootElement.GetProperty("ImageName").GetString() ?? "";

                        // Prefer AI-enhanced caption if available
                        var caption = "";
                        if (jDoc.RootElement.TryGetProperty("DetailedCaption", out var capEl) && capEl.ValueKind == JsonValueKind.String)
                            caption = capEl.GetString() ?? "";
                        if (string.IsNullOrWhiteSpace(caption) && jDoc.RootElement.TryGetProperty("Caption", out var rawCap))
                            caption = rawCap.GetString() ?? "";
                        flowchartNames.Add($"{imgName} ({caption})");

                        // Prefer ProcessSteps over raw Steps
                        var stepsKey = jDoc.RootElement.TryGetProperty("ProcessSteps", out var procStepsEl) && procStepsEl.ValueKind == JsonValueKind.Array && procStepsEl.GetArrayLength() > 0
                            ? "ProcessSteps" : "Steps";
                        if (jDoc.RootElement.TryGetProperty(stepsKey, out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array)
                            foreach (var s in stepsEl.EnumerateArray())
                                allSteps.Add(s.GetString() ?? "");

                        // Prefer DecisionPoints over raw Decisions
                        var decsKey = jDoc.RootElement.TryGetProperty("DecisionPoints", out var decPtsEl) && decPtsEl.ValueKind == JsonValueKind.Array && decPtsEl.GetArrayLength() > 0
                            ? "DecisionPoints" : "Decisions";
                        if (jDoc.RootElement.TryGetProperty(decsKey, out var decsEl) && decsEl.ValueKind == JsonValueKind.Array)
                            foreach (var d in decsEl.EnumerateArray())
                                allDecisions.Add(d.GetString() ?? "");
                    }

                    var summaryBody = new
                    {
                        messages = new[]
                        {
                            new { role = "system", content = "You are a manufacturing process expert. Analyze the combined data from multiple flowchart diagrams and identify common patterns, shared keywords, and overall process insights. The steps and decisions provided have been AI-classified from spatially-merged OCR text. Do not use markdown formatting like asterisks, bold, or headers. Use plain text only." },
                            new { role = "user", content = $"Here are {flowchartNames.Count} flowcharts analyzed:\n{string.Join("\n", flowchartNames.Select((n, i) => $"  {i + 1}. {n}"))}\n\nAll process steps found: {string.Join("; ", allSteps.Distinct().Take(40))}\n\nAll decision points: {string.Join("; ", allDecisions.Distinct().Take(20))}\n\nProvide:\n1) Common themes across all flowcharts\n2) Shared keywords/patterns\n3) Process improvement recommendations" }
                        },
                        max_tokens = 600,
                        temperature = 0.5
                    };

                    var summaryJson = JsonSerializer.Serialize(summaryBody);
                    var summaryUrl = $"{aiEndpoint}/openai/deployments/{deploymentName}/chat/completions?api-version={aiApiVersion}";

                    using var summaryClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
                    summaryClient.DefaultRequestHeaders.Add("api-key", aiKey);
                    var summaryContent = new StringContent(summaryJson, Encoding.UTF8, "application/json");
                    var summaryResp = await summaryClient.PostAsync(summaryUrl, summaryContent);

                    if (summaryResp.IsSuccessStatusCode)
                    {
                        var summaryApiResp = await summaryResp.Content.ReadAsStringAsync();
                        using var summaryDoc = JsonDocument.Parse(summaryApiResp);
                        var summaryText = summaryDoc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString() ?? "No summary available";

                        // Strip any residual markdown formatting
                        summaryText = summaryText.Replace("**", "").Replace("##", "").Replace("###", "");

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("\n───────────────────────────────────────────────────────────────");
                        Console.WriteLine("   🔗 CROSS-FLOWCHART INSIGHTS");
                        Console.WriteLine("───────────────────────────────────────────────────────────────");
                        Console.ResetColor();
                        Console.WriteLine(summaryText);
                    }
                    else
                    {
                        var errBody = await summaryResp.Content.ReadAsStringAsync();
                        Console.WriteLine($"⚠️ Summary API error: {errBody}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Cross-flowchart analysis skipped: {ex.Message}");
                }
            }
        }

        static async Task RunFlowchartKeywordSearch()
        {
            Console.WriteLine("\n🔎 Search inside flowcharts (from outputs/flowcharts JSON)");
            Console.Write("Enter keyword (e.g., approve, inspection, SOP): ");
            var keyword = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                Console.WriteLine("⚠️ Keyword is empty.");
                return;
            }

            var outputDir = Path.Combine(Environment.CurrentDirectory, "outputs", "flowcharts");
            if (!Directory.Exists(outputDir))
            {
                Console.WriteLine($"❌ Output folder not found: {outputDir}");
                Console.WriteLine("Run Option 13 (Flowchart mode) first to generate JSON outputs.");
                return;
            }

            var jsonFiles = Directory.GetFiles(outputDir, "*.json")
                .Where(f => !f.EndsWith(".summary.json", StringComparison.OrdinalIgnoreCase)) // in case you add summaries later
                .OrderBy(f => f)
                .ToList();

            if (jsonFiles.Count == 0)
            {
                Console.WriteLine($"⚠️ No JSON files found in: {outputDir}");
                return;
            }

            int matches = 0;

            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);

                    // Minimal parse: pull out Steps only
                    var doc = System.Text.Json.JsonDocument.Parse(json);

                    bool hit = false;

                    // Search Caption
                    if (doc.RootElement.TryGetProperty("Caption", out var captionEl))
                    {
                        var caption = captionEl.GetString() ?? "";
                        if (caption.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            hit = true;
                    }

                    // Search Tags
                    if (!hit && doc.RootElement.TryGetProperty("Tags", out var tagsEl) &&
                        tagsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var tag in tagsEl.EnumerateArray())
                        {
                            var tagText = tag.GetString() ?? "";
                            if (tagText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            { hit = true; break; }
                        }
                    }

                    // Search Steps
                    if (!hit && doc.RootElement.TryGetProperty("Steps", out var stepsElement) &&
                        stepsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var s in stepsElement.EnumerateArray())
                        {
                            var stepText = s.GetString() ?? "";
                            if (stepText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            { hit = true; break; }
                        }
                    }

                    // Search Decisions
                    if (!hit && doc.RootElement.TryGetProperty("Decisions", out var decisionsEl) &&
                        decisionsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var d in decisionsEl.EnumerateArray())
                        {
                            var dText = d.GetString() ?? "";
                            if (dText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            { hit = true; break; }
                        }
                    }

                    if (hit)
                    {
                        matches++;
                        Console.WriteLine($"✅ {Path.GetFileName(file)}");
                        if (doc.RootElement.TryGetProperty("Steps", out var matchSteps) &&
                            matchSteps.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var s in matchSteps.EnumerateArray())
                            {
                                var stepText = s.GetString() ?? "";
                                if (stepText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"   → \"{stepText}\"");
                                    Console.ResetColor();
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore bad files; keep the demo smooth
                }
            }

            Console.WriteLine(matches == 0
                ? "No matches found."
                : $"Total matches: {matches}");
        }

        // ═══════════════════════════════════════════════════════════════
        //  MCP INSPECTION PIPELINE (menu option 15)
        // ═══════════════════════════════════════════════════════════════

        private static CaseFile? lastCaseFile; // keep last case for viewing

        static async Task RunMcpInspection()
        {
            Console.WriteLine("\n🏭 MCP INSPECTION PIPELINE");
            Console.WriteLine(new string('═', 70));
            Console.WriteLine("This runs a full 6-step MCP pipeline with guardrails on a single image.\n");

            Console.Write("Enter image path (or press Enter for sample): ");
            var inputPath = Console.ReadLine()?.Trim('"');

            string imagePath;
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                // Try DeepPCB dataset first, then MVTec
                var deepPcbRoot = Path.Combine(Environment.CurrentDirectory, "datasets", "DeepPCB");
                var sample = FindFirstTestImage(deepPcbRoot);
                if (sample == null)
                {
                    var mvtecRoot = Path.Combine(Environment.CurrentDirectory, "datasets", "mvtec_anomaly_detection");
                    sample = FindFirstTestImage(mvtecRoot);
                }
                if (sample == null)
                {
                    Console.WriteLine("❌ No sample images found. Place DeepPCB dataset in datasets/DeepPCB/");
                    return;
                }
                imagePath = sample;
                Console.WriteLine($"  Using sample: {Path.GetFileName(imagePath)}");
            }
            else
            {
                imagePath = Path.GetFullPath(inputPath);
            }

            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"❌ File not found: {imagePath}");
                return;
            }

            // Create orchestrator (uses GPT-4.1 vision, not Azure AI Vision SDK)
            var openAIVision = new OpenAIVisionAnalyzer();
            var orchestrator = new McpOrchestrator(openAIVision, graph);

            // Run the pipeline
            var caseFile = await orchestrator.RunInspectionPipeline(imagePath);
            lastCaseFile = caseFile;

            // Display summary
            Console.WriteLine();
            PrintCaseSummary(caseFile);

            // Offer export
            Console.Write("\n💾 Export this case? (y/n): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                Console.Write("Format (json/txt): ");
                var format = Console.ReadLine()?.Trim() ?? "json";
                var path = orchestrator.ExportReport(caseFile, format);
                Console.WriteLine($"✅ Report exported to: {path}");
            }
        }

        static void PrintCaseSummary(CaseFile c)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("┌─────────────────────────────────────────────────────────────────┐");
            Console.WriteLine("│                    INSPECTION CASE SUMMARY                      │");
            Console.WriteLine("└─────────────────────────────────────────────────────────────────┘");
            Console.ResetColor();

            Console.WriteLine($"  Case ID:       {c.CaseId}");
            Console.WriteLine($"  Image:         {Path.GetFileName(c.ImagePath)}");
            Console.WriteLine($"  Product:       {c.ProductType}");

            // Status with color
            Console.Write($"  Status:        ");
            Console.ForegroundColor = c.Status switch
            {
                CaseStatus.Completed => ConsoleColor.Green,
                CaseStatus.ReviewRequired => ConsoleColor.Yellow,
                CaseStatus.Failed => ConsoleColor.Red,
                _ => ConsoleColor.White
            };
            Console.WriteLine(c.Status);
            Console.ResetColor();

            if (c.VisionAnalysis != null)
            {
                Console.WriteLine($"\n  📷 Vision:     \"{c.VisionAnalysis.Caption}\" (conf: {c.VisionAnalysis.Confidence:P0})");
            }

            if (c.NormalizedDefect != null)
            {
                Console.WriteLine($"  🔴 Defect:     {c.NormalizedDefect.DefectType} | {c.NormalizedDefect.Severity} severity | {c.NormalizedDefect.TaxonomyId}");
            }

            if (c.GraphContext != null)
            {
                Console.WriteLine($"  🔗 Context:    {c.GraphContext.RelatedDefectIds.Count} related defects, " +
                                  $"{c.GraphContext.EquipmentIds.Count} equipment, " +
                                  $"{c.GraphContext.HistoricalIncidents.Count} historical incidents");
            }

            if (c.RootCause != null)
            {
                Console.WriteLine($"  🔍 Root Cause: {c.RootCause.ProbableCause}");
                foreach (var a in c.RootCause.Actions.Take(3))
                    Console.WriteLine($"     → [{a.Priority}] {a.Action}");
            }

            if (c.Compliance != null)
            {
                var addressed = c.Compliance.Checklist.Count(x => x.Addressed);
                var total = c.Compliance.Checklist.Count;
                Console.WriteLine($"  📋 Compliance: {c.Compliance.ApplicableStandard} § {c.Compliance.Section} — {addressed}/{total} items addressed");
            }

            if (c.HumanReviewRequired)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n  ⚠ HUMAN REVIEW REQUIRED:");
                foreach (var reason in c.HumanReviewReasons)
                    Console.WriteLine($"    - {reason}");
                Console.ResetColor();
            }

            if (c.PolicyViolations.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  🛡 Policy violations: {c.PolicyViolations.Count}");
                foreach (var v in c.PolicyViolations)
                    Console.WriteLine($"    [{v.Action}] {v.Rule}");
                Console.ResetColor();
            }

            Console.WriteLine($"\n  📝 Trace entries: {c.Trace.Count}");
        }

        static string? FindFirstTestImage(string datasetRoot)
        {
            if (!Directory.Exists(datasetRoot)) return null;

            // Check for DeepPCB structure: PCBData/group{X}/{Y}/{Y}_test.jpg or {Y}.jpg
            var pcbDataDir = Path.Combine(datasetRoot, "PCBData");
            if (Directory.Exists(pcbDataDir))
            {
                // Standard naming: _test suffix
                var testImages = Directory.GetFiles(pcbDataDir, "*_test.jpg", SearchOption.AllDirectories);
                if (testImages.Any()) return testImages.First();
                testImages = Directory.GetFiles(pcbDataDir, "*_test.png", SearchOption.AllDirectories);
                if (testImages.Any()) return testImages.First();

                // This distribution: bare filename (exclude _not / _temp reference images)
                var bareImages = Directory.GetFiles(pcbDataDir, "*.jpg", SearchOption.AllDirectories)
                    .Where(f => {
                        var n = Path.GetFileNameWithoutExtension(f);
                        return !n.EndsWith("_not", StringComparison.OrdinalIgnoreCase)
                            && !n.EndsWith("_temp", StringComparison.OrdinalIgnoreCase);
                    }).ToArray();
                if (bareImages.Any()) return bareImages.First();
            }

            // Also check root directly for DeepPCB _test images
            var rootTestImages = Directory.GetFiles(datasetRoot, "*_test.jpg", SearchOption.AllDirectories);
            if (rootTestImages.Any()) return rootTestImages.First();

            // Fallback: MVTec structure — {product}/test/{defect}/*.png
            foreach (var productDir in Directory.GetDirectories(datasetRoot))
            {
                var testDir = Path.Combine(productDir, "test");
                if (!Directory.Exists(testDir)) continue;
                foreach (var defectDir in Directory.GetDirectories(testDir))
                {
                    if (Path.GetFileName(defectDir).Equals("good", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var img = Directory.GetFiles(defectDir, "*.png").FirstOrDefault();
                    if (img != null) return img;
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  EVALUATION SUITE (menu option 16)
        // ═══════════════════════════════════════════════════════════════

        static async Task RunEvaluationSuite()
        {
            Console.WriteLine("\n📊 EVALUATION MODE");
            Console.WriteLine(new string('═', 70));
            Console.WriteLine("Runs labeled test cases through the MCP pipeline and reports metrics.\n");

            var testCasesFile = Path.Combine(Environment.CurrentDirectory, "test_cases", "expected_labels.json");

            if (!File.Exists(testCasesFile))
            {
                Console.WriteLine($"  ❌ Test cases file not found: {testCasesFile}");
                return;
            }

            var raw = File.ReadAllText(testCasesFile);
            var labels = System.Text.Json.JsonSerializer.Deserialize<List<ExpectedLabel>>(raw,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (labels == null || !labels.Any())
            {
                Console.WriteLine("  ⚠ No test cases found in the labels file.");
                return;
            }

            // Resolve relative paths
            foreach (var label in labels)
                if (!Path.IsPathRooted(label.ImagePath))
                    label.ImagePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, label.ImagePath));

            Console.WriteLine($"  Test cases: {testCasesFile}  ({labels.Count} cases)");
            Console.Write("  How many test cases to run? (Enter for all, or a number): ");
            var countInput = Console.ReadLine()?.Trim();
            int maxCases = string.IsNullOrEmpty(countInput)
                ? labels.Count
                : Math.Clamp(int.TryParse(countInput, out var n) ? n : labels.Count, 1, labels.Count);

            // Slice list upfront so EvaluationRunner only runs the requested count
            if (maxCases < labels.Count)
                labels = labels.Take(maxCases).ToList();

            // Write resolved (and sliced) labels to a temp file for EvaluationRunner
            var resolvedPath = Path.Combine(Path.GetTempPath(), "eval_labels_resolved.json");
            File.WriteAllText(resolvedPath, System.Text.Json.JsonSerializer.Serialize(labels));

            Console.WriteLine($"\n⏳ Running {labels.Count} case(s) — this may take several minutes (1 API call per case)...\n");

            var openAIVision = new OpenAIVisionAnalyzer();
            var orchestrator = new McpOrchestrator(openAIVision, graph);
            var runner = new EvaluationRunner(orchestrator, resolvedPath);

            var report = await runner.RunAsync();

            // Display the report
            EvaluationRunner.DisplayReport(report);

            // Export
            Console.Write("\n💾 Export evaluation report? (y/n): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                var path = EvaluationRunner.ExportReport(report);
                Console.WriteLine($"✅ Report exported to: {path}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  VIEW / EXPORT CASE (menu option 17)
        // ═══════════════════════════════════════════════════════════════

        static async Task ViewOrExportCase()
        {
            Console.WriteLine("\n📂 VIEW / EXPORT CASE REPORT");
            Console.WriteLine(new string('─', 70));

            if (lastCaseFile == null)
            {
                Console.WriteLine("  No recent case in memory.");
                Console.WriteLine("  Options:");
                Console.WriteLine("    1. Load case from file");
                Console.WriteLine("    2. Return to menu");
                Console.Write("\n  Select (1-2): ");
                var choice = Console.ReadLine();

                if (choice == "1")
                {
                    Console.Write("  Enter case JSON file path: ");
                    var filePath = Console.ReadLine()?.Trim('"');
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        try
                        {
                            var json = File.ReadAllText(filePath);
                            lastCaseFile = System.Text.Json.JsonSerializer.Deserialize<CaseFile>(json,
                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            Console.WriteLine("  ✅ Case loaded.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ❌ Error: {ex.Message}");
                            return;
                        }
                    }
                    else
                    {
                        Console.WriteLine("  ❌ File not found.");
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            if (lastCaseFile != null)
            {
                PrintCaseSummary(lastCaseFile);

                Console.WriteLine("\n  Actions:");
                Console.WriteLine("    1. View full trace");
                Console.WriteLine("    2. Export as JSON");
                Console.WriteLine("    3. Export as TXT");
                Console.WriteLine("    4. Return to menu");
                Console.Write("\n  Select (1-4): ");
                var action = Console.ReadLine();

                switch (action)
                {
                    case "1":
                        Console.WriteLine("\n  FULL TRACE:");
                        Console.WriteLine("  " + new string('─', 66));
                        foreach (var t in lastCaseFile.Trace)
                        {
                            Console.ForegroundColor = t.Outcome.Contains("error") || t.Outcome.Contains("fail")
                                ? ConsoleColor.Red
                                : t.Outcome.Contains("warning") || t.Outcome.Contains("fallback")
                                    ? ConsoleColor.Yellow
                                    : ConsoleColor.Gray;
                            Console.WriteLine($"  {t.Timestamp:HH:mm:ss.fff} | {t.Tool,-30} | {t.Outcome,-20} | {t.Detail}");
                            Console.ResetColor();
                        }
                        break;
                    case "2":
                    case "3":
                        var fmt = action == "2" ? "json" : "txt";
                        var openAIVision = new OpenAIVisionAnalyzer();
                        var orch = new McpOrchestrator(openAIVision, graph);
                        var path = orch.ExportReport(lastCaseFile, fmt);
                        Console.WriteLine($"  ✅ Exported to: {path}");
                        break;
                }
            }

            await Task.CompletedTask;
        }

        // ═══════════════════════════════════════════════════════════════
        //  BATCH MCP INSPECTION (menu option 14)  🆕
        // ═══════════════════════════════════════════════════════════════

        static async Task RunBatchMcpInspection()
        {
            Console.WriteLine("\n🔄 BATCH MCP INSPECTION PIPELINE");
            Console.WriteLine(new string('═', 70));
            Console.WriteLine("Runs the MCP pipeline on N random test images per DeepPCB category.\n");

            Console.Write("How many images per category? (1-10, default=2): ");
            var countInput = Console.ReadLine()?.Trim();
            int perCategory = string.IsNullOrEmpty(countInput) ? 2 : Math.Clamp(int.TryParse(countInput, out var n) ? n : 2, 1, 10);

            // Find PCBData test images grouped by defect category
            var pcbDataDir = Path.Combine(Environment.CurrentDirectory, "datasets", "PCBData");

            if (!Directory.Exists(pcbDataDir))
            {
                Console.WriteLine("❌ PCBData dataset not found at datasets/PCBData/");
                Console.WriteLine("   Ensure the DeepPCB dataset is extracted to: datasets/PCBData/");
                return;
            }

            // DeepPCB: annotation files have defect codes 0-5
            var defectNames = new Dictionary<string, string>
            {
                ["0"] = "open", ["1"] = "short", ["2"] = "mousebite",
                ["3"] = "spur", ["4"] = "pin_hole", ["5"] = "spurious_copper"
            };

            // Collect all test images — supports both naming conventions:
            //   standard DeepPCB: {id}_test.jpg
            //   this distribution: {id}.jpg  (bare filename, _not = reference)
            var allTestImages = Directory.GetFiles(pcbDataDir, "*_test.jpg", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(pcbDataDir, "*_test.png", SearchOption.AllDirectories))
                .ToList();

            if (!allTestImages.Any())
            {
                // Bare filename convention: include all .jpg that don't end in _not
                allTestImages = Directory.GetFiles(pcbDataDir, "*.jpg", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(pcbDataDir, "*.png", SearchOption.AllDirectories))
                    .Where(f => {
                        var name = Path.GetFileNameWithoutExtension(f);
                        return !name.EndsWith("_not", StringComparison.OrdinalIgnoreCase)
                            && !name.EndsWith("_temp", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }

            if (!allTestImages.Any())
            {
                Console.WriteLine("❌ No test images found in the DeepPCB dataset folder.");
                return;
            }

            // Group by inferred category from annotation file (pick first defect code found)
            var byCategory = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in defectNames.Values) byCategory[name] = new List<string>();
            byCategory["unknown"] = new List<string>();

            foreach (var img in allTestImages)
            {
                var annot = Path.ChangeExtension(img, ".txt");
                // Also try without the _test suffix
                if (!File.Exists(annot))
                    annot = img.Replace("_test.jpg", ".txt").Replace("_test.png", ".txt");
                // DeepPCB convention: annotations live in {groupId}_not/ sibling folder
                if (!File.Exists(annot))
                {
                    var imgDir   = Path.GetDirectoryName(img) ?? "";
                    var imgParent = Path.GetDirectoryName(imgDir) ?? "";
                    var groupId  = Path.GetFileName(imgDir) ?? "";
                    var notDir   = Path.Combine(imgParent, groupId + "_not");
                    var imgBase  = Path.GetFileNameWithoutExtension(img)
                                       .Replace("_test", "").Replace("_temp", "");
                    var candidate = Path.Combine(notDir, imgBase + ".txt");
                    if (File.Exists(candidate)) annot = candidate;
                }

                if (File.Exists(annot))
                {
                    var firstLine = File.ReadLines(annot).FirstOrDefault()?.Trim();
                    if (firstLine != null)
                    {
                        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5 && defectNames.TryGetValue(parts[4], out var defectCat))
                        {
                            byCategory[defectCat].Add(img);
                            continue;
                        }
                    }
                }
                byCategory["unknown"].Add(img);
            }

            // Remove empty categories
            var activeCategories = byCategory.Where(kv => kv.Value.Any()).ToList();
            Console.WriteLine($"\n📋 Found images in {activeCategories.Count} categories. Selecting {perCategory} per category:\n");

            var selected = new List<(string category, string imagePath)>();
            var rng = new Random(42);
            foreach (var (cat, imgs) in activeCategories)
            {
                var picks = imgs.OrderBy(_ => rng.Next()).Take(perCategory);
                foreach (var p in picks) selected.Add((cat, p));
            }

            Console.WriteLine($"  Total images to process: {selected.Count}\n");
            Console.Write("  Proceed? (y/n): ");
            if (Console.ReadLine()?.ToLower() != "y") return;

            var openAIVision = new OpenAIVisionAnalyzer();
            var orchestrator = new McpOrchestrator(openAIVision, graph);

            // Run pipeline on each
            var results = new List<(string category, string image, CaseStatus status, bool humanReview, int violations)>();
            int done = 0;

            foreach (var (cat, imgPath) in selected)
            {
                done++;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"\n[{done}/{selected.Count}] {cat.ToUpper()} — {Path.GetFileName(imgPath)} ... ");
                Console.ResetColor();

                try
                {
                    var caseFile = await orchestrator.RunInspectionPipeline(imgPath);
                    lastCaseFile = caseFile;
                    results.Add((cat, Path.GetFileName(imgPath), caseFile.Status, caseFile.HumanReviewRequired, caseFile.PolicyViolations.Count));

                    var statusColor = caseFile.Status switch
                    {
                        CaseStatus.Completed => ConsoleColor.Green,
                        CaseStatus.ReviewRequired => ConsoleColor.Yellow,
                        _ => ConsoleColor.Red
                    };
                    Console.ForegroundColor = statusColor;
                    Console.Write($"{caseFile.Status}");
                    Console.ResetColor();
                    if (caseFile.HumanReviewRequired) Console.Write(" ⚠️ HumanReview");
                    if (caseFile.PolicyViolations.Any()) Console.Write($" 🛡{caseFile.PolicyViolations.Count}");

                    // Export each case
                    var p = orchestrator.ExportReport(caseFile, "json");
                    Console.WriteLine($" → {Path.GetFileName(p)}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"ERROR: {ex.Message}");
                    Console.ResetColor();
                    results.Add((cat, Path.GetFileName(imgPath), CaseStatus.Failed, false, 0));
                }
            }

            // Aggregated summary
            Console.WriteLine("\n\n" + new string('═', 70));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  BATCH RESULTS SUMMARY");
            Console.ResetColor();
            Console.WriteLine(new string('═', 70));

            var summaryRows = new List<string[]>();
            foreach (var cat in results.Select(r => r.category).Distinct().OrderBy(c => c))
            {
                var catResults = results.Where(r => r.category == cat).ToList();
                var completed = catResults.Count(r => r.status == CaseStatus.Completed);
                var review = catResults.Count(r => r.status == CaseStatus.ReviewRequired);
                var failed = catResults.Count(r => r.status == CaseStatus.Failed);
                var violations = catResults.Sum(r => r.violations);
                summaryRows.Add(new[] { cat, catResults.Count.ToString(), completed.ToString(), review.ToString(), failed.ToString(), violations.ToString() });
            }
            PrintTable(new[] { "Category", "Total", "✅ Done", "⚠️ Review", "❌ Failed", "🛡 Violations" }, summaryRows);

            var totalCompleted = results.Count(r => r.status == CaseStatus.Completed);
            var passRate = results.Count > 0 ? (double)totalCompleted / results.Count * 100 : 0;
            Console.WriteLine($"\n  Overall pass rate: {passRate:F0}% ({totalCompleted}/{results.Count})");
            Console.WriteLine($"  Human review flagged: {results.Count(r => r.humanReview)}");
            Console.WriteLine($"  All case reports saved to: outputs/");
        }

        // ═══════════════════════════════════════════════════════════════
        //  COMPARE EVALUATION RESULTS (menu option 17)  🆕
        // ═══════════════════════════════════════════════════════════════

        static async Task CompareEvaluationResults()
        {
            Console.WriteLine("\n📋 COMPARE EVALUATION RESULTS");
            Console.WriteLine(new string('═', 70));
            Console.WriteLine("Compare two evaluation report files side-by-side.\n");

            var outputDir = Path.Combine(Environment.CurrentDirectory, "outputs");
            var evalFiles = Directory.Exists(outputDir)
                ? Directory.GetFiles(outputDir, "EvalReport_*.json", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(outputDir, "EvalReport_*.txt", SearchOption.AllDirectories))
                    .Concat(Directory.GetFiles(Environment.CurrentDirectory, "EvalReport_*.json"))
                    .Concat(Directory.GetFiles(Environment.CurrentDirectory, "EvalReport_*.txt"))
                    .Distinct()
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToArray()
                : Array.Empty<string>();

            if (evalFiles.Length < 2)
            {
                Console.WriteLine("  ⚠️ Need at least 2 evaluation report files to compare.");
                Console.WriteLine($"  Found: {evalFiles.Length} report(s). Run option 15 to generate reports.");
                if (evalFiles.Length == 1)
                    Console.WriteLine($"  Existing: {Path.GetFileName(evalFiles[0])}");
                return;
            }

            Console.WriteLine("  Available evaluation reports:");
            for (int i = 0; i < Math.Min(evalFiles.Length, 10); i++)
                Console.WriteLine($"    {i + 1}. {Path.GetFileName(evalFiles[i])}  ({File.GetCreationTime(evalFiles[i]):yyyy-MM-dd HH:mm})");

            Console.Write("\n  Select FIRST report (number): ");
            if (!int.TryParse(Console.ReadLine(), out var idx1) || idx1 < 1 || idx1 > evalFiles.Length) { Console.WriteLine("Invalid."); return; }

            Console.Write("  Select SECOND report (number): ");
            if (!int.TryParse(Console.ReadLine(), out var idx2) || idx2 < 1 || idx2 > evalFiles.Length) { Console.WriteLine("Invalid."); return; }

            var file1 = evalFiles[idx1 - 1];
            var file2 = evalFiles[idx2 - 1];

            // Parse basic metrics from JSON reports
            static (int total, int pass, int review, int fail, double passRate) ParseReport(string path)
            {
                try
                {
                    var text = File.ReadAllText(path);
                    if (!path.EndsWith(".json")) return (0, 0, 0, 0, 0);
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    int total = root.TryGetProperty("TotalCases", out var t) ? t.GetInt32() : 0;
                    int pass  = root.TryGetProperty("PassCount",  out var p) ? p.GetInt32() : 0;
                    int rev   = root.TryGetProperty("ReviewCount",out var r) ? r.GetInt32() : 0;
                    int fail  = root.TryGetProperty("FailCount",  out var f) ? f.GetInt32() : 0;
                    double rate = total > 0 ? (double)pass / total * 100 : 0;
                    return (total, pass, rev, fail, rate);
                }
                catch { return (0, 0, 0, 0, 0); }
            }

            var (t1, p1, rv1, f1, rate1) = ParseReport(file1);
            var (t2, p2, rv2, f2, rate2) = ParseReport(file2);

            Console.WriteLine("\n" + new string('═', 70));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  SIDE-BY-SIDE COMPARISON");
            Console.ResetColor();
            Console.WriteLine(new string('─', 70));

            var rows = new List<string[]>
            {
                new[] { "File", Path.GetFileName(file1), Path.GetFileName(file2) },
                new[] { "Created", File.GetCreationTime(file1).ToString("yyyy-MM-dd HH:mm"), File.GetCreationTime(file2).ToString("yyyy-MM-dd HH:mm") },
                new[] { "Total Cases", t1.ToString(), t2.ToString() },
                new[] { "✅ Pass", p1.ToString(), p2.ToString() },
                new[] { "⚠️ Review", rv1.ToString(), rv2.ToString() },
                new[] { "❌ Fail", f1.ToString(), f2.ToString() },
                new[] { "Pass Rate", $"{rate1:F1}%", $"{rate2:F1}%" },
            };
            PrintTable(new[] { "Metric", "Report A", "Report B" }, rows);

            // Delta
            if (t1 > 0 && t2 > 0)
            {
                var delta = rate2 - rate1;
                Console.Write($"\n  Pass rate delta: ");
                Console.ForegroundColor = delta >= 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"{(delta >= 0 ? "+" : "")}{delta:F1}% ({Path.GetFileName(file1)} → {Path.GetFileName(file2)})");
                Console.ResetColor();
            }

            await Task.CompletedTask;
        }

        static void PrintTable(string[] headers, List<string[]> rows)
        {
            // Calculate column widths
            var widths = new int[headers.Length];
            for (int i = 0; i < headers.Length; i++)
            {
                widths[i] = Math.Max(headers[i].Length,
                    rows.Any() ? rows.Max(r => r[i].Length) : 0) + 2;
            }

            // Print header
            Console.Write("┌");
            for (int i = 0; i < headers.Length; i++)
            {
                Console.Write(new string('─', widths[i]));
                Console.Write(i < headers.Length - 1 ? "┬" : "┐");
            }
            Console.WriteLine();

            Console.Write("│");
            for (int i = 0; i < headers.Length; i++)
            {
                Console.Write(" " + headers[i].PadRight(widths[i] - 1) + "│");
            }
            Console.WriteLine();

            Console.Write("├");
            for (int i = 0; i < headers.Length; i++)
            {
                Console.Write(new string('─', widths[i]));
                Console.Write(i < headers.Length - 1 ? "┼" : "┤");
            }
            Console.WriteLine();

            // Print rows
            foreach (var row in rows)
            {
                Console.Write("│");
                for (int i = 0; i < row.Length; i++)
                {
                    Console.Write(" " + row[i].PadRight(widths[i] - 1) + "│");
                }
                Console.WriteLine();
            }

            Console.Write("└");
            for (int i = 0; i < headers.Length; i++)
            {
                Console.Write(new string('─', widths[i]));
                Console.Write(i < headers.Length - 1 ? "┴" : "┘");
            }
            Console.WriteLine();
        }

        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
╔════════════════════════════════════════════════════════════════════╗
║                                                                    ║
║     MANUFACTURING KNOWLEDGE GRAPH                                  ║
║     Cross-Modal Intelligence for Quality Control                   ║
║                                                                    ║
║     🏭 Connecting Visual Data + Process Knowledge + Standards     ║
║                                                                    ║
╚════════════════════════════════════════════════════════════════════╝
            ");
            Console.ResetColor();
        }

        static string GetDatasetPath()
        {
            Console.Write("Enter path to dataset (DeepPCB): ");
            var path = Console.ReadLine()?.Trim('"');
            return path ?? "";
        }


        static async Task ShowCompleteDashboard()
        {
            // Generate all analytics
            var defectFreq = graph.GetDefectFrequency();
            var severityDist = graph.GetSeverityDistribution();
            var productDefects = graph.GetProductDefectCounts();
            var equipmentUsage = graph.GetEquipmentUsage();
            var (products, severities, heatmapData) = graph.GetQualityHeatmap();
            var similarities = graph.FindSimilarDefectsAcrossProducts();
            var insights = graph.GenerateInsights();

            // Key metrics
            var metrics = new Dictionary<string, string>
            {
                ["Images Analyzed"] = graph.GetNodesByType("image").Count.ToString(),
                ["Total Defects"] = graph.GetNodesByType("defect").Count.ToString(),
                ["Product Categories"] = products.Length.ToString(),
                ["Equipment Types"] = equipmentUsage.Count.ToString(),
                ["Cross-Product Patterns"] = similarities.Count.ToString(),
                ["Avg Defects/Product"] = $"{(double)graph.GetNodesByType("defect").Count / products.Length:F1}"
            };

            // Draw main dashboard
            ChartGenerator.DrawDashboard(
                "MANUFACTURING KNOWLEDGE GRAPH - ANALYTICS DASHBOARD",
                metrics,
                insights
            );

            Console.WriteLine("Press any key to see detailed visualizations...");
            Console.ReadKey();

            // 1. Defect Distribution
            ChartGenerator.DrawBarChart("📊 DEFECT TYPE DISTRIBUTION", defectFreq);

            // 2. Severity Breakdown
            ChartGenerator.DrawPieChart("🔴 SEVERITY BREAKDOWN", severityDist);

            // 3. Product Defect Counts
            ChartGenerator.DrawBarChart("📦 DEFECTS BY PRODUCT", productDefects);

            // 4. Equipment Usage
            ChartGenerator.DrawBarChart("🔧 EQUIPMENT USAGE ANALYSIS", equipmentUsage);

            // 5. Quality Heatmap
            if (products.Length > 0)
            {
                ChartGenerator.DrawHeatmap("🔥 PRODUCT QUALITY HEATMAP", products, severities, heatmapData);
            }

            // 6. Summary footer with real data
            Console.WriteLine("\n" + new string('═', 70));
            Console.WriteLine($"📈 DASHBOARD SUMMARY (data-driven from {graph.GetNodesByType("image").Count} images across {products.Length} products)");
            Console.WriteLine($"   • {graph.GetNodesByType("defect").Count} total defects detected, {similarities.Count} cross-product patterns found");
            Console.WriteLine($"   • {equipmentUsage.Count} equipment types tracked");
            Console.WriteLine($"   • Use option [6] Visual Diagram for cross-product knowledge transfer network");
            Console.WriteLine(new string('═', 70));

            // At the end of ShowCompleteDashboard(), before await Task.CompletedTask:

            Console.Write("\n💾 Export this dashboard to file? (y/n): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = $"Dashboard_Export_{timestamp}.txt";

                // Redirect console output to file
                using (var writer = new StreamWriter(filename))
                {
                    var oldOut = Console.Out;
                    Console.SetOut(writer);

                    // Regenerate dashboard for file
                    ChartGenerator.DrawBarChart("DEFECT TYPE DISTRIBUTION", defectFreq);
                    ChartGenerator.DrawPieChart("SEVERITY BREAKDOWN", severityDist);
                    ChartGenerator.DrawBarChart("DEFECTS BY PRODUCT", productDefects);
                    ChartGenerator.DrawBarChart("EQUIPMENT USAGE ANALYSIS", equipmentUsage);

                    Console.SetOut(oldOut);
                }

                Console.WriteLine($"✅ Dashboard exported to: {filename}");
            }
            await Task.CompletedTask;
        }
        static async Task SaveGraphCache()
        {
            Console.WriteLine("💾 SAVE GRAPH TO CACHE");
            Console.WriteLine(new string('─', 70));

            graph.SaveToFile("knowledge_graph.json");
            Console.WriteLine("✅ Graph saved successfully!");

            await Task.CompletedTask;
        }

        static async Task RebuildGraph()
        {
            Console.WriteLine("🔄 REBUILD GRAPH FROM DATASET");
            Console.WriteLine(new string('─', 70));
            Console.WriteLine("⚠️  This will take 10-15 minutes and overwrite cached data.");
            Console.Write("Continue? (y/n): ");

            if (Console.ReadLine()?.ToLower() != "y")
            {
                Console.WriteLine("Cancelled.");
                return;
            }

            string azureEndpoint = AppConfig.VisionEndpoint;
            string azureKey = AppConfig.VisionKey;
            string datasetPath = GetDatasetPath();

            await BuildNewGraph(azureEndpoint, azureKey, datasetPath, "knowledge_graph.json");

            Console.WriteLine("\n✅ Graph rebuilt successfully!");

            await Task.CompletedTask;
        }

        static async Task DeleteCache()
        {
            Console.WriteLine("🗑️  DELETE CACHE FILE");
            Console.WriteLine(new string('─', 70));

            string cacheFile = "knowledge_graph.json";

            if (File.Exists(cacheFile))
            {
                Console.Write("⚠️  Are you sure? This cannot be undone. (y/n): ");
                if (Console.ReadLine()?.ToLower() == "y")
                {
                    File.Delete(cacheFile);
                    Console.WriteLine("✅ Cache deleted. Run option 12 to rebuild.");
                }
                else
                {
                    Console.WriteLine("Cancelled.");
                }
            }
            else
            {
                Console.WriteLine("❌ No cache file found.");
            }

            await Task.CompletedTask;
        }
    }
}