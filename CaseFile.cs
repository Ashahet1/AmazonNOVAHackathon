using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  CaseFile — single inspection case that flows through the MCP
    //  pipeline.  Every MCP tool appends its output here and writes a
    //  trace entry so the full decision chain is auditable.
    // ───────────────────────────────────────────────────────────────────

    public class CaseFile
    {
        // ── Identity ──
        public string CaseId { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string ImagePath { get; set; } = "";
        public string ProductType { get; set; } = "";

        // ── Step 1: Vision Analysis ──
        public VisionStepResult? VisionAnalysis { get; set; }

        // ── Step 2: Normalized Defect ──
        public NormalizedDefect? NormalizedDefect { get; set; }

        // ── Step 3: Knowledge Graph Context ──
        public KnowledgeGraphContext? GraphContext { get; set; }

        // ── Step 4: Root-Cause Actions ──
        public RootCauseResult? RootCause { get; set; }

        // ── Step 5: ISO Compliance Guidance ──
        public IsoComplianceResult? Compliance { get; set; }

        // ── Guardrail State ──
        public bool HumanReviewRequired { get; set; }
        public List<string> HumanReviewReasons { get; set; } = new();
        public List<PolicyViolation> PolicyViolations { get; set; } = new();

        // ── Trace ──
        public List<TraceEntry> Trace { get; set; } = new();

        // ── Overall Status ──
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CaseStatus Status { get; set; } = CaseStatus.Created;

        // ── Helpers ──
        public void AddTrace(string tool, string outcome, string? detail = null)
        {
            Trace.Add(new TraceEntry
            {
                Timestamp = DateTime.UtcNow,
                Tool = tool,
                Outcome = outcome,
                Detail = detail
            });
        }

        public void FlagForHumanReview(string reason)
        {
            HumanReviewRequired = true;
            HumanReviewReasons.Add(reason);
            Status = CaseStatus.ReviewRequired;
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }
    }

    public enum CaseStatus
    {
        Created,
        InProgress,
        Completed,
        ReviewRequired,
        Failed
    }

    // ── Step result models ──

    public class VisionStepResult
    {
        public string Caption { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public List<DetectedObjectInfo> Objects { get; set; } = new();
        public double Confidence { get; set; }

        // ── GPT-4.1 vision enrichment (not available from Azure AI Vision SDK) ──
        public string DefectType { get; set; } = "";
        public string DefectSeverity { get; set; } = "";
        public string DefectDescription { get; set; } = "";
    }

    public class DetectedObjectInfo
    {
        public string Name { get; set; } = "";
        public double Confidence { get; set; }
    }

    public class NormalizedDefect
    {
        public string DefectType { get; set; } = "";
        public string Severity { get; set; } = "";   // low | medium | high
        public string Product { get; set; } = "";
        public string Description { get; set; } = "";
        public string TaxonomyId { get; set; } = "";  // e.g. "DEF-SCRATCH-001"

        // ── AI-inferred fields (populated by GPT-4.1-nano, not hardcoded) ──
        public List<string> InspectionMethods { get; set; } = new(); // e.g. ["AOI","X-Ray"]
        public string Reasoning { get; set; } = "";                  // model’s explanation
    }

    public class KnowledgeGraphContext
    {
        public List<string> RelatedDefectIds { get; set; } = new();
        public List<string> EquipmentIds { get; set; } = new();
        public List<string> StandardIds { get; set; } = new();
        public List<HistoricalIncident> HistoricalIncidents { get; set; } = new();
        public List<string> IsoSnippets { get; set; } = new();

        // ── Annotation-enriched context ──
        public List<string> CoOccurringDefects { get; set; } = new();  // other defect types in same image
        public double DefectDensity { get; set; }                      // defects per unit area
    }

    public class HistoricalIncident
    {
        public string DefectNodeId { get; set; } = "";
        public string Product { get; set; } = "";
        public string DefectType { get; set; } = "";
        public string Severity { get; set; } = "";
    }

    public class RootCauseResult
    {
        public string ProbableCause { get; set; } = "";
        public List<string> ContributingFactors { get; set; } = new();
        public List<RecommendedAction> Actions { get; set; } = new();
        public List<string> ReferencedContextIds { get; set; } = new();  // traceability

        // ── AI-enriched fields ──
        public string Reasoning { get; set; } = "";   // model’s chain-of-thought
        public double Confidence { get; set; }          // model’s self-assessed confidence
    }

    public class RecommendedAction
    {
        public string Action { get; set; } = "";
        public string Priority { get; set; } = "";   // immediate | short-term | long-term
        public string Owner { get; set; } = "";       // e.g. "QE", "Maintenance"
        public string ContextRef { get; set; } = "";  // graph node / IPC section that justifies this action
    }

    public class IsoComplianceResult
    {
        public string ApplicableStandard { get; set; } = "";
        public string Section { get; set; } = "";
        public List<ComplianceCheckItem> Checklist { get; set; } = new();
        public List<string> ReferencedContextIds { get; set; } = new();
        public bool FullyCovered { get; set; }

        // ── RAG-enriched fields ──
        public List<string> RetrievedSections { get; set; } = new(); // IPC section IDs retrieved
        public string Classification { get; set; } = "";            // Class 1/2/3
        public string Disposition { get; set; } = "";               // accept | rework | reject
    }

    public class ComplianceCheckItem
    {
        public string Requirement { get; set; } = "";
        public bool Addressed { get; set; }
        public string Evidence { get; set; } = "";
        public string SectionRef { get; set; } = "";  // e.g. "IPC-A-600-2.2"
    }

    // ── Guardrail models ──

    public class PolicyViolation
    {
        public string Rule { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Action { get; set; } = "";  // "redacted" | "flagged" | "blocked"
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Trace ──

    public class TraceEntry
    {
        public DateTime Timestamp { get; set; }
        public string Tool { get; set; } = "";
        public string Outcome { get; set; } = "";
        public string? Detail { get; set; }
    }
}
