using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Runtime.Documents;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  AgentTools — the three callable tools Nova can invoke during
    //  the agentic loop (Step 7 of the MCP pipeline).
    //
    //  Tool 1: quarantine_batch       → outputs/quarantine_log.jsonl
    //  Tool 2: update_knowledge_graph → updates in-memory graph + cache
    //  Tool 3: file_work_order        → outputs/work_orders/WO_*.json
    //
    //  Each tool is defined with a JSON schema (ToolInputSchema)
    //  so Nova knows exactly what arguments to supply.
    //  ExecuteAsync dispatches by tool name and returns a result string
    //  that goes back into the Converse conversation as a ToolResult.
    // ───────────────────────────────────────────────────────────────────

    public class AgentAction
    {
        public string ToolName    { get; set; } = "";
        public string Input       { get; set; } = "";
        public string Result      { get; set; } = "";
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    }

    public static class AgentTools
    {
        // ─── Helper: safely read a string from a Document dictionary ───
        private static string S(Document doc, string key, string def = "")
        {
            var d = doc.AsDictionary();
            return d.TryGetValue(key, out var v) ? v.AsString() : def;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Tool definitions — passed to Nova via ToolConfig
        // ═══════════════════════════════════════════════════════════════

        public static List<Amazon.BedrockRuntime.Model.Tool> GetToolDefinitions()
        {
            return new List<Amazon.BedrockRuntime.Model.Tool>
            {
                // ── Tool 1: quarantine_batch ──
                new Amazon.BedrockRuntime.Model.Tool
                {
                    ToolSpec = new Amazon.BedrockRuntime.Model.ToolSpecification
                    {
                        Name        = "quarantine_batch",
                        Description = "Quarantines a production batch by writing a permanent quarantine record. " +
                                      "Use this when the defect is high severity, poses a safety risk, " +
                                      "or when a systemic process failure is suspected.",
                        InputSchema = new Amazon.BedrockRuntime.Model.ToolInputSchema
                        {
                            Json = new Document(new Dictionary<string, Document>
                            {
                                ["type"] = new Document("object"),
                                ["properties"] = new Document(new Dictionary<string, Document>
                                {
                                    ["batchId"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Production batch identifier (e.g. BATCH-2026-0303-A)")
                                    }),
                                    ["reason"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Specific reason for quarantine based on defect analysis")
                                    }),
                                    ["severity"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Defect severity: low, medium, or high")
                                    })
                                }),
                                ["required"] = new Document(new List<Document>
                                {
                                    new Document("batchId"),
                                    new Document("reason"),
                                    new Document("severity")
                                })
                            })
                        }
                    }
                },

                // ── Tool 2: update_knowledge_graph ──
                new Amazon.BedrockRuntime.Model.Tool
                {
                    ToolSpec = new Amazon.BedrockRuntime.Model.ToolSpecification
                    {
                        Name        = "update_knowledge_graph",
                        Description = "Updates the manufacturing knowledge graph with new defect pattern data " +
                                      "discovered during this inspection. Use to record co-occurring defects " +
                                      "or adjust severity scores based on confirmed findings.",
                        InputSchema = new Amazon.BedrockRuntime.Model.ToolInputSchema
                        {
                            Json = new Document(new Dictionary<string, Document>
                            {
                                ["type"] = new Document("object"),
                                ["properties"] = new Document(new Dictionary<string, Document>
                                {
                                    ["defectType"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("The primary defect type confirmed in this inspection")
                                    }),
                                    ["coOccurringDefect"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("A second defect that co-occurred with the primary (empty string if none)")
                                    }),
                                    ["severityAdjustment"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Severity adjustment: increase, decrease, or unchanged")
                                    }),
                                    ["notes"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Brief note about what was learned from this inspection")
                                    })
                                }),
                                ["required"] = new Document(new List<Document>
                                {
                                    new Document("defectType"),
                                    new Document("severityAdjustment")
                                })
                            })
                        }
                    }
                },

                // ── Tool 3: file_work_order ──
                new Amazon.BedrockRuntime.Model.Tool
                {
                    ToolSpec = new Amazon.BedrockRuntime.Model.ToolSpecification
                    {
                        Name        = "file_work_order",
                        Description = "Creates a structured work order assigned to the responsible process engineer. " +
                                      "Use to initiate corrective action for process adjustments, equipment checks, " +
                                      "or re-inspection tasks.",
                        InputSchema = new Amazon.BedrockRuntime.Model.ToolInputSchema
                        {
                            Json = new Document(new Dictionary<string, Document>
                            {
                                ["type"] = new Document("object"),
                                ["properties"] = new Document(new Dictionary<string, Document>
                                {
                                    ["defectType"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("The defect type this work order addresses")
                                    }),
                                    ["action"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Specific corrective action to be performed")
                                    }),
                                    ["priority"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Work order priority: P1 (critical), P2 (high), P3 (medium), P4 (low)")
                                    }),
                                    ["assignee"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Role or team responsible (e.g. process_engineer, qa_team, maintenance)")
                                    }),
                                    ["batchId"] = new Document(new Dictionary<string, Document>
                                    {
                                        ["type"]        = new Document("string"),
                                        ["description"] = new Document("Associated batch ID if applicable")
                                    })
                                }),
                                ["required"] = new Document(new List<Document>
                                {
                                    new Document("defectType"),
                                    new Document("action"),
                                    new Document("priority"),
                                    new Document("assignee")
                                })
                            })
                        }
                    }
                }
            };
        }

        // ═══════════════════════════════════════════════════════════════
        //  Dispatcher — routes tool call to the correct implementation
        // ═══════════════════════════════════════════════════════════════

        public static async Task<string> ExecuteAsync(
            string toolName,
            Document input,
            KnowledgeGraph graph,
            CaseFile caseFile)
        {
            Console.WriteLine($"\n  ⚙️  Executing tool: {toolName}");

            string result;
            switch (toolName)
            {
                case "quarantine_batch":
                    result = await QuarantineBatchAsync(input);
                    break;
                case "update_knowledge_graph":
                    result = UpdateKnowledgeGraph(input, graph, caseFile);
                    break;
                case "file_work_order":
                    result = await FileWorkOrderAsync(input, caseFile);
                    break;
                default:
                    result = $"ERROR: Unknown tool '{toolName}'";
                    break;
            }

            // Record action in case file — convert Document to plain string dict to avoid cycles
            var plainInput = new Dictionary<string, string>();
            try
            {
                foreach (var kv in input.AsDictionary())
                    plainInput[kv.Key] = kv.Value.AsString();
            }
            catch { /* best-effort */ }

            caseFile.AgentActions.Add(new AgentAction
            {
                ToolName    = toolName,
                Input       = JsonSerializer.Serialize(plainInput),
                Result      = result,
                ExecutedAt  = DateTime.UtcNow
            });

            caseFile.AddTrace("agentic_loop", $"tool_executed:{toolName}", result[..Math.Min(120, result.Length)]);
            Console.WriteLine($"     → {result}");
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Tool 1 — quarantine_batch
        //  Writes a quarantine record to outputs/quarantine_log.jsonl
        // ═══════════════════════════════════════════════════════════════

        private static async Task<string> QuarantineBatchAsync(Document input)
        {
            var batchId  = S(input, "batchId",  $"BATCH-{DateTime.UtcNow:yyyyMMdd}-AUTO");
            var reason   = S(input, "reason",   "Defect detected during MCP inspection");
            var severity = S(input, "severity", "medium");

            var record = new
            {
                BatchId       = batchId,
                QuarantinedAt = DateTime.UtcNow.ToString("O"),
                Reason        = reason,
                Severity      = severity,
                Status        = "QUARANTINED",
                Source        = "MCP_Agent_Pipeline"
            };

            var outDir  = Path.Combine(Environment.CurrentDirectory, "outputs");
            Directory.CreateDirectory(outDir);
            var logFile = Path.Combine(outDir, "quarantine_log.jsonl");

            await File.AppendAllTextAsync(logFile, JsonSerializer.Serialize(record) + "\n");

            return $"✅ Batch '{batchId}' quarantined (severity: {severity}). " +
                   $"Record appended to outputs/quarantine_log.jsonl. Status: QUARANTINED";
        }

        // ═══════════════════════════════════════════════════════════════
        //  Tool 2 — update_knowledge_graph
        //  Adds a co-occurrence edge or updates defect metadata
        // ═══════════════════════════════════════════════════════════════

        private static string UpdateKnowledgeGraph(Document input, KnowledgeGraph graph, CaseFile caseFile)
        {
            var defectType         = S(input, "defectType",         caseFile.NormalizedDefect?.DefectType ?? "unknown");
            var coOccurring        = S(input, "coOccurringDefect",  "");
            var severityAdj        = S(input, "severityAdjustment", "unchanged");
            var notes              = S(input, "notes",              "");

            var updates = new List<string>();

            // Add co-occurrence relationship if specified
            if (!string.IsNullOrWhiteSpace(coOccurring))
            {
                var fromId = $"defect_{defectType}";
                var toId   = $"defect_{coOccurring}";

                // Only add if both nodes exist
                var fromNode = graph.GetNodeById(fromId);
                var toNode   = graph.GetNodeById(toId);

                if (fromNode != null && toNode != null)
                {
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId   = fromId,
                        ToNodeId     = toId,
                        RelationType = "co_occurs_with",
                        Confidence   = 0.8
                    });
                    updates.Add($"added co-occurrence edge: {defectType} ↔ {coOccurring}");
                }
                else
                {
                    updates.Add($"note: co-occurrence nodes not found in graph (defect type may be new)");
                }
            }

            // Record severity feedback
            if (severityAdj != "unchanged")
                updates.Add($"severity feedback recorded: {defectType} → {severityAdj}");

            if (!string.IsNullOrWhiteSpace(notes))
                updates.Add($"notes logged: {notes}");

            // Persist updated graph
            graph.SaveToFile("knowledge_graph.json");

            var summary = updates.Count > 0
                ? string.Join("; ", updates)
                : "no structural changes — metadata noted";

            return $"✅ Knowledge graph updated for '{defectType}': {summary}. Graph saved.";
        }

        // ═══════════════════════════════════════════════════════════════
        //  Tool 3 — file_work_order
        //  Writes a work order JSON to outputs/work_orders/
        // ═══════════════════════════════════════════════════════════════

        private static async Task<string> FileWorkOrderAsync(Document input, CaseFile caseFile)
        {
            var defectType = S(input, "defectType", caseFile.NormalizedDefect?.DefectType ?? "unknown");
            var action     = S(input, "action",     "Investigate and correct");
            var priority   = S(input, "priority",   "P2");
            var assignee   = S(input, "assignee",   "process_engineer");
            var batchId    = S(input, "batchId",    "");

            var workOrderId = $"WO-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{caseFile.CaseId[..6].ToUpper()}";

            var workOrder = new
            {
                WorkOrderId  = workOrderId,
                CreatedAt    = DateTime.UtcNow.ToString("O"),
                CaseId       = caseFile.CaseId,
                BatchId      = batchId,
                DefectType   = defectType,
                Action       = action,
                Priority     = priority,
                Assignee     = assignee,
                Status       = "OPEN",
                Source       = "MCP_Agent_Pipeline",
                ImagePath    = Path.GetFileName(caseFile.ImagePath)
            };

            var outDir = Path.Combine(Environment.CurrentDirectory, "outputs", "work_orders");
            Directory.CreateDirectory(outDir);
            var filePath = Path.Combine(outDir, $"{workOrderId}.json");

            await File.WriteAllTextAsync(filePath,
                JsonSerializer.Serialize(workOrder, new JsonSerializerOptions { WriteIndented = true }));

            return $"✅ Work order '{workOrderId}' filed (priority: {priority}, assignee: {assignee}). " +
                   $"Action: \"{action}\". Saved to outputs/work_orders/{workOrderId}.json";
        }
    }
}
