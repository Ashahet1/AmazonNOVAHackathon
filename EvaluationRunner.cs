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
    //  EvaluationRunner — Quality + Governance metrics
    //
    //  Runs a set of labeled test cases through the MCP pipeline and
    //  computes:
    //    1. Triage accuracy        — defect type / severity vs expected
    //    2. JSON validity rate     — % of agent outputs passing schema
    //    3. Traceability score     — % outputs citing retrieved context IDs
    //    4. Policy violation rate  — % outputs triggering redaction/flags
    //    5. Compliance completeness — coverage of required checklist items
    //
    //  Results are written to an exportable report.
    // ───────────────────────────────────────────────────────────────────

    public class EvaluationRunner
    {
        private readonly McpOrchestrator orchestrator;
        private readonly string testCasesPath;

        public EvaluationRunner(McpOrchestrator orchestrator, string testCasesPath)
        {
            this.orchestrator = orchestrator;
            this.testCasesPath = testCasesPath;
        }

        // ═══════════════════════════════════════════════════════════════
        //  RUN EVALUATION
        // ═══════════════════════════════════════════════════════════════

        public async Task<EvaluationReport> RunAsync()
        {
            Console.WriteLine("\n" + new string('═', 70));
            Console.WriteLine("  EVALUATION MODE — Running test-case suite");
            Console.WriteLine(new string('═', 70));

            var expectedLabels = LoadExpectedLabels();

            if (!expectedLabels.Any())
            {
                Console.WriteLine("  ⚠ No test cases found. Generate them first with option 18.");
                return new EvaluationReport();
            }

            Console.WriteLine($"  Loaded {expectedLabels.Count} test case(s)\n");

            var report = new EvaluationReport { RunTimestamp = DateTime.UtcNow };
            var caseResults = new List<EvaluationCaseResult>();

            int index = 0;
            foreach (var expected in expectedLabels)
            {
                index++;
                Console.WriteLine($"  [{index}/{expectedLabels.Count}] {Path.GetFileName(expected.ImagePath)}");

                if (!File.Exists(expected.ImagePath))
                {
                    Console.WriteLine($"    ⚠ File not found, skipping");
                    caseResults.Add(new EvaluationCaseResult
                    {
                        ImagePath = expected.ImagePath,
                        Skipped = true,
                        SkipReason = "File not found"
                    });
                    continue;
                }

                try
                {
                    var caseFile = await orchestrator.RunInspectionPipeline(
                        expected.ImagePath, expected.Product);

                    var result = EvaluateCase(caseFile, expected);
                    caseResults.Add(result);

                    Console.WriteLine($"    Type: {(result.DefectTypeMatch ? "MATCH" : "MISS")} " +
                                      $"| Severity: {(result.SeverityMatch ? "MATCH" : "MISS")} " +
                                      $"| JSON: {(result.JsonValid ? "OK" : "FAIL")} " +
                                      $"| Trace: {(result.HasTraceability ? "OK" : "FAIL")}");

                    // Rate-limit to avoid API throttling
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    ❌ Error: {ex.Message}");
                    caseResults.Add(new EvaluationCaseResult
                    {
                        ImagePath = expected.ImagePath,
                        Skipped = true,
                        SkipReason = ex.Message
                    });
                }
            }

            // ── Compute aggregate metrics ──
            report.CaseResults = caseResults;
            report.TotalCases = caseResults.Count;
            report.SkippedCases = caseResults.Count(c => c.Skipped);

            var evaluated = caseResults.Where(c => !c.Skipped).ToList();
            if (evaluated.Any())
            {
                report.TriageAccuracy_DefectType = (double)evaluated.Count(c => c.DefectTypeMatch) / evaluated.Count;
                report.TriageAccuracy_Severity = (double)evaluated.Count(c => c.SeverityMatch) / evaluated.Count;
                report.JsonValidityRate = (double)evaluated.Count(c => c.JsonValid) / evaluated.Count;
                report.TraceabilityScore = (double)evaluated.Count(c => c.HasTraceability) / evaluated.Count;
                report.PolicyViolationRate = (double)evaluated.Count(c => c.HasPolicyViolations) / evaluated.Count;
                report.ComplianceCompleteness = evaluated.Average(c => c.ComplianceCoverage);
                report.HumanReviewRate = (double)evaluated.Count(c => c.HumanReviewTriggered) / evaluated.Count;
            }

            return report;
        }

        // ═══════════════════════════════════════════════════════════════
        //  EVALUATE SINGLE CASE
        // ═══════════════════════════════════════════════════════════════

        private EvaluationCaseResult EvaluateCase(CaseFile caseFile, ExpectedLabel expected)
        {
            var result = new EvaluationCaseResult
            {
                ImagePath = expected.ImagePath,
                ExpectedDefectType = expected.ExpectedDefectType,
                ExpectedSeverity = expected.ExpectedSeverity,
                ActualDefectType = caseFile.NormalizedDefect?.DefectType ?? "",
                ActualSeverity = caseFile.NormalizedDefect?.Severity ?? ""
            };

            // 1. Triage accuracy
            result.DefectTypeMatch = result.ActualDefectType
                .Contains(expected.ExpectedDefectType, StringComparison.OrdinalIgnoreCase) ||
                expected.ExpectedDefectType
                .Contains(result.ActualDefectType, StringComparison.OrdinalIgnoreCase);

            result.SeverityMatch = string.Equals(
                result.ActualSeverity, expected.ExpectedSeverity, StringComparison.OrdinalIgnoreCase);

            // 2. JSON validity — check if root-cause and compliance outputs were valid JSON
            //    (If the guardrail didn't flag them, they parsed correctly)
            var schemaTraces = caseFile.Trace
                .Where(t => t.Outcome == "schema_valid" || t.Outcome == "schema_repaired")
                .ToList();
            var schemaFails = caseFile.Trace
                .Where(t => t.Outcome == "schema_fallback" || t.Outcome == "schema_invalid")
                .ToList();
            result.JsonValid = schemaFails.Count == 0 && schemaTraces.Count > 0;

            // 3. Traceability — check if reasoning outputs cited context IDs
            var traceTraces = caseFile.Trace
                .Where(t => t.Outcome == "traceability_pass")
                .ToList();
            result.HasTraceability = traceTraces.Any();

            // 4. Policy violations
            result.HasPolicyViolations = caseFile.PolicyViolations.Any();
            result.PolicyViolationCount = caseFile.PolicyViolations.Count;

            // 5. Compliance completeness — ratio of addressed checklist items
            if (caseFile.Compliance?.Checklist != null && caseFile.Compliance.Checklist.Any())
            {
                result.ComplianceCoverage = (double)caseFile.Compliance.Checklist.Count(c => c.Addressed)
                    / caseFile.Compliance.Checklist.Count;
            }

            // 6. Human review triggered?
            result.HumanReviewTriggered = caseFile.HumanReviewRequired;

            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LOAD EXPECTED LABELS
        // ═══════════════════════════════════════════════════════════════

        private List<ExpectedLabel> LoadExpectedLabels()
        {
            if (!File.Exists(testCasesPath))
            {
                Console.WriteLine($"  ⚠ Expected labels file not found: {testCasesPath}");
                Console.WriteLine("    Attempting to auto-generate from DeepPCB dataset...");
                return AutoGenerateLabels();
            }

            try
            {
                var json = File.ReadAllText(testCasesPath);
                return JsonSerializer.Deserialize<List<ExpectedLabel>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Error loading labels: {ex.Message}");
                return AutoGenerateLabels();
            }
        }

        /// <summary>
        /// Auto-generate test-case labels from the DeepPCB folder structure.
        /// Reads annotation files to get ground-truth defect types and maps
        /// them to expected labels for evaluation.
        /// Falls back to MVTec if DeepPCB is not present.
        /// </summary>
        private List<ExpectedLabel> AutoGenerateLabels()
        {
            var labels = new List<ExpectedLabel>();

            // Try DeepPCB first
            var deepPcbRoot = Path.Combine(Environment.CurrentDirectory, "datasets", "DeepPCB");
            if (!Directory.Exists(deepPcbRoot))
                deepPcbRoot = Path.Combine(Environment.CurrentDirectory, "datasets", "deeppcb");
            if (!Directory.Exists(deepPcbRoot))
            {
                Console.WriteLine($"  ⚠ DeepPCB dataset not found at {deepPcbRoot}");
                return labels;
            }

            // Find annotation files in the PCBData tree
            var pcbDataDir = Path.Combine(deepPcbRoot, "PCBData");
            var searchRoot = Directory.Exists(pcbDataDir) ? pcbDataDir : deepPcbRoot;

            var annotationFiles = Directory.GetFiles(searchRoot, "*.txt", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return !name.Equals("readme.txt", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("license.txt", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(f => f)
                .ToList();

            foreach (var annoFile in annotationFiles)
            {
                if (labels.Count >= 20) break;

                try
                {
                    var dir = Path.GetDirectoryName(annoFile) ?? "";
                    var baseName = Path.GetFileNameWithoutExtension(annoFile);

                    // Find test image
                    string? testImage = null;
                    foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
                    {
                        var candidate = Path.Combine(dir, baseName + "_test" + ext);
                        if (File.Exists(candidate)) { testImage = candidate; break; }
                    }
                    if (testImage == null) continue;

                    // Parse annotations to get defect types
                    var lines = File.ReadAllLines(annoFile)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

                    foreach (var line in lines)
                    {
                        if (labels.Count >= 20) break;

                        var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5 && int.TryParse(parts[4], out var classId) && classId >= 0 && classId <= 5)
                        {
                            var defectName = DeepPCBProcessor.DefectClassNames.GetValueOrDefault(classId, "unknown");
                            var severity = DeepPCBProcessor.DefectSeverity.GetValueOrDefault(defectName, "medium");

                            labels.Add(new ExpectedLabel
                            {
                                ImagePath = testImage,
                                Product = "pcb",
                                ExpectedDefectType = defectName,
                                ExpectedSeverity = severity
                            });
                            break; // one label per image
                        }
                    }
                }
                catch { /* skip bad files */ }
            }

            Console.WriteLine($"  Auto-generated {labels.Count} test case(s) from DeepPCB structure");
            return labels;
        }

        private static string DetermineExpectedSeverity(string defectCategory)
        {
            // Use DeepPCB severity mapping if available
            if (DeepPCBProcessor.DefectSeverity.TryGetValue(defectCategory.ToLower(), out var sev))
                return sev;

            defectCategory = defectCategory.ToLower();
            if (defectCategory.Contains("large") || defectCategory.Contains("severe"))
                return "high";
            if (defectCategory.Contains("small") || defectCategory.Contains("minor"))
                return "low";
            return "medium";
        }

        // ═══════════════════════════════════════════════════════════════
        //  REPORT DISPLAY + EXPORT
        // ═══════════════════════════════════════════════════════════════

        public static void DisplayReport(EvaluationReport report)
        {
            Console.WriteLine("\n" + new string('═', 70));
            Console.WriteLine("  EVALUATION REPORT");
            Console.WriteLine(new string('═', 70));
            Console.WriteLine($"  Run:  {report.RunTimestamp:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"  Cases: {report.TotalCases} total, {report.SkippedCases} skipped\n");

            var metrics = new (string Name, double Value, string Description)[]
            {
                ("Triage Accuracy (Type)", report.TriageAccuracy_DefectType,
                    "Defect type matches expected label"),
                ("Triage Accuracy (Severity)", report.TriageAccuracy_Severity,
                    "Severity matches expected label"),
                ("JSON Validity Rate", report.JsonValidityRate,
                    "Agent outputs passing schema validation"),
                ("Traceability Score", report.TraceabilityScore,
                    "Outputs citing retrieved context IDs"),
                ("Policy Violation Rate", report.PolicyViolationRate,
                    "Outputs triggering redaction/flags"),
                ("Compliance Completeness", report.ComplianceCompleteness,
                    "Coverage of required checklist items"),
                ("Human Review Rate", report.HumanReviewRate,
                    "Cases flagged for human sign-off")
            };

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  ┌──────────────────────────────┬──────────┬───────────────────────────────────┐");
            Console.WriteLine("  │ Metric                       │ Score    │ Description                       │");
            Console.WriteLine("  ├──────────────────────────────┼──────────┼───────────────────────────────────┤");
            Console.ResetColor();

            foreach (var (name, value, desc) in metrics)
            {
                var scoreStr = $"{value:P0}";
                var color = value >= 0.8 ? ConsoleColor.Green :
                            value >= 0.5 ? ConsoleColor.Yellow : ConsoleColor.Red;

                // Policy violation rate: invert color logic (lower is better)
                if (name.Contains("Violation") || name.Contains("Human Review"))
                    color = value <= 0.2 ? ConsoleColor.Green :
                            value <= 0.5 ? ConsoleColor.Yellow : ConsoleColor.Red;

                Console.Write("  │ ");
                Console.Write($"{name,-28}");
                Console.Write(" │ ");
                Console.ForegroundColor = color;
                Console.Write($"{scoreStr,-8}");
                Console.ResetColor();
                Console.Write(" │ ");
                Console.Write($"{desc,-33}");
                Console.WriteLine(" │");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  └──────────────────────────────┴──────────┴───────────────────────────────────┘");
            Console.ResetColor();

            // Per-case breakdown
            Console.WriteLine("\n  PER-CASE BREAKDOWN:");
            Console.WriteLine("  " + new string('─', 66));

            foreach (var c in report.CaseResults.Where(c => !c.Skipped))
            {
                var img = Path.GetFileName(c.ImagePath);
                var typeIcon = c.DefectTypeMatch ? "✓" : "✗";
                var sevIcon = c.SeverityMatch ? "✓" : "✗";
                var jsonIcon = c.JsonValid ? "✓" : "✗";
                var traceIcon = c.HasTraceability ? "✓" : "✗";

                Console.Write($"  {img,-30} ");
                WriteColored(typeIcon, c.DefectTypeMatch);
                Console.Write($"Type ");
                WriteColored(sevIcon, c.SeverityMatch);
                Console.Write($"Sev ");
                WriteColored(jsonIcon, c.JsonValid);
                Console.Write($"JSON ");
                WriteColored(traceIcon, c.HasTraceability);
                Console.Write($"Trace ");

                if (c.HumanReviewTriggered)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("⚠HR");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
        }

        public static string ExportReport(EvaluationReport report)
        {
            var outputDir = Path.Combine(Environment.CurrentDirectory, "outputs", "evaluations");
            Directory.CreateDirectory(outputDir);

            var filename = $"Evaluation_{report.RunTimestamp:yyyyMMdd_HHmmss}.json";
            var path = Path.Combine(outputDir, filename);

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(path, json);
            return path;
        }

        private static void WriteColored(string text, bool good)
        {
            Console.ForegroundColor = good ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(text);
            Console.ResetColor();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Data models for evaluation
    // ═══════════════════════════════════════════════════════════════

    public class ExpectedLabel
    {
        public string ImagePath { get; set; } = "";
        public string Product { get; set; } = "";
        public string ExpectedDefectType { get; set; } = "";
        public string ExpectedSeverity { get; set; } = "";
    }

    public class EvaluationReport
    {
        public DateTime RunTimestamp { get; set; }
        public int TotalCases { get; set; }
        public int SkippedCases { get; set; }

        // Aggregate metrics
        public double TriageAccuracy_DefectType { get; set; }
        public double TriageAccuracy_Severity { get; set; }
        public double JsonValidityRate { get; set; }
        public double TraceabilityScore { get; set; }
        public double PolicyViolationRate { get; set; }
        public double ComplianceCompleteness { get; set; }
        public double HumanReviewRate { get; set; }

        // Per-case
        public List<EvaluationCaseResult> CaseResults { get; set; } = new();
    }

    public class EvaluationCaseResult
    {
        public string ImagePath { get; set; } = "";
        public bool Skipped { get; set; }
        public string? SkipReason { get; set; }

        public string ExpectedDefectType { get; set; } = "";
        public string ExpectedSeverity { get; set; } = "";
        public string ActualDefectType { get; set; } = "";
        public string ActualSeverity { get; set; } = "";

        public bool DefectTypeMatch { get; set; }
        public bool SeverityMatch { get; set; }
        public bool JsonValid { get; set; }
        public bool HasTraceability { get; set; }
        public bool HasPolicyViolations { get; set; }
        public int PolicyViolationCount { get; set; }
        public double ComplianceCoverage { get; set; }
        public bool HumanReviewTriggered { get; set; }
    }
}
