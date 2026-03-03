using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  McpOrchestrator — deterministic multi-step inspection pipeline
    //
    //  Exposed MCP tools:
    //    1. analyze_image_with_vision(imagePath)  — GPT-4.1 vision
    //    2. normalize_defect_with_ai(visionResult)— GPT-4.1-nano classification
    //    3. query_knowledge_graph(caseFile)        — graph + annotation enrichment
    //    4. root_cause_with_enriched_context       — o4-mini reasoning
    //    5. compliance_with_rag                    — RAG + GPT-4.1-mini
    //    6. export_report(caseFile, format)
    //
    //  Key design decisions (no hardcoding):
    //    • Step 2 — Equipment & inspection methods are AI-inferred by the
    //      classification model, NOT looked up from a static table.
    //    • Step 4 — Root-cause prompt includes annotation bounding boxes,
    //      co-occurring defect types, and defect density.
    //    • Step 5 — IPC sections are RETRIEVED from IpcComplianceReference
    //      before the model runs (RAG pattern).  The model may only cite
    //      sections present in the retrieved context.
    //    • Per-step model selection via AppConfig (VisionModel,
    //      ClassificationModel, ReasoningModel, ComplianceModel).
    // ───────────────────────────────────────────────────────────────────

    public class McpOrchestrator
    {
        private readonly KnowledgeGraph knowledgeGraph;
        private readonly BedrockNovaClient _nova;

        // DeepPCB defect-type map (annotation int -> name)
        private static readonly Dictionary<int, string> DeepPCBDefectNames = new()
        {
            [0] = "open",
            [1] = "short",
            [2] = "mousebite",
            [3] = "spur",
            [4] = "pin_hole",
            [5] = "spurious_copper"
        };

        public McpOrchestrator(KnowledgeGraph knowledgeGraph)
        {
            this.knowledgeGraph = knowledgeGraph;
            _nova = new BedrockNovaClient();
        }

        // ═══════════════════════════════════════════════════════════════
        //  FULL PIPELINE — runs all six MCP tools in sequence
        // ═══════════════════════════════════════════════════════════════

        public async Task<CaseFile> RunInspectionPipeline(string imagePath, string? productHint = null)
        {
            var caseFile = new CaseFile
            {
                ImagePath = imagePath,
                ProductType = productHint ?? InferProduct(imagePath),
                Status = CaseStatus.InProgress
            };

            caseFile.AddTrace("pipeline", "started",
                $"Image: {Path.GetFileName(imagePath)}, Product: {caseFile.ProductType}");

            Console.WriteLine($"\n{'═',0} MCP INSPECTION PIPELINE — Case {caseFile.CaseId}");
            Console.WriteLine(new string('═', 70));

            // -- Step 1: Analyze image with vision --
            Console.Write($"  [1/6] analyze_image_with_vision (Nova {AppConfig.NovaVisionModel}) ... ");
            await AnalyzeImageWithVision(caseFile);
            Console.WriteLine(caseFile.VisionAnalysis != null ? "OK" : "FAIL");

            // -- Step 2: Normalize defect (AI-driven) --
            Console.Write($"  [2/6] normalize_defect_with_ai (Nova {AppConfig.NovaReasoningModel}) ... ");
            await NormalizeDefectWithAI(caseFile);
            Console.WriteLine(caseFile.NormalizedDefect != null ? "OK" : "FAIL");

            // ── Step 3: Query knowledge graph + annotation enrichment ──
            Console.Write("  [3/6] query_knowledge_graph ... ");
            QueryKnowledgeGraphEnriched(caseFile);
            Console.WriteLine(caseFile.GraphContext != null ? "OK" : "SKIP");

            // -- Step 4: Root-cause with enriched context --
            Console.Write($"  [4/6] root_cause_enriched (Nova {AppConfig.NovaReasoningModel}) ... ");
            await RootCauseWithEnrichedContext(caseFile);
            Console.WriteLine(caseFile.RootCause != null ? "OK" : "FALLBACK");

            // -- Step 5: IPC compliance with RAG --
            Console.Write($"  [5/6] compliance_with_rag (Nova {AppConfig.NovaComplianceModel}) ... ");
            await ComplianceWithRAG(caseFile);
            Console.WriteLine(caseFile.Compliance != null ? "OK" : "FALLBACK");

            // ── Policy checks on accumulated outputs ──
            Console.Write("  [P]   policy_checks ... ");
            RunPolicyChecksOnCase(caseFile);
            Console.WriteLine("DONE");

            // ── Final review gate ──
            Console.Write("  [G]   final_review_gate ... ");
            Guardrails.FinalReviewGate(caseFile);
            Console.WriteLine(caseFile.HumanReviewRequired ? "REVIEW REQUIRED" : "PASSED");

            // ── Step 7: Agentic action loop ──
            Console.WriteLine($"  [7]   agentic_action_loop (Nova {AppConfig.NovaReasoningModel}) ...");
            await RunAgenticActionLoop(caseFile);

            Console.WriteLine(new string('═', 70));
            return caseFile;
        }

        // ═══════════════════════════════════════════════════════════════
        //  MCP TOOL 1 — analyze_image_with_vision
        //  Model: AppConfig.VisionModel (GPT-4.1)
        // ═══════════════════════════════════════════════════════════════

        public async Task AnalyzeImageWithVision(CaseFile caseFile)
        {
            const string tool = "analyze_image_with_vision";
            try
            {
                if (!File.Exists(caseFile.ImagePath))
                {
                    caseFile.AddTrace(tool, "error", $"File not found: {caseFile.ImagePath}");
                    return;
                }

                var result = await _nova.AnalyzeImageAsync(caseFile.ImagePath);
                caseFile.VisionAnalysis = result;

                caseFile.AddTrace(tool, "success",
                    $"[Nova {AppConfig.NovaVisionModel}] Caption: \"{result.Caption}\" (conf: {result.Confidence:P1}), " +
                    $"Defect: {result.DefectType} ({result.DefectSeverity}), " +
                    $"Tags: {result.Tags.Count}, Objects: {result.Objects.Count}");

                Guardrails.CheckVisionConfidence(result.Confidence, caseFile);
            }
            catch (Exception ex)
            {
                caseFile.AddTrace(tool, "error", ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  MCP TOOL 2 — normalize_defect_with_ai
        //  Model: AppConfig.ClassificationModel (GPT-4.1-nano)
        //
        //  Instead of a hardcoded lookup table for equipment and
        //  inspection methods, we ask the classification model:
        //    "Given this defect type on this product, what equipment
        //     likely produced it and how should it be inspected?"
        // ═══════════════════════════════════════════════════════════════

        public async Task NormalizeDefectWithAI(CaseFile caseFile)
        {
            const string tool = "normalize_defect_with_ai";

            if (caseFile.VisionAnalysis == null)
            {
                caseFile.AddTrace(tool, "skipped", "No vision analysis available");
                return;
            }

            // ── Basic normalization (deterministic) ──
            var defectType = caseFile.VisionAnalysis.DefectType;
            var severity = caseFile.VisionAnalysis.DefectSeverity;
            var folderName = Path.GetFileName(Path.GetDirectoryName(caseFile.ImagePath) ?? "");

            if (string.IsNullOrEmpty(defectType) ||
                defectType.Equals("good", StringComparison.OrdinalIgnoreCase) ||
                defectType.Equals("other", StringComparison.OrdinalIgnoreCase))
            {
                defectType = folderName;
            }

            if (string.IsNullOrEmpty(severity))
                severity = DetermineSeverity(folderName);

            var taxonomyId = $"DEF-{defectType.ToUpper().Replace("_", "-")}-{caseFile.ProductType.ToUpper()[..Math.Min(3, caseFile.ProductType.Length)]}";

            var description = !string.IsNullOrEmpty(caseFile.VisionAnalysis.DefectDescription)
                ? caseFile.VisionAnalysis.DefectDescription
                : caseFile.VisionAnalysis.Caption;

            caseFile.NormalizedDefect = new NormalizedDefect
            {
                DefectType = defectType,
                Severity = severity,
                Product = caseFile.ProductType,
                Description = description,
                TaxonomyId = taxonomyId
            };

            // ── AI-driven equipment & inspection inference ──
            var aiSystemPrompt = @"You are a PCB manufacturing process expert. Given a defect type and product, infer:
1) Which manufacturing equipment most likely caused or is related to this defect
2) Which inspection methods should be used to detect this defect type

Respond with ONLY valid JSON:
{
  ""equipment"": [""string""],
  ""inspectionMethods"": [""string""],
  ""reasoning"": ""one sentence explaining the link""
}

Return raw JSON only, no markdown fences.";

            var aiUserPrompt = $@"Product: {caseFile.ProductType}
Defect type: {defectType}
Severity: {severity}
Description: {description}";

            var rawJson = await CallModelAsync(
                aiSystemPrompt, aiUserPrompt,
                caseFile, tool);

            if (!string.IsNullOrEmpty(rawJson))
            {
                try
                {
                    var cleaned = StripMarkdownFences(rawJson);
                    using var doc = JsonDocument.Parse(cleaned);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("inspectionMethods", out var methods) &&
                        methods.ValueKind == JsonValueKind.Array)
                    {
                        caseFile.NormalizedDefect.InspectionMethods = methods.EnumerateArray()
                            .Select(m => m.GetString() ?? "")
                            .Where(m => !string.IsNullOrEmpty(m))
                            .ToList();
                    }

                    if (root.TryGetProperty("reasoning", out var reason))
                        caseFile.NormalizedDefect.Reasoning = reason.GetString() ?? "";

                    // Store AI-inferred equipment IDs for later use in graph context
                    if (root.TryGetProperty("equipment", out var equip) &&
                        equip.ValueKind == JsonValueKind.Array)
                    {
                        caseFile.NormalizedDefect.InspectionMethods ??= new();
                        // We'll pass equipment through to step 3 via NormalizedDefect metadata
                        var equipList = equip.EnumerateArray()
                            .Select(e => e.GetString() ?? "")
                            .Where(e => !string.IsNullOrEmpty(e))
                            .ToList();

                        // Store temporarily in the trace for step 3 to pick up
                        caseFile.AddTrace(tool, "ai_equipment",
                            string.Join("|", equipList));
                    }
                }
                catch (JsonException ex)
                {
                    caseFile.AddTrace(tool, "ai_parse_error", ex.Message);
                }
            }

            caseFile.AddTrace(tool, "success",
                $"Type: {defectType}, Severity: {severity}, Taxonomy: {taxonomyId}, " +
                $"InspectionMethods: [{string.Join(", ", caseFile.NormalizedDefect.InspectionMethods)}]");
        }

        // ═══════════════════════════════════════════════════════════════
        //  MCP TOOL 3 — query_knowledge_graph (enriched)
        //
        //  In addition to graph queries, this step now:
        //    a) Reads annotation bounding boxes for the image
        //    b) Computes co-occurring defect types
        //    c) Computes defect density
        //    d) Uses AI-inferred equipment from step 2
        // ═══════════════════════════════════════════════════════════════

        public void QueryKnowledgeGraphEnriched(CaseFile caseFile)
        {
            const string tool = "query_knowledge_graph";

            if (caseFile.NormalizedDefect == null)
            {
                caseFile.AddTrace(tool, "skipped", "No normalized defect available");
                return;
            }

            var defectType = caseFile.NormalizedDefect.DefectType;
            var product = caseFile.ProductType;

            // ── Graph queries (same as before) ──
            var allDefects = knowledgeGraph.GetNodesByType("defect");
            var relatedDefects = allDefects
                .Where(d => d.Properties.ContainsKey("name") &&
                            d.Properties["name"].ToString()!
                                .Contains(defectType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var standards = knowledgeGraph.GetNodesByType("standard");

            var historicalIncidents = relatedDefects
                .Where(d => d.Properties.ContainsKey("product") &&
                            d.Properties["product"].ToString() != product)
                .Select(d => new HistoricalIncident
                {
                    DefectNodeId = d.Id,
                    Product = d.Properties["product"]?.ToString() ?? "",
                    DefectType = d.Properties["name"]?.ToString() ?? "",
                    Severity = d.Properties.ContainsKey("severity") ? d.Properties["severity"]?.ToString() ?? "" : ""
                })
                .ToList();

            // ── AI-inferred equipment from step 2 (not hardcoded) ──
            var aiEquipmentTrace = caseFile.Trace
                .FirstOrDefault(t => t.Tool == "normalize_defect_with_ai" && t.Outcome == "ai_equipment");
            var aiEquipment = aiEquipmentTrace?.Detail?.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList()
                ?? new List<string>();

            // ── Annotation enrichment ──
            var annotationCtx = GetAnnotationContext(caseFile.ImagePath);

            // ── IPC section IDs (from RAG reference, not hardcoded) ──
            var ipcSections = IpcComplianceReference.GetSectionsForDefect(defectType);
            var isoSnippets = ipcSections
                .Select(s => $"{s.Standard} § {s.Section} — {s.Title}")
                .ToList();

            caseFile.GraphContext = new KnowledgeGraphContext
            {
                RelatedDefectIds = relatedDefects.Select(d => d.Id).ToList(),
                EquipmentIds = aiEquipment,
                StandardIds = ipcSections.Select(s => s.Id).ToList(),
                HistoricalIncidents = historicalIncidents,
                IsoSnippets = isoSnippets,
                CoOccurringDefects = annotationCtx.CoOccurring,
                DefectDensity = annotationCtx.Density
            };

            caseFile.AddTrace(tool, "success",
                $"Related defects: {relatedDefects.Count}, AI-equipment: {aiEquipment.Count}, " +
                $"IPC sections: {ipcSections.Count}, " +
                $"Co-occurring: [{string.Join(", ", annotationCtx.CoOccurring)}], " +
                $"Density: {annotationCtx.Density:F2}, BBoxes: {annotationCtx.BoundingBoxes.Count}");
        }

        // ═══════════════════════════════════════════════════════════════
        //  MCP TOOL 4 — root_cause_with_enriched_context
        //  Model: AppConfig.ReasoningModel (o4-mini)
        //
        //  Prompt now includes:
        //    • annotation bounding boxes (location/size of defects)
        //    • co-occurring defect types (pattern: does open + short
        //      always appear together → etching issue)
        //    • defect density (high density → systemic process issue)
        //    • AI-inferred equipment links from step 2
        // ═══════════════════════════════════════════════════════════════

        public async Task RootCauseWithEnrichedContext(CaseFile caseFile)
        {
            const string tool = "root_cause_enriched";

            if (caseFile.NormalizedDefect == null)
            {
                caseFile.AddTrace(tool, "skipped", "No normalized defect available");
                return;
            }

            var contextSummary = BuildEnrichedContextSummary(caseFile);

            var systemPrompt = @"You are a PCB manufacturing quality engineer performing root-cause analysis. You are given a defect report with enriched context including annotation bounding boxes, co-occurring defect patterns, defect density metrics, AI-inferred equipment links, and knowledge-graph data.

You MUST respond with ONLY valid JSON matching this schema:
{
  ""probableCause"": ""string"",
  ""reasoning"": ""string — explain your chain of thought: what evidence led to this conclusion"",
  ""confidence"": 0.85,
  ""contributingFactors"": [""string""],
  ""actions"": [
    { ""action"": ""string"", ""priority"": ""immediate|short-term|long-term"", ""owner"": ""string"", ""contextRef"": ""string — graph node ID or IPC section that justifies this action"" }
  ],
  ""referencedContextIds"": [""string""]
}

Rules:
- Use the annotation data (bounding boxes, co-occurrence, density) to inform your analysis.
  High density → likely systemic process issue. Co-occurring open+short → etching problem.
  Defect location (bounding box position) can indicate specific equipment stage.
- referencedContextIds MUST contain at least one ID from the retrieved context.
- contextRef in each action should cite the evidence that justifies the action.
- Do not fabricate. Base analysis on provided context only.
- Return raw JSON only, no markdown fences.";

            var userPrompt = $@"Defect report:
- Product: {caseFile.NormalizedDefect.Product}
- Defect type: {caseFile.NormalizedDefect.DefectType}
- Severity: {caseFile.NormalizedDefect.Severity}
- Description: {caseFile.NormalizedDefect.Description}
- Taxonomy ID: {caseFile.NormalizedDefect.TaxonomyId}
- AI-inferred inspection methods: [{string.Join(", ", caseFile.NormalizedDefect.InspectionMethods)}]

Enriched context:
{contextSummary}

Provide root-cause analysis as JSON.";

            var rawJson = await CallModelAsync(
                systemPrompt, userPrompt,
                caseFile, tool, isReasoning: true);

            if (string.IsNullOrEmpty(rawJson))
            {
                caseFile.RootCause = BuildFallbackRootCause(caseFile);
                caseFile.AddTrace(tool, "fallback", "API call failed, using rule-based fallback");
                caseFile.FlagForHumanReview("root_cause_enriched: model unavailable, used fallback");
                return;
            }

            var (result, usedFallback) = Guardrails.ValidateOrRepair<RootCauseResult>(rawJson, caseFile, tool);

            if (usedFallback)
            {
                caseFile.RootCause = BuildFallbackRootCause(caseFile);
                return;
            }

            caseFile.RootCause = result;
            Guardrails.CheckTraceability(result.ReferencedContextIds, caseFile, tool);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MCP TOOL 5 — compliance_with_rag
        //  Model: AppConfig.ComplianceModel (GPT-4.1-mini)
        //
        //  RAG pattern:
        //    1. RETRIEVE relevant IPC sections from IpcComplianceReference
        //    2. Inject retrieved sections into the prompt
        //    3. Model may ONLY cite sections present in retrieved context
        //    4. Guardrails.CheckTraceability verifies this
        // ═══════════════════════════════════════════════════════════════

        public async Task ComplianceWithRAG(CaseFile caseFile)
        {
            const string tool = "compliance_with_rag";

            if (caseFile.NormalizedDefect == null)
            {
                caseFile.AddTrace(tool, "skipped", "No normalized defect available");
                return;
            }

            // ── RAG Step 1: Retrieve relevant IPC sections ──
            var defectType = caseFile.NormalizedDefect.DefectType;
            var allDefectTypes = new List<string> { defectType };
            if (caseFile.GraphContext?.CoOccurringDefects.Any() == true)
                allDefectTypes.AddRange(caseFile.GraphContext.CoOccurringDefects);

            var retrievedSections = IpcComplianceReference.GetSectionsForDefects(allDefectTypes);
            var retrievedContext = IpcComplianceReference.BuildComplianceContext(defectType);

            caseFile.AddTrace(tool, "rag_retrieval",
                $"Retrieved {retrievedSections.Count} IPC sections for defect type '{defectType}'");

            // ── RAG Step 2: Build prompt with retrieved sections ──
            var systemPrompt = $@"You are an IPC compliance advisor for PCB manufacturing. Given a defect case and RETRIEVED IPC reference sections, provide compliance guidance.

CRITICAL: You may ONLY reference sections that appear in the RETRIEVED IPC REFERENCE below.
Do NOT fabricate section numbers, criteria, or standards not listed below.

{retrievedContext}

You MUST respond with ONLY valid JSON matching this schema:
{{
  ""applicableStandard"": ""string — must be from retrieved sections"",
  ""section"": ""string — must be from retrieved sections"",
  ""classification"": ""Class 1|Class 2|Class 3"",
  ""disposition"": ""accept|rework|reject"",
  ""checklist"": [
    {{ ""requirement"": ""string"", ""addressed"": true/false, ""evidence"": ""string"", ""sectionRef"": ""string — IPC section ID from retrieved context"" }}
  ],
  ""retrievedSections"": [""string — list of IPC section IDs you referenced""],
  ""referencedContextIds"": [""string — same as retrievedSections + any graph node IDs""],
  ""fullyCovered"": true/false
}}

Rules:
- Classification determines acceptance criteria (Class 2 is typical for commercial PCBs).
- disposition: accept (within tolerance), rework (can be repaired), reject (fails criteria).
- Every checklist item MUST have a sectionRef from the retrieved sections.
- Return raw JSON only, no markdown fences.";

            var userPrompt = $@"Inspection case:
- Product: {caseFile.NormalizedDefect.Product}
- Defect: {caseFile.NormalizedDefect.DefectType} ({caseFile.NormalizedDefect.Severity} severity)
- Taxonomy: {caseFile.NormalizedDefect.TaxonomyId}
- Description: {caseFile.NormalizedDefect.Description}
- Root cause: {caseFile.RootCause?.ProbableCause ?? "pending"}
- Co-occurring defects: [{string.Join(", ", caseFile.GraphContext?.CoOccurringDefects ?? new())}]
- Defect density: {caseFile.GraphContext?.DefectDensity ?? 0:F2}

Provide IPC compliance guidance using ONLY the retrieved sections above.";

            var rawJson = await CallModelAsync(
                systemPrompt, userPrompt,
                caseFile, tool);

            if (string.IsNullOrEmpty(rawJson))
            {
                caseFile.Compliance = BuildFallbackCompliance(caseFile, retrievedSections);
                caseFile.AddTrace(tool, "fallback", "API call failed, using template fallback");
                caseFile.FlagForHumanReview("compliance_with_rag: model unavailable, used fallback");
                return;
            }

            var (result, usedFallback) = Guardrails.ValidateOrRepair<IsoComplianceResult>(rawJson, caseFile, tool);

            if (usedFallback)
            {
                caseFile.Compliance = BuildFallbackCompliance(caseFile, retrievedSections);
                return;
            }

            // Populate RAG metadata
            result.RetrievedSections = retrievedSections.Select(s => s.Id).ToList();
            caseFile.Compliance = result;

            Guardrails.CheckTraceability(result.ReferencedContextIds, caseFile, tool);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MCP TOOL 7 — agentic_action_loop
        //
        //  Gives Nova three callable tools and lets it decide autonomously
        //  which actions to take based on the completed inspection.
        //  Nova loops (perceive → reason → act → observe result → reason)
        //  until it reaches end_turn or the 5-iteration safety cap.
        // ═══════════════════════════════════════════════════════════════

        private async Task RunAgenticActionLoop(CaseFile caseFile)
        {
            const string tool = "agentic_action_loop";

            try
            {
                // Derive a demo batch ID from the case / image for context
                var batchId = $"BATCH-{DateTime.UtcNow:yyyyMMdd}-{caseFile.CaseId[..6].ToUpper()}";

                // Build a rich context summary for Nova
                var defectType  = caseFile.NormalizedDefect?.DefectType  ?? caseFile.VisionAnalysis?.DefectType ?? "unknown";
                var severity    = caseFile.NormalizedDefect?.Severity    ?? caseFile.VisionAnalysis?.DefectSeverity ?? "medium";
                var rootCause   = caseFile.RootCause?.ProbableCause      ?? "undetermined";
                var actionsText = caseFile.RootCause?.Actions != null
                    ? string.Join("; ", caseFile.RootCause.Actions.Select(a => $"[{a.Priority}] {a.Action}"))
                    : "none";
                var compliance  = caseFile.Compliance?.ApplicableStandard ?? "IPC-A-610";
                var humanReview = caseFile.HumanReviewRequired;
                var violations  = caseFile.PolicyViolations.Count;

                var systemPrompt =
                    "You are an autonomous manufacturing quality agent. You have just completed a full PCB inspection " +
                    "and now must take concrete corrective actions using the tools available to you. " +
                    "Do not just recommend — actually call the tools. " +
                    "After calling tools and receiving their results, summarize what you did and why. " +
                    "Available tools: quarantine_batch, update_knowledge_graph, file_work_order.";

                var userPrompt =
                    $"INSPECTION SUMMARY:\n" +
                    $"  Batch ID:        {batchId}\n" +
                    $"  Defect Type:     {defectType}\n" +
                    $"  Severity:        {severity}\n" +
                    $"  Root Cause:      {rootCause}\n" +
                    $"  Recommended Actions: {actionsText}\n" +
                    $"  Compliance Standard: {compliance}\n" +
                    $"  Human Review Required: {humanReview}\n" +
                    $"  Policy Violations: {violations}\n\n" +
                    $"Based on this inspection result, decide which tools to call and call them now. " +
                    $"Use quarantine_batch if the severity warrants it. " +
                    $"Always file_work_order for any actionable finding. " +
                    $"Always call update_knowledge_graph to record what was learned.";

                var tools = AgentTools.GetToolDefinitions();

                // Run the agentic loop — Nova calls tools until end_turn
                var finalSummary = await _nova.InvokeAgentLoopAsync(
                    systemPrompt,
                    userPrompt,
                    tools,
                    async (toolName, inputDoc) =>
                        await AgentTools.ExecuteAsync(toolName, inputDoc, knowledgeGraph, caseFile)
                );

                // Log Nova's final reasoning
                if (!string.IsNullOrWhiteSpace(finalSummary))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n  🤖 Agent Summary: {finalSummary[..Math.Min(200, finalSummary.Length)]}...");
                    Console.ResetColor();
                    caseFile.AddTrace(tool, "loop_complete", finalSummary[..Math.Min(300, finalSummary.Length)]);
                }

                Console.WriteLine($"  ✅ Agentic loop complete — {caseFile.AgentActions.Count} action(s) taken.");
            }
            catch (Exception ex)
            {
                caseFile.AddTrace(tool, "error", ex.Message);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⚠️  Agentic loop error: {ex.Message}");
                Console.ResetColor();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  MCP TOOL 6 — export_report
        // ═══════════════════════════════════════════════════════════════

        public string ExportReport(CaseFile caseFile, string format = "json")
        {
            const string tool = "export_report";

            var outputDir = Path.Combine(Environment.CurrentDirectory, "outputs", "cases");
            Directory.CreateDirectory(outputDir);

            var filename = $"Case_{caseFile.CaseId}_{DateTime.Now:yyyyMMdd_HHmmss}";

            string outputPath;

            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                outputPath = Path.Combine(outputDir, filename + ".json");
                File.WriteAllText(outputPath, caseFile.ToJson());
            }
            else
            {
                outputPath = Path.Combine(outputDir, filename + ".txt");
                File.WriteAllText(outputPath, FormatCaseAsText(caseFile));
            }

            caseFile.AddTrace(tool, "exported", $"Report saved to {outputPath}");
            return outputPath;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ANNOTATION CONTEXT — reads DeepPCB annotation files
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads the annotation file for a DeepPCB image and extracts:
        ///   - Bounding boxes with defect types
        ///   - Co-occurring defect types (other types in the same image)
        ///   - Defect density (count / image area approximation)
        /// </summary>
        private AnnotationContext GetAnnotationContext(string imagePath)
        {
            var ctx = new AnnotationContext();

            // DeepPCB annotation path: .../groupXXXXX/XXXXX/XXXXX.txt
            //   from image:            .../groupXXXXX/XXXXX/XXXXX_test.jpg
            var dir = Path.GetDirectoryName(imagePath);
            if (dir == null) return ctx;

            // Try to find annotation file
            var baseName = Path.GetFileNameWithoutExtension(imagePath)
                .Replace("_test", "").Replace("_temp", "");
            var annotationPath = Path.Combine(dir, baseName + ".txt");

            if (!File.Exists(annotationPath))
            {
                // Also try with _not_ suffix pattern used in some DeepPCB versions
                var altFiles = Directory.GetFiles(dir, "*.txt");
                annotationPath = altFiles.FirstOrDefault(f =>
                    !f.EndsWith("readme.txt", StringComparison.OrdinalIgnoreCase) &&
                    !f.EndsWith("license.txt", StringComparison.OrdinalIgnoreCase));

                if (annotationPath == null) return ctx;
            }

            try
            {
                var lines = File.ReadAllLines(annotationPath);
                var defectTypesInImage = new HashSet<string>();

                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 &&
                        int.TryParse(parts[0], out var x1) &&
                        int.TryParse(parts[1], out var y1) &&
                        int.TryParse(parts[2], out var x2) &&
                        int.TryParse(parts[3], out var y2) &&
                        int.TryParse(parts[4], out var typeId))
                    {
                        var typeName = DeepPCBDefectNames.GetValueOrDefault(typeId, $"type_{typeId}");
                        ctx.BoundingBoxes.Add(new BBox
                        {
                            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                            DefectType = typeName
                        });
                        defectTypesInImage.Add(typeName);
                    }
                }

                ctx.CoOccurring = defectTypesInImage.ToList();

                // Density: defects per 640×640 image (DeepPCB standard size)
                const double imageArea = 640.0 * 640.0;
                ctx.Density = ctx.BoundingBoxes.Count / imageArea * 10000; // per 100×100 block
            }
            catch { /* annotation parsing is best-effort */ }

            return ctx;
        }

        // ═══════════════════════════════════════════════════════════════
        //  INTERNAL HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void RunPolicyChecksOnCase(CaseFile caseFile)
        {
            var textsToScan = new List<string>();

            if (caseFile.VisionAnalysis != null)
                textsToScan.Add(caseFile.VisionAnalysis.Caption);

            if (caseFile.RootCause != null)
            {
                textsToScan.Add(caseFile.RootCause.ProbableCause);
                textsToScan.Add(caseFile.RootCause.Reasoning);
                textsToScan.AddRange(caseFile.RootCause.ContributingFactors);
                textsToScan.AddRange(caseFile.RootCause.Actions.Select(a => a.Action));
            }

            if (caseFile.Compliance != null)
            {
                textsToScan.AddRange(caseFile.Compliance.Checklist.Select(c => c.Requirement));
                textsToScan.AddRange(caseFile.Compliance.Checklist.Select(c => c.Evidence));
            }

            var combined = string.Join("\n", textsToScan.Where(t => !string.IsNullOrEmpty(t)));
            Guardrails.RunPolicyChecks(combined, caseFile);
        }

        /// <summary>
        /// Calls Amazon Nova via Bedrock for reasoning/compliance steps.
        /// </summary>
        private async Task<string?> CallModelAsync(
            string systemPrompt, string userPrompt,
            CaseFile caseFile, string tool,
            bool isReasoning = false)
        {
            return await _nova.InvokeTextAsync(systemPrompt, userPrompt, isReasoning, tool, caseFile);
        }

        private string BuildEnrichedContextSummary(CaseFile caseFile)
        {
            var sb = new StringBuilder();

            if (caseFile.GraphContext != null)
            {
                var ctx = caseFile.GraphContext;
                sb.AppendLine($"Related defect node IDs: {string.Join(", ", ctx.RelatedDefectIds.Take(10))}");
                sb.AppendLine($"AI-inferred equipment: {string.Join(", ", ctx.EquipmentIds)}");
                sb.AppendLine($"Applicable IPC sections: {string.Join(", ", ctx.StandardIds)}");

                // Annotation enrichment
                if (ctx.CoOccurringDefects.Any())
                    sb.AppendLine($"Co-occurring defects in this image: [{string.Join(", ", ctx.CoOccurringDefects)}]");

                sb.AppendLine($"Defect density: {ctx.DefectDensity:F2} defects per 100×100px block");

                if (ctx.HistoricalIncidents.Any())
                {
                    sb.AppendLine("Historical incidents (similar defects on other boards):");
                    foreach (var h in ctx.HistoricalIncidents.Take(5))
                        sb.AppendLine($"  - {h.DefectType} on {h.Product} ({h.Severity}) [ID: {h.DefectNodeId}]");
                }

                if (ctx.IsoSnippets.Any())
                {
                    sb.AppendLine("IPC references:");
                    foreach (var iso in ctx.IsoSnippets)
                        sb.AppendLine($"  - {iso}");
                }
            }
            else
            {
                sb.AppendLine("No knowledge-graph context retrieved.");
            }

            // Add bounding-box data from annotations
            var annotCtx = GetAnnotationContext(caseFile.ImagePath);
            if (annotCtx.BoundingBoxes.Any())
            {
                sb.AppendLine($"\nAnnotation bounding boxes ({annotCtx.BoundingBoxes.Count} defects):");
                foreach (var bb in annotCtx.BoundingBoxes)
                {
                    sb.AppendLine($"  - [{bb.X1},{bb.Y1}]-[{bb.X2},{bb.Y2}] → {bb.DefectType} " +
                                  $"(size: {bb.X2 - bb.X1}×{bb.Y2 - bb.Y1}px)");
                }
            }

            return sb.ToString();
        }

        private static RootCauseResult BuildFallbackRootCause(CaseFile caseFile)
        {
            var defect = caseFile.NormalizedDefect!;
            return new RootCauseResult
            {
                ProbableCause = $"[FALLBACK] Potential {defect.DefectType} due to process variation — requires human review",
                Reasoning = "Model unavailable; using rule-based fallback",
                Confidence = 0.3,
                ContributingFactors = new List<string>
                {
                    "Process parameter drift",
                    "Material quality variation",
                    "Equipment calibration"
                },
                Actions = new List<RecommendedAction>
                {
                    new() { Action = "Inspect equipment calibration", Priority = "immediate", Owner = "Maintenance", ContextRef = "" },
                    new() { Action = "Review process parameters", Priority = "short-term", Owner = "QE", ContextRef = "" },
                    new() { Action = "Update inspection criteria", Priority = "long-term", Owner = "Quality Manager", ContextRef = "" }
                },
                ReferencedContextIds = caseFile.GraphContext?.RelatedDefectIds.Take(2).ToList() ?? new()
            };
        }

        private static IsoComplianceResult BuildFallbackCompliance(CaseFile caseFile, List<IpcSection>? retrievedSections = null)
        {
            var sections = retrievedSections ?? new();
            var primarySection = sections.FirstOrDefault();

            return new IsoComplianceResult
            {
                ApplicableStandard = primarySection?.Standard ?? "IPC-A-600J",
                Section = primarySection?.Section ?? "2.2",
                Classification = "Class 2",
                Disposition = "rework",
                RetrievedSections = sections.Select(s => s.Id).ToList(),
                Checklist = new List<ComplianceCheckItem>
                {
                    new() { Requirement = "Defect documented per IPC criteria", Addressed = true,
                            Evidence = $"Case {caseFile.CaseId} created",
                            SectionRef = primarySection?.Id ?? "" },
                    new() { Requirement = "Root-cause analysis performed", Addressed = caseFile.RootCause != null,
                            Evidence = caseFile.RootCause?.ProbableCause ?? "Pending",
                            SectionRef = "" },
                    new() { Requirement = "Corrective action defined", Addressed = caseFile.RootCause?.Actions.Any() == true,
                            Evidence = "See actions list",
                            SectionRef = "" },
                    new() { Requirement = "Preventive measures identified", Addressed = false,
                            Evidence = "[FALLBACK] Requires human review",
                            SectionRef = "" },
                    new() { Requirement = "Verification plan established", Addressed = false,
                            Evidence = "[FALLBACK] Requires human review",
                            SectionRef = "" }
                },
                ReferencedContextIds = sections.Select(s => s.Id).ToList(),
                FullyCovered = false
            };
        }

        private string InferProduct(string imagePath)
        {
            var parts = imagePath.Replace('\\', '/').Split('/');

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals("DeepPCB", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("deeppcb", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("PCBData", StringComparison.OrdinalIgnoreCase))
                    return "pcb";
            }

            // Fallback: MVTec-style path
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals("test", StringComparison.OrdinalIgnoreCase) && i > 0)
                    return parts[i - 1];
            }
            return "unknown";
        }

        private static string DetermineSeverity(string defectCategory)
        {
            defectCategory = defectCategory.ToLower();
            if (defectCategory.Contains("large") || defectCategory.Contains("severe"))
                return "high";
            else if (defectCategory.Contains("small") || defectCategory.Contains("minor"))
                return "low";
            return "medium";
        }

        private static string StripMarkdownFences(string text)
        {
            var cleaned = text.Trim();
            if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                if (firstNewline > 0)
                    cleaned = cleaned[(firstNewline + 1)..];
                if (cleaned.EndsWith("```"))
                    cleaned = cleaned[..^3];
                cleaned = cleaned.Trim();
            }
            return cleaned;
        }

        private static string FormatCaseAsText(CaseFile c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("  MCP INSPECTION REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine($"  Case ID:      {c.CaseId}");
            sb.AppendLine($"  Created:      {c.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"  Image:        {Path.GetFileName(c.ImagePath)}");
            sb.AppendLine($"  Product:      {c.ProductType}");
            sb.AppendLine($"  Status:       {c.Status}");
            sb.AppendLine($"  Human Review: {(c.HumanReviewRequired ? "YES" : "No")}");
            sb.AppendLine();

            if (c.VisionAnalysis != null)
            {
                sb.AppendLine("── VISION ANALYSIS ──");
                sb.AppendLine($"  Caption:    {c.VisionAnalysis.Caption}");
                sb.AppendLine($"  Confidence: {c.VisionAnalysis.Confidence:P1}");
                sb.AppendLine($"  Tags:       {string.Join(", ", c.VisionAnalysis.Tags)}");
                sb.AppendLine();
            }

            if (c.NormalizedDefect != null)
            {
                sb.AppendLine("── NORMALIZED DEFECT ──");
                sb.AppendLine($"  Type:       {c.NormalizedDefect.DefectType}");
                sb.AppendLine($"  Severity:   {c.NormalizedDefect.Severity}");
                sb.AppendLine($"  Taxonomy:   {c.NormalizedDefect.TaxonomyId}");
                sb.AppendLine($"  Inspection: [{string.Join(", ", c.NormalizedDefect.InspectionMethods)}]");
                if (!string.IsNullOrEmpty(c.NormalizedDefect.Reasoning))
                    sb.AppendLine($"  Reasoning:  {c.NormalizedDefect.Reasoning}");
                sb.AppendLine();
            }

            if (c.GraphContext != null)
            {
                sb.AppendLine("── KNOWLEDGE GRAPH CONTEXT ──");
                sb.AppendLine($"  Related defects:      {c.GraphContext.RelatedDefectIds.Count}");
                sb.AppendLine($"  AI-equipment:         {string.Join(", ", c.GraphContext.EquipmentIds)}");
                sb.AppendLine($"  Co-occurring:         [{string.Join(", ", c.GraphContext.CoOccurringDefects)}]");
                sb.AppendLine($"  Defect density:       {c.GraphContext.DefectDensity:F2}");
                sb.AppendLine($"  Historical incidents: {c.GraphContext.HistoricalIncidents.Count}");
                sb.AppendLine($"  IPC references:       {string.Join("; ", c.GraphContext.IsoSnippets)}");
                sb.AppendLine();
            }

            if (c.RootCause != null)
            {
                sb.AppendLine("── ROOT-CAUSE ANALYSIS ──");
                sb.AppendLine($"  Probable cause: {c.RootCause.ProbableCause}");
                sb.AppendLine($"  Confidence:     {c.RootCause.Confidence:P0}");
                if (!string.IsNullOrEmpty(c.RootCause.Reasoning))
                    sb.AppendLine($"  Reasoning:      {c.RootCause.Reasoning}");
                sb.AppendLine($"  Contributing factors:");
                foreach (var f in c.RootCause.ContributingFactors)
                    sb.AppendLine($"    - {f}");
                sb.AppendLine($"  Actions:");
                foreach (var a in c.RootCause.Actions)
                    sb.AppendLine($"    [{a.Priority}] {a.Action} (Owner: {a.Owner})" +
                                  (string.IsNullOrEmpty(a.ContextRef) ? "" : $" ← {a.ContextRef}"));
                sb.AppendLine($"  Referenced IDs: {string.Join(", ", c.RootCause.ReferencedContextIds)}");
                sb.AppendLine();
            }

            if (c.Compliance != null)
            {
                sb.AppendLine("── IPC COMPLIANCE ──");
                sb.AppendLine($"  Standard:     {c.Compliance.ApplicableStandard} § {c.Compliance.Section}");
                sb.AppendLine($"  Class:        {c.Compliance.Classification}");
                sb.AppendLine($"  Disposition:  {c.Compliance.Disposition}");
                sb.AppendLine($"  Fully covered: {(c.Compliance.FullyCovered ? "Yes" : "No")}");
                sb.AppendLine($"  Retrieved:    [{string.Join(", ", c.Compliance.RetrievedSections)}]");
                sb.AppendLine($"  Checklist:");
                foreach (var item in c.Compliance.Checklist)
                    sb.AppendLine($"    [{(item.Addressed ? "X" : " ")}] {item.Requirement}  — {item.Evidence}" +
                                  (string.IsNullOrEmpty(item.SectionRef) ? "" : $" [{item.SectionRef}]"));
                sb.AppendLine();
            }

            if (c.HumanReviewRequired)
            {
                sb.AppendLine("── HUMAN REVIEW FLAGS ──");
                foreach (var reason in c.HumanReviewReasons)
                    sb.AppendLine($"  ⚠ {reason}");
                sb.AppendLine();
            }

            if (c.PolicyViolations.Any())
            {
                sb.AppendLine("── POLICY VIOLATIONS ──");
                foreach (var v in c.PolicyViolations)
                    sb.AppendLine($"  [{v.Action}] {v.Rule}: {v.Detail}");
                sb.AppendLine();
            }

            sb.AppendLine("── TRACE ──");
            foreach (var t in c.Trace)
                sb.AppendLine($"  {t.Timestamp:HH:mm:ss.fff} | {t.Tool,-30} | {t.Outcome,-20} | {t.Detail}");

            return sb.ToString();
        }

        // ─── Internal data models ────────────────────────────────────

        private class AnnotationContext
        {
            public List<BBox> BoundingBoxes { get; set; } = new();
            public List<string> CoOccurring { get; set; } = new();
            public double Density { get; set; }
        }

        private class BBox
        {
            public int X1 { get; set; }
            public int Y1 { get; set; }
            public int X2 { get; set; }
            public int Y2 { get; set; }
            public string DefectType { get; set; } = "";
        }
    }
}
