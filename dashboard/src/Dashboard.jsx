import { useState, useEffect, useRef, useCallback } from "react";

const FONTS = `
@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500;600&family=Barlow+Condensed:wght@300;400;600;700;800&family=Barlow:wght@300;400;500&display=swap');
`;

const PIPELINE_STEPS = [
  { id: 1, label: "analyze_image_with_vision", model: "Nova Pro", icon: "👁", color: "#00d4ff" },
  { id: 2, label: "normalize_defect_with_ai", model: "Nova Lite", icon: "⚙", color: "#00d4ff" },
  { id: 3, label: "query_knowledge_graph", model: "In-Memory", icon: "🔗", color: "#00d4ff" },
  { id: 4, label: "root_cause_enriched", model: "Nova Lite", icon: "🔍", color: "#00d4ff" },
  { id: 5, label: "compliance_with_rag", model: "Nova Lite", icon: "📋", color: "#00d4ff" },
  { id: "P", label: "policy_checks", model: "Rule Engine", icon: "🛡", color: "#f59e0b" },
  { id: "G", label: "final_review_gate", model: "Rule Engine", icon: "🚦", color: "#f59e0b" },
  { id: 7, label: "agentic_action_loop", model: "Nova Lite", icon: "🤖", color: "#10b981" },
];

const AGENT_ACTIONS = [
  { tool: "quarantine_batch", batchId: "BATCH-20260303-0CD549", severity: "high", priority: "CRITICAL", icon: "🚫", color: "#ef4444", detail: "Batch quarantined — record appended to quarantine_log.jsonl" },
  { tool: "update_knowledge_graph", defect: "open", adjustment: "severity → increase", icon: "🧠", color: "#8b5cf6", detail: "Co-occurrence edge added: open ↔ pin_hole. Graph saved." },
  { tool: "file_work_order", id: "WO-20260303-180947", priority: "P1", assignee: "process_engineer", icon: "📝", color: "#ef4444", detail: "Inspect and calibrate the etching machine settings" },
  { tool: "file_work_order", id: "WO-20260303-180948", priority: "P2", assignee: "qa_team", icon: "📝", color: "#f59e0b", detail: "Perform a thorough check of the etching solution concentration" },
  { tool: "file_work_order", id: "WO-20260303-180949", priority: "P3", assignee: "maintenance", icon: "📝", color: "#10b981", detail: "Review and update the preventive maintenance schedule" },
];

const DEFECT_DATA = [
  { type: "open", count: 89, pct: 100, severity: "high" },
  { type: "short", count: 67, pct: 75, severity: "high" },
  { type: "pin_hole", count: 54, pct: 61, severity: "low" },
  { type: "mousebite", count: 48, pct: 54, severity: "medium" },
  { type: "spurious_copper", count: 41, pct: 46, severity: "medium" },
  { type: "spur", count: 28, pct: 31, severity: "medium" },
];

const RECENT_CASES = [
  { id: "0cd549", image: "00041005_test.jpg", defect: "open", severity: "HIGH", status: "QUARANTINED", actions: 5, time: "13s" },
  { id: "1ef823", image: "00041006_test.jpg", defect: "short", severity: "HIGH", status: "WORK ORDER", actions: 4, time: "11s" },
  { id: "2ab441", image: "00041007_test.jpg", defect: "pin_hole", severity: "LOW", status: "PASSED", actions: 2, time: "9s" },
  { id: "3cd990", image: "00041008_test.jpg", defect: "mousebite", severity: "MED", status: "REVIEW", actions: 3, time: "12s" },
];

const SEVERITY_COLORS = { high: "#ef4444", medium: "#f59e0b", low: "#10b981" };
const STATUS_COLORS = { QUARANTINED: "#ef4444", "WORK ORDER": "#f59e0b", PASSED: "#10b981", REVIEW: "#8b5cf6" };

const MENU_OPTIONS = [
  { id: 1, icon: "🏭", label: "Inspect single PCB image", sub: "MCP pipeline · Nova Pro", accent: "#00d4ff" },
  { id: 2, icon: "🔁", label: "Batch inspect", sub: "N images per defect category", accent: "#8b5cf6" },
  { id: 3, icon: "📊", label: "Defect statistics", sub: "Full dashboard", accent: "#f59e0b" },
  { id: 4, icon: "🤖", label: "AI insights", sub: "Knowledge graph · Nova Lite", accent: "#10b981" },
  { id: 5, icon: "📂", label: "View / export case", sub: "Last case report", accent: "#00d4ff" },
  { id: 6, icon: "❌", label: "Exit", sub: "End session", accent: "#ef4444" },
];

const BATCH_RESULTS = [
  { category: "open",            total: 2, done: 1, review: 1, failed: 0, violations: 1, time: "13s" },
  { category: "short",           total: 2, done: 2, review: 0, failed: 0, violations: 0, time: "11s" },
  { category: "mousebite",       total: 2, done: 2, review: 0, failed: 0, violations: 0, time: "10s" },
  { category: "spur",            total: 2, done: 1, review: 1, failed: 0, violations: 0, time: "12s" },
  { category: "pin_hole",        total: 2, done: 2, review: 0, failed: 0, violations: 0, time: "9s"  },
  { category: "spurious_copper", total: 2, done: 2, review: 0, failed: 0, violations: 1, time: "11s" },
];

const AI_INSIGHTS = [
  {
    num: 1, title: "Cross-Product Knowledge Transfer",
    accent: "#00d4ff",
    body: "The knowledge graph contains 1,308 relationships linking defects across 50 PCB images. 'open' and 'short' defects co-occur in 23 boards — a single etching process fix can eliminate both simultaneously, reducing rework cost by an estimated 40%.",
    action: "Standardize the etching bath chemistry SOP across all production lines.",
  },
  {
    num: 2, title: "Equipment Optimization",
    accent: "#8b5cf6",
    body: "The etching_bath_controller node has the highest betweenness centrality (347 edges) in the graph. It appears in root-cause chains for 4 of 6 defect types. A single calibration event for this equipment would reduce defect incidence across all categories.",
    action: "Schedule monthly calibration for etching bath controller — estimated 60% reduction in open/short defects.",
  },
  {
    num: 3, title: "Manufacturing Knowledge Base Value",
    accent: "#f59e0b",
    body: "388 nodes and 1,308 edges were automatically built from 50 images without manual labeling. Each new inspection adds co-occurrence edges and severity feedback. The graph self-improves — root cause confidence increases with every run.",
    action: "Expand the dataset to 500 images to achieve 95%+ root cause accuracy through graph density.",
  },
  {
    num: 4, title: "IPC-A-600J Compliance Posture",
    accent: "#10b981",
    body: "9 IPC standard sections are linked in the knowledge graph. Current inspection data shows § 2.4 (Conductor Spacing) is the most frequently violated section — present in 67% of high-severity cases. All rejected boards correctly map to Class 3 High Reliability requirements.",
    action: "Auto-generate IPC compliance reports per batch and deliver to QA team within 1 hour of inspection.",
  },
];

const TRACE_LOG = [
  { time: "18:09:34.201", tool: "analyze_image_with_vision",      outcome: "ok",       detail: "Nova Pro · caption len=142 · conf=0.85" },
  { time: "18:09:35.887", tool: "normalize_defect_with_ai",       outcome: "ok",       detail: "open · high · DEF-OPEN-PCB · taxonomy matched" },
  { time: "18:09:35.891", tool: "query_knowledge_graph",          outcome: "ok",       detail: "14 related defects, 3 equipment, 7 historical" },
  { time: "18:09:37.442", tool: "root_cause_enriched",            outcome: "ok",       detail: "etching bath anomaly · conf=0.85 · 5 factors" },
  { time: "18:09:39.114", tool: "compliance_with_rag",            outcome: "ok",       detail: "IPC-A-600J §2.2/§2.4 · 2/4 failed · REJECT" },
  { time: "18:09:39.116", tool: "policy_checks",                  outcome: "violation",detail: "high severity → quarantine required" },
  { time: "18:09:39.117", tool: "final_review_gate",              outcome: "ok",       detail: "REJECT disposition confirmed · no human review" },
  { time: "18:09:39.120", tool: "agentic_action_loop[call 1/5]",  outcome: "ok",       detail: "quarantine_batch BATCH-20260303-0CD549" },
  { time: "18:09:39.984", tool: "agentic_action_loop[call 2/5]",  outcome: "ok",       detail: "update_knowledge_graph open↔pin_hole edge+1" },
  { time: "18:09:40.771", tool: "agentic_action_loop[call 3/5]",  outcome: "ok",       detail: "file_work_order WO-180947 P1 process_engineer" },
  { time: "18:09:41.558", tool: "agentic_action_loop[call 4/5]",  outcome: "ok",       detail: "file_work_order WO-180948 P2 qa_team" },
  { time: "18:09:42.345", tool: "agentic_action_loop[call 5/5]",  outcome: "ok",       detail: "file_work_order WO-180949 P3 maintenance" },
  { time: "18:09:47.203", tool: "pipeline_complete",              outcome: "ok",       detail: "5 actions taken · 13.002s total · exit 0" },
];

const STATS_TABLE = [
  { category: "open",            images: 9,  defects: 89, highPct: "100%", codes: "0" },
  { category: "short",           images: 8,  defects: 67, highPct: "88%",  codes: "1" },
  { category: "mousebite",       images: 8,  defects: 48, highPct: "0%",   codes: "2" },
  { category: "spur",            images: 7,  defects: 28, highPct: "0%",   codes: "3" },
  { category: "pin_hole",        images: 9,  defects: 54, highPct: "0%",   codes: "4" },
  { category: "spurious_copper", images: 9,  defects: 41, highPct: "0%",   codes: "5" },
];

function useCyclingInspection() {
  const [activeStep, setActiveStep] = useState(-1);
  const [completedSteps, setCompletedSteps] = useState([]);
  const [running, setRunning] = useState(false);
  const [done, setDone] = useState(false);

  const run = () => {
    setCompletedSteps([]);
    setActiveStep(0);
    setRunning(true);
    setDone(false);
  };

  useEffect(() => {
    if (!running) return;
    if (activeStep >= PIPELINE_STEPS.length) {
      setRunning(false);
      setDone(true);
      setActiveStep(-1);
      return;
    }
    const delay = activeStep === PIPELINE_STEPS.length - 1 ? 5000 : 900;
    const t = setTimeout(() => {
      setCompletedSteps((p) => [...p, activeStep]);
      setActiveStep((p) => p + 1);
    }, delay);
    return () => clearTimeout(t);
  }, [activeStep, running]);

  return { activeStep, completedSteps, running, done, run };
}

function PipelineStep({ step, idx, isActive, isDone }) {
  const status = isDone ? "done" : isActive ? "active" : "idle";
  return (
    <div style={{
      display: "flex", alignItems: "center", gap: 10, padding: "7px 12px",
      background: status === "active" ? "rgba(0,212,255,0.08)" : status === "done" ? "rgba(16,185,129,0.06)" : "transparent",
      border: `1px solid ${status === "active" ? "#00d4ff40" : status === "done" ? "#10b98130" : "#ffffff10"}`,
      borderRadius: 6, transition: "all 0.3s ease",
    }}>
      <div style={{
        width: 28, height: 28, borderRadius: "50%", display: "flex", alignItems: "center", justifyContent: "center", fontSize: 11,
        fontFamily: "'IBM Plex Mono', monospace", fontWeight: 600,
        background: status === "active" ? "#00d4ff20" : status === "done" ? "#10b98120" : "#ffffff08",
        border: `1.5px solid ${status === "active" ? "#00d4ff" : status === "done" ? "#10b981" : "#ffffff20"}`,
        color: status === "active" ? "#00d4ff" : status === "done" ? "#10b981" : "#ffffff40",
        animation: status === "active" ? "pulse 1.2s ease-in-out infinite" : "none",
      }}>
        {status === "done" ? "✓" : step.id}
      </div>
      <div style={{ flex: 1 }}>
        <div style={{
          fontFamily: "'IBM Plex Mono', monospace", fontSize: 11,
          color: status === "active" ? "#00d4ff" : status === "done" ? "#10b981" : "#ffffff50",
          fontWeight: status === "active" ? 600 : 400,
        }}>
          {step.label}
        </div>
        <div style={{ fontSize: 9, color: "#ffffff30", fontFamily: "'Barlow', sans-serif", marginTop: 1 }}>
          {step.model}
        </div>
      </div>
      {status === "active" && (
        <div style={{ display: "flex", gap: 3 }}>
          {[0, 1, 2].map(i => (
            <div key={i} style={{
              width: 4, height: 4, borderRadius: "50%", background: "#00d4ff",
              animation: `bounce 0.9s ease-in-out ${i * 0.2}s infinite`,
            }} />
          ))}
        </div>
      )}
      {status === "done" && (
        <div style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#10b981" }}>OK</div>
      )}
    </div>
  );
}

function AgentActionCard({ action, delay }) {
  const [visible, setVisible] = useState(false);
  useEffect(() => {
    const t = setTimeout(() => setVisible(true), delay);
    return () => clearTimeout(t);
  }, [delay]);

  const priorityColor = action.priority === "P1" || action.priority === "CRITICAL" ? "#ef4444"
    : action.priority === "P2" ? "#f59e0b" : "#10b981";

  return (
    <div style={{
      padding: "10px 14px", background: `${action.color}08`,
      border: `1px solid ${action.color}25`, borderRadius: 8,
      opacity: visible ? 1 : 0, transform: visible ? "translateX(0)" : "translateX(20px)",
      transition: "all 0.4s ease", marginBottom: 8,
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
        <span style={{ fontSize: 14 }}>{action.icon}</span>
        <span style={{
          fontFamily: "'IBM Plex Mono', monospace", fontSize: 10, fontWeight: 600,
          color: action.color, letterSpacing: "0.05em",
        }}>{action.tool}</span>
        {action.priority && (
          <span style={{
            marginLeft: "auto", padding: "1px 6px", borderRadius: 3,
            background: `${priorityColor}20`, border: `1px solid ${priorityColor}50`,
            fontSize: 9, fontFamily: "'IBM Plex Mono', monospace",
            color: priorityColor, fontWeight: 700,
          }}>{action.priority}</span>
        )}
      </div>
      <div style={{ fontSize: 10, color: "#ffffff60", fontFamily: "'Barlow', sans-serif", lineHeight: 1.5 }}>
        {action.detail}
      </div>
      {action.assignee && (
        <div style={{ marginTop: 4, fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff35" }}>
          → {action.assignee}
        </div>
      )}
    </div>
  );
}

function DefectBar({ defect, delay }) {
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const t = setTimeout(() => setWidth(defect.pct), delay);
    return () => clearTimeout(t);
  }, [delay]);
  const color = SEVERITY_COLORS[defect.severity];
  return (
    <div style={{ marginBottom: 10 }}>
      <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 4 }}>
        <span style={{ fontFamily: "'IBM Plex Mono', monospace", fontSize: 10, color: "#ffffff80" }}>
          {defect.type}
        </span>
        <span style={{ fontFamily: "'IBM Plex Mono', monospace", fontSize: 10, color }}>
          {defect.count}
        </span>
      </div>
      <div style={{ background: "#ffffff10", borderRadius: 2, height: 4, overflow: "hidden" }}>
        <div style={{
          height: "100%", background: `linear-gradient(90deg, ${color}90, ${color})`,
          width: `${width}%`, transition: "width 1s cubic-bezier(0.16,1,0.3,1)",
          borderRadius: 2,
        }} />
      </div>
    </div>
  );
}

function StatCard({ label, value, sub, accent }) {
  return (
    <div style={{
      padding: "14px 16px", background: "#ffffff05",
      border: `1px solid #ffffff10`, borderRadius: 8, flex: 1,
    }}>
      <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 11, color: "#ffffff40", letterSpacing: "0.1em", textTransform: "uppercase", marginBottom: 6 }}>
        {label}
      </div>
      <div style={{ fontFamily: "'IBM Plex Mono', monospace", fontSize: 26, fontWeight: 600, color: accent || "#ffffff", lineHeight: 1 }}>
        {value}
      </div>
      {sub && <div style={{ fontFamily: "'Barlow', sans-serif", fontSize: 10, color: "#ffffff35", marginTop: 4 }}>{sub}</div>}
    </div>
  );
}

const CASE_DATA = {
  caseId: "0cd5493612dd",
  timestamp: "2026-03-03T18:09:47Z",
  image: "PCBData/group00041/00041/00041005_test.jpg",
  productType: "pcb_deeppcb",
  status: "Completed",
  humanReviewRequired: false,
  // Vision step
  vision: {
    caption: "PCB board with visible open circuit — broken copper trace detected near component pad area. Surface irregularity consistent with over-etching. Surrounding region shows pin-hole clusters.",
    confidence: 0.85,
    tags: ["open_circuit", "broken_trace", "over_etching", "pin_hole", "copper_surface"],
  },
  // Normalized defect
  defectType: "open circuit",
  severity: "HIGH",
  confidence: "85%",
  taxonomy: "DEF-OPEN-PCB",
  disposition: "REJECT",
  inspectionMethods: ["AOI", "X-Ray", "IPC-A-600J visual"],
  defectReasoning: "Nova Pro identified a broken copper trace with surface irregularity. Severity classified HIGH due to functional circuit discontinuity — board cannot pass electrical test.",
  // Knowledge graph context
  graphContext: {
    relatedDefectsCount: 14,
    equipment: ["etching_bath_controller", "spray_etcher", "chemical_dosing_unit"],
    coOccurringDefects: ["pin_hole", "spurious_copper"],
    defectDensity: 1.42,
    historicalIncidentsCount: 7,
    historicalIncidents: [
      { defectType: "open", product: "group00041", severity: "high" },
      { defectType: "pin_hole", product: "group00041", severity: "low" },
      { defectType: "open", product: "group00038", severity: "high" },
    ],
    ipcReferences: [
      "IPC-A-600J § 2.2 — Conductor / Land Integrity",
      "IPC-A-600J § 2.4 — Conductor Spacing & Foreign Material",
      "IPC-7711 § 3.2 — Rework of Broken Traces",
    ],
  },
  // Root cause
  rootCause: "Etching process deviation — over-etching dissolved copper trace continuity while localised under-etching left residual bridges at pad edges. The co-occurrence pattern with pin_hole defects in 7 historical boards is consistent with etching bath chemistry imbalance (pH drift, temperature excursion, or depleted etchant concentration).",
  rootCauseReasoning: "Knowledge graph shows 7 prior boards in group00041 with combined open + pin_hole defects, all processed in the same 6-hour shift window. Equipment node etching_bath_controller has the highest betweenness centrality for this defect pair, indicating single-point process ownership. Probability of etching bath anomaly vs. other root causes: 85% vs 15%.",
  rootCauseConfidence: "85%",
  contributingFactors: [
    "Etching bath pH drift beyond ±0.2 tolerance during shift 2",
    "Chemical dosing unit calibration overdue by 11 days",
    "Co-occurrence of pin_hole on same board — shared etching bath root cause",
    "Defect density 1.42/block exceeds baseline of 0.6/block for this panel",
    "7 historical open-circuit incidents in group00041 over past 30 days",
  ],
  rootCauseActions: [
    { priority: "P1", action: "Inspect and calibrate etching bath controller — verify pH, temperature, etchant concentration against SPC limits", owner: "process_engineer", contextRef: "etching_bath_controller node" },
    { priority: "P2", action: "Perform titration of etching solution; replace bath if concentration < 120 g/L or pH outside 8.5–9.0", owner: "qa_team", contextRef: "chemical_dosing_unit node" },
    { priority: "P3", action: "Review and update spray-etcher nozzle maintenance schedule; check for blocked nozzles causing uneven etch rates", owner: "maintenance", contextRef: "spray_etcher node" },
    { priority: "P4", action: "Audit last 48 h of group00041 production; pull AOI logs for open + pin_hole pattern correlation", owner: "qa_team", contextRef: "7 historical incidents" },
  ],
  referencedContextIds: ["defect_open_00041", "defect_pin_hole_00041", "equip_etching_bath", "ipc_2_2", "ipc_2_4"],
  // Compliance  
  compliance: {
    standard: "IPC-A-600J",
    section: "2.2 / 2.4",
    classification: "Class 3 — High Reliability",
    disposition: "REJECT — non-conforming conductor",
  },
  complianceChecks: [
    { ref: "§ 2.2", title: "Conductor / Land Integrity", passed: false, detail: "Broken trace detected — fails minimum conductor width requirement" },
    { ref: "§ 2.3", title: "Annular Ring & Land Conditions", passed: true, detail: "Annular ring intact, within tolerance" },
    { ref: "§ 2.4", title: "Conductor Spacing & Foreign Material", passed: false, detail: "Residual copper bridge violates minimum spacing of 0.1 mm" },
    { ref: "§ 3.1", title: "Solder Mask Conditions", passed: true, detail: "Solder mask intact, no lifting" },
  ],
  agentActions: AGENT_ACTIONS.map(a => ({ tool: a.tool, detail: a.detail, priority: a.priority || null, assignee: a.assignee || null })),
  pipelineRuntimeMs: 13000,
  model: { vision: "us.amazon.nova-pro-v1:0", reasoning: "us.amazon.nova-lite-v1:0" },
};

// Maps C# CaseFile JSON (camelCase, from .NET ToJson()) → Dashboard UI shape
function mapCaseFile(d) {
  const actions = (d.rootCause?.actions || []).map((a, i) => ({
    priority: ["P1", "P2", "P3", "P4"][i] ?? `P${i + 1}`,
    action: a.action,
    owner: a.owner,
    contextRef: a.contextRef,
  }));
  return {
    caseId: d.caseId || "",
    image: d.imagePath ? d.imagePath.split(/[\\/]/).pop() : "unknown",
    productType: d.productType || "",
    status: d.status || "",
    humanReviewRequired: d.humanReviewRequired || false,
    vision: {
      caption: d.visionAnalysis?.caption || "",
      confidence: d.visionAnalysis?.confidence || 0,
      tags: d.visionAnalysis?.tags || [],
    },
    defectType: d.normalizedDefect?.defectType || d.visionAnalysis?.defectType || "",
    severity: (d.normalizedDefect?.severity || d.visionAnalysis?.defectSeverity || "").toUpperCase(),
    taxonomy: d.normalizedDefect?.taxonomyId || "",
    disposition: (d.compliance?.disposition || "PENDING").toUpperCase(),
    inspectionMethods: d.normalizedDefect?.inspectionMethods || [],
    defectReasoning: d.normalizedDefect?.reasoning || "",
    graphContext: {
      relatedDefectsCount: d.graphContext?.relatedDefectIds?.length || 0,
      equipment: d.graphContext?.equipmentIds || [],
      coOccurringDefects: d.graphContext?.coOccurringDefects || [],
      defectDensity: d.graphContext?.defectDensity || 0,
      historicalIncidentsCount: d.graphContext?.historicalIncidents?.length || 0,
      historicalIncidents: (d.graphContext?.historicalIncidents || []).map(h => ({
        defectType: h.defectType, product: h.product, severity: h.severity,
      })),
      ipcReferences: d.graphContext?.isoSnippets || [],
    },
    rootCause: d.rootCause?.probableCause || "",
    rootCauseConfidence: d.rootCause?.confidence ? `${Math.round(d.rootCause.confidence * 100)}%` : "",
    rootCauseReasoning: d.rootCause?.reasoning || "",
    contributingFactors: d.rootCause?.contributingFactors || [],
    rootCauseActions: actions,
    referencedContextIds: d.rootCause?.referencedContextIds || [],
    compliance: {
      standard: d.compliance?.applicableStandard || "IPC-A-600J",
      section: d.compliance?.section || "",
      classification: d.compliance?.classification || "",
      disposition: (d.compliance?.disposition || "").toUpperCase(),
    },
    complianceChecks: (d.compliance?.checklist || []).map(c => ({
      ref: c.sectionRef,
      title: c.requirement,
      passed: c.addressed,
      detail: c.evidence,
    })),
    agentActions: (d.agentActions || []).map(a => ({
      tool: a.toolName,
      detail: a.result,
      priority: null,
      assignee: null,
    })),
    trace: d.trace || [],
    pipelineRuntimeMs: null,
    model: { vision: "us.amazon.nova-pro-v1:0", reasoning: "us.amazon.nova-lite-v1:0" },
  };
}

// Empty string = relative URLs → works when C# serves the built app on any port.
// Vite dev mode proxies /api/* to localhost:5174 via vite.config.js.
const API = '';

export default function Dashboard() {
  const { activeStep, completedSteps, running, done, run } = useCyclingInspection();
  const [showActions, setShowActions] = useState(false);
  const [tick, setTick] = useState(0);
  const [selectedMenu, setSelectedMenu] = useState(1);
  const [sessionEnded, setSessionEnded] = useState(false);
  const [uploadedFileName, setUploadedFileName] = useState(null);
  const [uploadedImageUrl, setUploadedImageUrl] = useState(null);
  const fileInputRef = useRef(null);
  const [liveData, setLiveData] = useState(null);

  // API state
  const [apiImages, setApiImages] = useState([]);
  const [selectedApiImage, setSelectedApiImage] = useState('');
  const [apiRunning, setApiRunning] = useState(false);
  const [apiError, setApiError] = useState('');
  const [apiStatus, setApiStatus] = useState(null); // null = not contacted, true = online, false = offline

  // Check if API is reachable and load image list
  useEffect(() => {
    fetch(`${API}/api/images`)
      .then(r => r.ok ? r.json() : [])
      .then(imgs => {
        setApiImages(imgs);
        setApiStatus(true);
        if (imgs.length > 0) setSelectedApiImage(imgs[0].path);
      })
      .catch(() => setApiStatus(false));
  }, []);

  const fetchLiveData = () => {
    fetch('/data/latest_case.json')
      .then(r => r.ok ? r.json() : null)
      .then(json => { if (json && !json._empty) setLiveData(json); })
      .catch(() => {});
  };

  // Load last inspection result written by the .NET app
  useEffect(() => { fetchLiveData(); }, []);

  // Handle real pipeline run via API
  const handleRealRun = useCallback(async () => {
    if (apiRunning) return;
    setApiError('');
    setApiRunning(true);
    setRealInsights(null);   // clear stale insights from previous image
    setInsightsMeta(null);
    run(); // start animation simultaneously
    try {
      const res = await fetch(`${API}/api/inspect`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ imagePath: selectedApiImage || '' }),
      });
      const data = await res.json();
      if (!res.ok) { setApiError(data.error || 'Inspection failed'); return; }
      // Give file a moment to flush to disk
      setTimeout(() => fetchLiveData(), 500);
    } catch (e) {
      setApiError('API unreachable — is the .NET app running?');
    } finally {
      setApiRunning(false);
    }
  }, [apiRunning, run, selectedApiImage]);

  // ── Real data state for tabs 2, 3, 4 ──
  const [realInsights, setRealInsights] = useState(null);  // null = not loaded
  const [insightsLoading, setInsightsLoading] = useState(false);
  const [insightsError, setInsightsError] = useState('');

  const [realStats, setRealStats] = useState(null);
  const [statsLoading, setStatsLoading] = useState(false);

  const [realBatch, setRealBatch] = useState(null);
  const [batchLoading, setBatchLoading] = useState(false);
  const [batchError, setBatchError] = useState('');

  const [insightsMeta, setInsightsMeta] = useState(null); // { caseSpecific, imageName, defectType, source }
  const generateInsights = useCallback(async () => {
    setInsightsLoading(true); setInsightsError('');
    try {
      const opts = liveData
        ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(liveData) }
        : { method: 'GET' };
      const r = await fetch(`${API}/api/insights`, opts);
      const d = await r.json();
      if (d.ok && d.insights) {
        setRealInsights(d.insights);
        setInsightsMeta({ caseSpecific: d.caseSpecific, imageName: d.imageName, defectType: d.defectType, source: d.source });
      }
      else setInsightsError(d.error || 'Failed');
    } catch { setInsightsError('API unreachable - is the .NET app running?'); }
    finally { setInsightsLoading(false); }
  }, [liveData]);

  const [statsError, setStatsError] = useState('');
  const [statsLastLoaded, setStatsLastLoaded] = useState(null);
  const loadStats = useCallback(async () => {
    setStatsLoading(true); setStatsError('');
    try {
      const r = await fetch(`${API}/api/stats`);
      const d = await r.json();
      if (d.ok) { setRealStats(d); setStatsLastLoaded(new Date()); }
      else setStatsError(d.message || d.error || 'API returned not-ok');
    } catch (e) { setStatsError('API unreachable - is the .NET app running on port 5174?'); }
    finally { setStatsLoading(false); }
  }, []);

  const runBatch = useCallback(async (perCat = 1) => {
    setBatchLoading(true); setBatchError('');
    try {
      const r = await fetch(`${API}/api/batch`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ perCategory: perCat }),
      });
      const d = await r.json();
      if (d.ok) setRealBatch(d.results);
      else setBatchError(d.error || 'Batch failed');
    } catch { setBatchError('API unreachable — is the .NET app running?'); }
    finally { setBatchLoading(false); }
  }, []);

  // Auto-load stats whenever the user switches to tab 3
  useEffect(() => {
    if (selectedMenu === 3 && !realStats && !statsLoading) loadStats();
  }, [selectedMenu, realStats, statsLoading, loadStats]);

  const DISPLAY_DATA = liveData ? mapCaseFile(liveData) : CASE_DATA;

  const handleImageUpload = useCallback((e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploadedFileName(file.name);
    const reader = new FileReader();
    reader.onload = (ev) => setUploadedImageUrl(ev.target.result);
    reader.readAsDataURL(file);
    // Auto-match uploaded filename against available API images so the right path is sent
    const match = apiImages.find(img => img.label === file.name || img.path?.endsWith(file.name));
    if (match) setSelectedApiImage(match.path);
    else setSelectedApiImage('');
  }, [apiImages]);

  const downloadReport = useCallback(() => {
    const r = liveData ? mapCaseFile(liveData) : CASE_DATA;
    const sep = "═".repeat(63);
    const dash = "─".repeat(63);
    const lines = [
      sep,
      "  MCP INSPECTION REPORT",
      "  Amazon Nova Agentic Pipeline · AWS Bedrock · MCP v1",
      sep,
      `  Case ID:      ${r.caseId}`,
      `  Exported:     ${new Date().toISOString()}`,
      `  Image:        ${uploadedFileName || r.image}`,
      `  Product:      ${r.productType}`,
      `  Status:       ${r.status}`,
      `  Human Review: ${r.humanReviewRequired ? "YES" : "No"}`,
      `  Pipeline:     ${r.pipelineRuntimeMs / 1000}s total`,
      `  Vision Model: ${r.model.vision}`,
      `  Agent Model:  ${r.model.reasoning}`,
      "",
      "── VISION ANALYSIS ──",
      `  Caption:    ${r.vision.caption}`,
      `  Confidence: ${Math.round(r.vision.confidence * 100)}%`,
      `  Tags:       ${r.vision.tags.join(", ")}`,
      "",
      "── NORMALIZED DEFECT ──",
      `  Type:       ${r.defectType}`,
      `  Severity:   ${r.severity}`,
      `  Taxonomy:   ${r.taxonomy}`,
      `  Disposition:${r.disposition}`,
      `  Methods:    [${r.inspectionMethods.join(", ")}]`,
      `  Reasoning:  ${r.defectReasoning}`,
      "",
      "── KNOWLEDGE GRAPH CONTEXT ──",
      `  Related defects:      ${r.graphContext.relatedDefectsCount}`,
      `  AI-equipment:         ${r.graphContext.equipment.join(", ")}`,
      `  Co-occurring:         [${r.graphContext.coOccurringDefects.join(", ")}]`,
      `  Defect density:       ${r.graphContext.defectDensity} defects per 100×100px block`,
      `  Historical incidents: ${r.graphContext.historicalIncidentsCount}`,
      "  Historical incidents (similar defects on other boards):",
      ...r.graphContext.historicalIncidents.map(h => `    - ${h.defectType} on ${h.product} (${h.severity})`),
      "  IPC references:",
      ...r.graphContext.ipcReferences.map(ref => `    - ${ref}`),
      "",
      "── ROOT-CAUSE ANALYSIS ──",
      `  Probable cause: ${r.rootCause}`,
      `  Confidence:     ${r.rootCauseConfidence}`,
      `  Reasoning:      ${r.rootCauseReasoning}`,
      `  Contributing factors:`,
      ...r.contributingFactors.map(f => `    - ${f}`),
      `  Actions:`,
      ...r.rootCauseActions.map(a =>
        `    [${a.priority}] ${a.action} (Owner: ${a.owner})${a.contextRef ? ` ← ${a.contextRef}` : ""}`
      ),
      `  Referenced IDs: ${r.referencedContextIds.join(", ")}`,
      "",
      "── IPC COMPLIANCE ──",
      `  Standard:     ${r.compliance.standard} § ${r.compliance.section}`,
      `  Class:        ${r.compliance.classification}`,
      `  Disposition:  ${r.compliance.disposition}`,
      ...r.complianceChecks.map(c =>
        `  ${c.passed ? "PASS" : "FAIL"}  ${c.ref}  ${c.title}${c.detail ? ` — ${c.detail}` : ""}`
      ),
      "",
      "── AGENT ACTIONS TAKEN ──",
      ...r.agentActions.map((a, i) => [
        `  [${i + 1}] ${a.tool}${a.priority ? ` (${a.priority})` : ""}`,
        `      ${a.detail}`,
        a.assignee ? `      → Assigned to: ${a.assignee}` : "",
      ].filter(Boolean).join("\n")),
      "",
      dash,
      `  github.com/Ashahet1/AmazonNOVAHackathon`,
      `  Amazon Nova AI Hackathon · March 2026`,
    ];
    const blob = new Blob([lines.join("\n")], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `pcb-report-${r.caseId}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  }, [uploadedFileName, liveData]);

  useEffect(() => {
    const t = setInterval(() => setTick(p => p + 1), 2000);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    if (done) setTimeout(() => setShowActions(true), 400);
    else setShowActions(false);
  }, [done]);

  const css = `
    ${FONTS}
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { background: #080c10; }
    @keyframes pulse { 0%,100% { opacity:1; box-shadow:0 0 0 0 rgba(0,212,255,0.4) } 50% { opacity:0.8; box-shadow:0 0 0 6px rgba(0,212,255,0) } }
    @keyframes bounce { 0%,100% { transform:translateY(0) } 50% { transform:translateY(-4px) } }
    @keyframes scanline { 0% { top:-10% } 100% { top:110% } }
    @keyframes blink { 0%,100% { opacity:1 } 50% { opacity:0.3 } }
    ::-webkit-scrollbar { width: 4px; } ::-webkit-scrollbar-track { background: transparent; } ::-webkit-scrollbar-thumb { background: #ffffff15; border-radius: 2px; }
  `;

  return (
    <div style={{ minHeight: "100vh", background: "#080c10", fontFamily: "'Barlow', sans-serif", color: "#fff", position: "relative", overflow: "hidden" }}>
      <style>{css}</style>

      {/* Subtle grid background */}
      <div style={{
        position: "fixed", inset: 0, pointerEvents: "none", zIndex: 0,
        backgroundImage: "linear-gradient(rgba(0,212,255,0.03) 1px, transparent 1px), linear-gradient(90deg, rgba(0,212,255,0.03) 1px, transparent 1px)",
        backgroundSize: "40px 40px",
      }} />

      {/* Scanline effect */}
      <div style={{
        position: "fixed", left: 0, right: 0, height: "3%", background: "linear-gradient(180deg, transparent, rgba(0,212,255,0.03), transparent)",
        pointerEvents: "none", zIndex: 1, animation: "scanline 8s linear infinite",
      }} />

      <div style={{ position: "relative", zIndex: 2, maxWidth: 1280, margin: "0 auto", padding: "20px 24px" }}>

        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 24, paddingBottom: 16, borderBottom: "1px solid #ffffff10" }}>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
              <div style={{ width: 8, height: 8, borderRadius: "50%", background: "#10b981", boxShadow: "0 0 8px #10b981", animation: "blink 2s ease-in-out infinite" }} />
              <span style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 11, color: "#ffffff40", letterSpacing: "0.15em", textTransform: "uppercase" }}>
                Manufacturing Execution System — PCB Quality Control
              </span>
            </div>
            <h1 style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 28, fontWeight: 800, letterSpacing: "0.02em", marginTop: 4, color: "#fff" }}>
              AGENTIC DEFECT INSPECTOR
            </h1>
            <div style={{ fontSize: 11, color: "#ffffff40", fontFamily: "'IBM Plex Mono', monospace", marginTop: 2 }}>
              Amazon Nova Pro + Nova Lite · AWS Bedrock Converse API · 7-step MCP pipeline
            </div>
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <div style={{ textAlign: "right" }}>
              <div style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff30" }}>DeepPCB Dataset</div>
              <div style={{ fontSize: 11, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff60" }}>Peking University · 6 defect classes</div>
            </div>
            <div style={{
              padding: "6px 14px", background: "#10b98115", border: "1px solid #10b98140",
              borderRadius: 6, fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#10b981",
            }}>LIVE</div>
          </div>
        </div>

        {/* ── Menu Bar ── */}
        <div style={{ display: "flex", gap: 8, marginBottom: 20, flexWrap: "wrap" }}>
          {MENU_OPTIONS.map(opt => {
            const isActive = selectedMenu === opt.id;
            const isExit = opt.id === 6;
            return (
              <button
                key={opt.id}
                onClick={() => {
                  if (isExit) { setSessionEnded(true); setSelectedMenu(6); }
                  else { setSessionEnded(false); setSelectedMenu(opt.id); }
                }}
                style={{
                  display: "flex", alignItems: "center", gap: 8, padding: "8px 14px",
                  borderRadius: 7, border: `1.5px solid ${isActive ? opt.accent : "#ffffff15"}`,
                  background: isActive ? `${opt.accent}18` : "#ffffff05",
                  cursor: "pointer", transition: "all 0.2s ease", flex: "0 0 auto",
                }}
              >
                <span style={{ fontSize: 13 }}>{opt.icon}</span>
                <div style={{ textAlign: "left" }}>
                  <div style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", fontWeight: 600,
                    color: isActive ? opt.accent : "#ffffff60", letterSpacing: "0.03em", whiteSpace: "nowrap" }}>
                    {opt.id}. {opt.label}
                  </div>
                  <div style={{ fontSize: 8, fontFamily: "'Barlow', sans-serif", color: "#ffffff30" }}>{opt.sub}</div>
                </div>
                {isActive && (
                  <div style={{ width: 5, height: 5, borderRadius: "50%", background: opt.accent, marginLeft: 2,
                    boxShadow: `0 0 6px ${opt.accent}` }} />
                )}
              </button>
            );
          })}
        </div>

        {/* Stats Row */}
        <div style={{ display: "flex", gap: 12, marginBottom: 20 }}>
          <StatCard label="PCB Images Analyzed" value="50" sub="DeepPCB training set" accent="#00d4ff" />
          <StatCard label="Defects Catalogued" value="327" sub="6 canonical types" accent="#f59e0b" />
          <StatCard label="Graph Nodes" value="388" sub="1,308 relationships" accent="#8b5cf6" />
          <StatCard label="Pipeline Runtime" value="~13s" sub="full 7-step + agent loop" accent="#10b981" />
          <StatCard label="Actions Taken" value="5" sub="per inspection, autonomous" accent="#ef4444" />
        </div>

        {/* ── Option 6: Exit ── */}
        {selectedMenu === 6 && (
          <div style={{ display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", minHeight: 320, gap: 16 }}>
            <div style={{ fontSize: 48 }}>👋</div>
            <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 32, fontWeight: 800, color: "#ffffff60" }}>SESSION ENDED</div>
            <div style={{ fontSize: 12, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff30" }}>5 actions taken · 50 images analysed · knowledge graph saved</div>
            <button onClick={() => { setSessionEnded(false); setSelectedMenu(1); }}
              style={{ marginTop: 12, padding: "8px 24px", borderRadius: 6, border: "1px solid #00d4ff40",
                background: "#00d4ff15", color: "#00d4ff", fontSize: 10, fontFamily: "'IBM Plex Mono', monospace",
                cursor: "pointer", fontWeight: 600 }}>↩ RETURN TO MENU</button>
          </div>
        )}

        {/* ── Option 2: Batch Inspect ── */}
        {selectedMenu === 2 && (
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>

            {/* Explanation card */}
            <div style={{ background: "#0d1117", border: "1px solid #00d4ff18", borderRadius: 10, padding: 16 }}>
              <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 15, fontWeight: 700, color: "#00d4ff", textTransform: "uppercase", letterSpacing: "0.12em", marginBottom: 8 }}>
                What is Batch Inspection?
              </div>
              <p style={{ fontSize: 11, color: "#ffffff70", lineHeight: 1.8, fontFamily: "'Barlow', sans-serif", margin: 0 }}>
                Unlike <strong style={{ color: "#ffffffaa" }}>Step 1</strong> which inspects a <strong style={{ color: "#ffffffaa" }}>single image you choose</strong>, Batch Inspection automatically
                runs the full AI pipeline on <strong style={{ color: "#ffffffaa" }}>1 test image from each of the 11 PCB groups</strong> in the dataset
                (group00041, group12000, group12100, group12300, group13000, group20085, group44000, group50600, group77000, group90100, group92000).
                Each group contains paired images — a <strong style={{ color: "#ffffffaa" }}>reference template</strong> and a <strong style={{ color: "#ffffffaa" }}>test image</strong> — and only the test images are inspected.
                <strong style={{ color: "#ffffffaa" }}> 11 inspections total, run one after another.</strong> Results are collected and shown in the table below.
              </p>
              <div style={{ marginTop: 12, display: "flex", gap: 12, alignItems: "center", flexWrap: "wrap" }}>
                {[["11", "PCB groups", "#00d4ff"], ["1", "test image per group", "#8b5cf6"], ["11", "total inspections", "#10b981"]].map(([v, l, c]) => (
                  <div key={l} style={{ padding: "6px 14px", background: `${c}10`, border: `1px solid ${c}25`, borderRadius: 6, display: "flex", alignItems: "baseline", gap: 6 }}>
                    <span style={{ fontSize: 18, fontFamily: "'IBM Plex Mono', monospace", fontWeight: 700, color: c }}>{v}</span>
                    <span style={{ fontSize: 9, color: `${c}90`, fontFamily: "'Barlow', sans-serif", textTransform: "uppercase", letterSpacing: "0.08em" }}>{l}</span>
                  </div>
                ))}
                <button onClick={() => runBatch(1)} disabled={batchLoading}
                  style={{ marginLeft: "auto", padding: "10px 24px", borderRadius: 7, border: "1px solid #00d4ff50",
                    background: batchLoading ? "#00d4ff08" : "#00d4ff20", color: batchLoading ? "#00d4ff60" : "#00d4ff",
                    fontSize: 11, fontFamily: "'IBM Plex Mono', monospace", cursor: batchLoading ? "not-allowed" : "pointer",
                    fontWeight: 700, letterSpacing: "0.06em" }}>
                  {batchLoading ? "⏳  RUNNING INSPECTION…" : "🔁  RUN BATCH INSPECTION"}
                </button>
              </div>
            </div>

            <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 14 }}>
                <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, color: "#ffffff80", textTransform: "uppercase", letterSpacing: "0.1em" }}>
                  {realBatch ? `Inspection Results — ${realBatch.length} images processed` : "Results will appear here after you run the batch"}
                </div>
                {realBatch && (
                  <div style={{ marginLeft: "auto", padding: "2px 8px", borderRadius: 3, background: "#10b98115", border: "1px solid #10b98130", fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#10b981" }}>
                    LIVE · {realBatch.length} CASES
                  </div>
                )}
              </div>
              {batchError && (
                <div style={{ marginBottom: 10, padding: "8px 14px", background: "#ef444410", border: "1px solid #ef444430", borderRadius: 6, fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#ef4444" }}>{batchError}</div>
              )}
              <div style={{ overflowX: "auto" }}>
                {!realBatch && !batchLoading && (
                  <div style={{ padding: "28px 0", textAlign: "center", color: "#ffffff20", fontSize: 11, fontFamily: "'IBM Plex Mono', monospace", letterSpacing: "0.06em" }}>
                    Press <span style={{ color: "#00d4ff40" }}>RUN BATCH INSPECTION</span> above to start — results will appear here
                  </div>
                )}
                {batchLoading && (
                  <div style={{ padding: "28px 0", textAlign: "center", color: "#00d4ff60", fontSize: 11, fontFamily: "'IBM Plex Mono', monospace", letterSpacing: "0.06em" }}>
                    ⏳  Inspecting images… this may take 1–3 minutes
                  </div>
                )}
                {realBatch && (
                  <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 10 }}>
                    <thead>
                      <tr style={{ borderBottom: "1px solid #ffffff15" }}>
                        {["#", "Defect Class", "Image File", "AI Verdict", "Defect Found?", "Confidence", "Time"].map(h => (
                          <th key={h} style={{ padding: "6px 12px", textAlign: "left", fontFamily: "'IBM Plex Mono', monospace", fontSize: 9, color: "#ffffff40", fontWeight: 600, letterSpacing: "0.06em", textTransform: "uppercase" }}>{h}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {realBatch.map((r, i) => (
                        <tr key={r.caseId || i} style={{ borderBottom: "1px solid #ffffff08", background: i % 2 === 0 ? "#ffffff03" : "transparent" }}>
                          <td style={{ padding: "8px 12px", color: "#ffffff30", fontFamily: "'IBM Plex Mono', monospace" }}>{i + 1}</td>
                          <td style={{ padding: "8px 12px", fontFamily: "'IBM Plex Mono', monospace", color: "#00d4ff90" }}>{r.category}</td>
                          <td style={{ padding: "8px 12px", color: "#ffffff50", fontFamily: "'IBM Plex Mono', monospace", fontSize: 9 }}>{r.imagePath?.split(/[/\\]/).pop() ?? "—"}</td>
                          <td style={{ padding: "8px 12px", color: r.status === 'completed' ? "#10b981" : r.status === 'review' ? "#f59e0b" : "#ef4444", fontFamily: "'IBM Plex Mono', monospace", fontWeight: 600 }}>{r.status?.toUpperCase() ?? "—"}</td>
                          <td style={{ padding: "8px 12px" }}>
                            <span style={{ padding: "2px 8px", borderRadius: 3, background: r.defectFound ? "#ef444415" : "#10b98115", border: `1px solid ${r.defectFound ? "#ef444430" : "#10b98130"}`, color: r.defectFound ? "#ef4444" : "#10b981", fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", fontWeight: 700 }}>
                              {r.defectFound ? "DEFECT" : "PASS"}
                            </span>
                          </td>
                          <td style={{ padding: "8px 12px", fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff70" }}>{r.confidence != null ? `${(r.confidence * 100).toFixed(0)}%` : "—"}</td>
                          <td style={{ padding: "8px 12px", fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff40" }}>{r.durationMs != null ? `${(r.durationMs / 1000).toFixed(1)}s` : "—"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
              {realBatch && (
                <div style={{ marginTop: 14, display: "flex", gap: 12, flexWrap: "wrap" }}>
                  {(() => {
                    const passed = realBatch.filter(r => !r.defectFound).length;
                    const failed = realBatch.filter(r => r.defectFound).length;
                    const pct = realBatch.length > 0 ? Math.round(passed / realBatch.length * 100) : 0;
                    return [
                      { label: "Total inspected", value: String(realBatch.length), c: "#00d4ff" },
                      { label: "Passed (no defect)", value: `${passed}`, c: "#10b981" },
                      { label: "Defects detected", value: `${failed}`, c: "#ef4444" },
                      { label: "Pass rate", value: `${pct}%`, c: pct >= 80 ? "#10b981" : "#f59e0b" },
                    ];
                  })().map(s => (
                    <div key={s.label} style={{ padding: "8px 16px", background: `${s.c}10`, border: `1px solid ${s.c}25`, borderRadius: 6 }}>
                      <div style={{ fontSize: 8, color: `${s.c}70`, fontFamily: "'Barlow', sans-serif", textTransform: "uppercase", letterSpacing: "0.08em", marginBottom: 2 }}>{s.label}</div>
                      <div style={{ fontSize: 16, fontFamily: "'IBM Plex Mono', monospace", color: s.c, fontWeight: 700 }}>{s.value}</div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}

        {/* ── Option 3: Defect Statistics ── */}
        {selectedMenu === 3 && (() => {
          const S = realStats;
          const maxDef = S ? Math.max(...(S.byCat || []).map(r => r.defects), 1) : 89;
          const maxEq  = S ? Math.max(...(S.equipmentHubs || []).map(e => e.edges), 1) : 261;
          const maxProd = S ? Math.max(...(S.byProduct || []).map(p => p.count), 1) : 327;
          const sevTotal = S ? (S.severityHigh + S.severityMedium + S.severityLow) || 1 : 327;

          const downloadStats = () => {
            const d = S || {};
            const sep = "═".repeat(70);
            const lines = [
              sep,
              "  📊 DEFECT STATISTICS REPORT",
              `  Generated: ${new Date().toISOString()}`,
              sep,
              "",
              "📊 KEY METRICS",
              "─".repeat(70),
              `  Images Analyzed:        ${d.totalImages ?? 50}`,
              `  Total Defects:          ${d.totalDefects ?? 327}`,
              `  Product Categories:     ${d.totalProducts ?? 1}`,
              `  Equipment Types:        ${d.totalEquipment ?? 7}`,
              `  Avg Defects/Product:    ${d.avgDefectsPerProduct ?? 327}`,
              "",
              "📊 DEFECT TYPE DISTRIBUTION",
              "─".repeat(70),
              ...(d.byCat || STATS_TABLE).map(r => `  ${(r.category ?? r.category).padEnd(20)} ${r.defects ?? r.defects}`),
              "",
              "🔴 SEVERITY BREAKDOWN",
              "─".repeat(70),
              `  High Severity:    ${d.severityHigh ?? "—"}`,
              `  Medium Severity:  ${d.severityMedium ?? "—"}`,
              `  Low Severity:     ${d.severityLow ?? "—"}`,
              "",
              "🔧 EQUIPMENT USAGE",
              "─".repeat(70),
              ...(d.equipmentHubs || []).map(e => `  ${(e.name || "").padEnd(35)} ${e.edges} connections`),
              "",
              "📦 DEFECTS BY PRODUCT",
              "─".repeat(70),
              ...(d.byProduct || [{ product: "pcb", count: 327 }]).map(p => `  ${p.product.padEnd(20)} ${p.count}`),
              sep,
            ];
            const blob = new Blob([lines.join("\n")], { type: "text/plain" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url; a.download = `defect_stats_${Date.now()}.txt`; a.click();
            URL.revokeObjectURL(url);
          };

          return (
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            {/* Header row */}
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <button onClick={loadStats} disabled={statsLoading}
                style={{ padding: "7px 16px", borderRadius: 6, border: "1px solid #8b5cf650",
                  background: statsLoading ? "#8b5cf608" : "#8b5cf620", color: "#8b5cf6",
                  fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", cursor: statsLoading ? "not-allowed" : "pointer", fontWeight: 700 }}>
                {statsLoading ? "LOADING..." : "LOAD REAL STATS"}
              </button>
              {statsLastLoaded && !statsLoading && (
                <span style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#10b981", border: "1px solid #10b98140", borderRadius: 4, padding: "3px 8px" }}>
                  REFRESHED {statsLastLoaded.toLocaleTimeString()}
                </span>
              )}
              <button onClick={downloadStats}
                style={{ padding: "7px 16px", borderRadius: 6, border: "1px solid #10b98150",
                  background: "#10b98115", color: "#10b981",
                  fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", cursor: "pointer", fontWeight: 700 }}>
                ⬇ DOWNLOAD TXT REPORT
              </button>
              {S && <div style={{ marginLeft: "auto", fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff30" }}>LIVE DATA · {S.totalDefects} defects across {S.totalImages} images</div>}
            </div>

            {/* Error banner */}
            {statsError && <div style={{ padding: "8px 14px", background: "#ef444415", border: "1px solid #ef444440", borderRadius: 7, fontSize: 10, color: "#ef4444", fontFamily: "'IBM Plex Mono', monospace" }}>⚠ {statsError}</div>}

            {/* TOP INSIGHTS — exact strings from graph.GenerateInsights() via API */}
            {S && (S.insights || []).length > 0 && (
              <div style={{ background: "#0a0f14", border: "1px solid #00d4ff18", borderRadius: 10, padding: "12px 16px", fontFamily: "'IBM Plex Mono', monospace" }}>
                <div style={{ fontSize: 11, fontWeight: 700, color: "#00d4ff70", letterSpacing: "0.1em", textTransform: "uppercase", marginBottom: 10 }}>💡 TOP INSIGHTS</div>
                {(S.insights || []).map((line, i) => (
                  <div key={i} style={{ display: "flex", gap: 8, marginBottom: 6, fontSize: 10, color: "#ffffff65", lineHeight: 1.6 }}>
                    <span style={{ color: "#10b981", flexShrink: 0 }}>✓</span>
                    <span>{line}</span>
                  </div>
                ))}
              </div>
            )}

            {/* Key Metrics strip */}
            <div style={{ display: "grid", gridTemplateColumns: "repeat(6, 1fr)", gap: 8 }}>
              {[
                ["Images Analyzed",       S?.totalImages              ?? 50,   "#00d4ff"],
                ["Total Defects",         S?.totalDefects             ?? 327,  "#f59e0b"],
                ["Product Categories",    S?.totalProducts            ?? 1,    "#ffffff70"],
                ["Equipment Types",       S?.totalEquipment           ?? 7,    "#f59e0b"],
                ["Avg Defects/Prod",      S?.avgDefectsPerProduct     ?? 327,  "#8b5cf6"],
                ["Cross-Product Patterns",S?.crossProductPatterns     ?? 0,    "#10b981"],
              ].map(([l,v,c]) => (
                <div key={l} style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 8, padding: "10px 14px" }}>
                  <div style={{ fontSize: 8, color: "#ffffff35", fontFamily: "'Barlow', sans-serif", textTransform: "uppercase", letterSpacing: "0.08em", marginBottom: 4 }}>{l}</div>
                  <div style={{ fontSize: 20, fontFamily: "'IBM Plex Mono', monospace", fontWeight: 700, color: c }}>{v}</div>
                </div>
              ))}
            </div>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              {/* Left col: Defect distribution + severity */}
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>

                {/* Defect type distribution */}
                <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
                  <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, color: "#ffffff80", textTransform: "uppercase", letterSpacing: "0.1em", marginBottom: 14 }}>📊 Defect Type Distribution</div>
                  {(S?.byCat || STATS_TABLE).filter(r => (r.defects ?? 0) > 0 || !S).map((r, i) => {
                    const count = r.defects ?? 0;
                    const pct = Math.round((count / maxDef) * 100);
                    const COLORS = ["#ef4444","#f59e0b","#10b981","#00d4ff","#8b5cf6","#ec4899"];
                    const color = COLORS[i % COLORS.length];
                    return (
                      <div key={r.category} style={{ marginBottom: 10 }}>
                        <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 3 }}>
                          <span style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff80" }}>{r.category}</span>
                          <span style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: color, fontWeight: 700 }}>{count}</span>
                        </div>
                        <div style={{ height: 6, background: "#ffffff08", borderRadius: 3, overflow: "hidden" }}>
                          <div style={{ height: "100%", width: `${pct}%`, background: color, borderRadius: 3, transition: "width 0.6s ease" }} />
                        </div>
                      </div>
                    );
                  })}
                </div>

                {/* Severity breakdown */}
                <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
                  <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, color: "#ffffff80", textTransform: "uppercase", letterSpacing: "0.1em", marginBottom: 14 }}>🔴 Severity Breakdown</div>
                  {[
                    ["High Severity",   S?.severityHigh   ?? 63,  "#ef4444"],
                    ["Medium Severity", S?.severityMedium ?? 198, "#f59e0b"],
                    ["Low Severity",    S?.severityLow    ?? 66,  "#10b981"],
                  ].map(([label, count, color]) => {
                    const pct = Math.round((Number(count) / sevTotal) * 100);
                    return (
                      <div key={label} style={{ marginBottom: 10 }}>
                        <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 3 }}>
                          <span style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff80" }}>{label}</span>
                          <span style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color, fontWeight: 700 }}>{count} <span style={{ color: "#ffffff40", fontWeight: 400 }}>({pct}%)</span></span>
                        </div>
                        <div style={{ height: 8, background: "#ffffff08", borderRadius: 4, overflow: "hidden" }}>
                          <div style={{ height: "100%", width: `${pct}%`, background: color, borderRadius: 4, transition: "width 0.6s ease" }} />
                        </div>
                      </div>
                    );
                  })}
                  <div style={{ marginTop: 8, fontSize: 9, color: "#ffffff30", fontFamily: "'IBM Plex Mono', monospace" }}>Total: {S?.totalDefects ?? 327}</div>
                </div>
              </div>

              {/* Right col: Equipment + by product + graph summary */}
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>

                {/* Equipment usage */}
                <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
                  <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, color: "#ffffff80", textTransform: "uppercase", letterSpacing: "0.1em", marginBottom: 14 }}>🔧 Equipment Usage Analysis</div>
                  {(S?.equipmentHubs?.length > 0 ? S.equipmentHubs : [
                    { name: "Automated Optical Inspection (AOI)", edges: 261 },
                    { name: "High-resolution microscope",         edges: 195 },
                    { name: "Flying probe tester",                edges: 69  },
                    { name: "X-ray inspection system",            edges: 66  },
                    { name: "In-Circuit Test (ICT)",              edges: 63  },
                  ]).map((eq, i) => {
                    const pct = Math.round((eq.edges / maxEq) * 100);
                    const truncated = eq.name.length > 32 ? eq.name.slice(0, 30) + "…" : eq.name;
                    return (
                      <div key={eq.name} style={{ marginBottom: 9 }}>
                        <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 3 }}>
                          <span style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff70" }}>{truncated}</span>
                          <span style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#8b5cf6", fontWeight: 700 }}>{eq.edges}</span>
                        </div>
                        <div style={{ height: 5, background: "#ffffff08", borderRadius: 3, overflow: "hidden" }}>
                          <div style={{ height: "100%", width: `${pct}%`, background: "#8b5cf6", borderRadius: 3, transition: "width 0.6s ease" }} />
                        </div>
                      </div>
                    );
                  })}
                </div>

                {/* Defects by product */}
                <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
                  <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, color: "#ffffff80", textTransform: "uppercase", letterSpacing: "0.1em", marginBottom: 12 }}>📦 Defects by Product</div>
                  {(S?.byProduct?.length > 0 ? S.byProduct : [{ product: "pcb", count: 327 }]).map(p => {
                    const pct = Math.round((p.count / maxProd) * 100);
                    return (
                      <div key={p.product} style={{ marginBottom: 9 }}>
                        <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 3 }}>
                          <span style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#00d4ff90" }}>{p.product}</span>
                          <span style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#f59e0b", fontWeight: 700 }}>{p.count}</span>
                        </div>
                        <div style={{ height: 6, background: "#ffffff08", borderRadius: 3, overflow: "hidden" }}>
                          <div style={{ height: "100%", width: `${pct}%`, background: "#f59e0b", borderRadius: 3 }} />
                        </div>
                      </div>
                    );
                  })}
                </div>

                {/* Knowledge graph summary */}
                <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
                  <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, color: "#ffffff80", textTransform: "uppercase", letterSpacing: "0.1em", marginBottom: 10 }}>Knowledge Graph Summary</div>
                  {[
                    ["Nodes",            String(S?.totalNodes     ?? 388),   "#8b5cf6"],
                    ["Relationships",    String(S?.totalEdges     ?? 1308),  "#8b5cf6"],
                    ["Images inspected", String(S?.totalImages    ?? 50),    "#00d4ff"],
                    ["Defect records",   String(S?.totalDefects   ?? 327),   "#f59e0b"],
                    ["Equipment types",  String(S?.totalEquipment ?? 7),     "#f59e0b"],
                    ["Standards",        String(S?.totalStandards ?? 9),     "#10b981"],
                  ].map(([l,v,c]) => (
                    <div key={l} style={{ display: "flex", justifyContent: "space-between", padding: "4px 0", borderBottom: "1px solid #ffffff06" }}>
                      <span style={{ fontSize: 10, color: "#ffffff40" }}>{l}</span>
                      <span style={{ fontSize: 11, fontFamily: "'IBM Plex Mono', monospace", fontWeight: 600, color: c }}>{v}</span>
                    </div>
                  ))}
                  <div style={{ marginTop: 10, padding: "6px 10px", background: "#f59e0b06", border: "1px solid #f59e0b15", borderRadius: 5, fontSize: 9, color: "#f59e0b60", fontFamily: "'IBM Plex Mono', monospace" }}>
                    DeepPCB codes: open=0 · short=1 · mousebite=2 · spur=3 · pin_hole=4 · spurious_copper=5
                  </div>
                </div>
              </div>
            </div>
          </div>
          );
        })()}


        {/* ── Option 4: AI Insights ── */}
        {selectedMenu === 4 && (
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
              <div style={{ flex: 1, padding: "8px 14px", background: "#10b98108", border: "1px solid #10b98120", borderRadius: 8, fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#10b98180" }}>
                Amazon Nova Lite
                {insightsMeta
                  ? <span style={{ marginLeft: 8 }}>
                      {insightsMeta.caseSpecific
                        ? <><span style={{ color: "#00d4ff80" }}>CASE-SPECIFIC</span> · {insightsMeta.imageName} · defect: <span style={{ color: "#ef4444a0" }}>{insightsMeta.defectType}</span></>
                        : <span style={{ color: "#ffffff30" }}>GENERAL GRAPH INSIGHTS</span>
                      }
                      <span style={{ marginLeft: 8, color: insightsMeta.source === "nova" ? "#10b98180" : "#ffffff30" }}>
                        · {insightsMeta.source === "nova" ? "LIVE LLM" : "static fallback"}
                      </span>
                    </span>
                  : <span style={{ marginLeft: 8, color: "#ffffff30" }}>
                      {liveData ? `ready to analyse: ${(liveData.imagePath || '').split(/[\\/]/).pop()}` : "run inspection first for image-specific insights"}
                    </span>
                }
              </div>
              <button onClick={generateInsights} disabled={insightsLoading}
                style={{ padding: "8px 16px", borderRadius: 6,
                  border: `1px solid ${liveData ? "#00d4ff50" : "#10b98150"}`,
                  background: insightsLoading ? "#ffffff08" : liveData ? "#00d4ff15" : "#10b98115",
                  color: insightsLoading ? "#ffffff40" : liveData ? "#00d4ff" : "#10b981",
                  fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", cursor: insightsLoading ? "not-allowed" : "pointer", fontWeight: 700, whiteSpace: "nowrap" }}>
                {insightsLoading ? "CALLING LLM..." : liveData ? "ANALYSE THIS IMAGE" : "GENERATE INSIGHTS"}
              </button>
            </div>
            {!liveData && !realInsights && (
              <div style={{ padding: "10px 14px", background: "#f59e0b06", border: "1px solid #f59e0b20", borderRadius: 7, fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#f59e0b70" }}>
                Tip: Run an inspection on Tab 1 first — insights will be specific to that image's defect type, root cause, and IPC compliance failures.
              </div>
            )}
            {insightsError && (
              <div style={{ padding: "8px 14px", background: "#ef444410", border: "1px solid #ef444430", borderRadius: 6, fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#ef4444" }}>{insightsError}</div>
            )}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              {(realInsights || AI_INSIGHTS).map((ins, idx) => {
                const ACCENTS = ["#10b981", "#8b5cf6", "#00d4ff", "#f59e0b"];
                const accent = ins.accent || ACCENTS[idx % ACCENTS.length];
                return (
                  <div key={ins.num || idx} style={{ background: "#0d1117", border: `1px solid ${accent}20`, borderRadius: 10, padding: 16 }}>
                    <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 10 }}>
                      <span style={{ padding: "2px 8px", borderRadius: 3, background: `${accent}20`, border: `1px solid ${accent}40`,
                        fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: accent, fontWeight: 700 }}>INSIGHT #{ins.num || idx + 1}</span>
                      <span style={{ fontSize: 11, fontFamily: "'Barlow Condensed', sans-serif", fontWeight: 700, color: "#ffffffcc", letterSpacing: "0.04em" }}>{ins.title}</span>
                    </div>
                    <p style={{ fontSize: 11, color: "#ffffff70", lineHeight: 1.7, fontFamily: "'Barlow', sans-serif", marginBottom: 10 }}>{ins.body}</p>
                    <div style={{ padding: "8px 10px", background: `${accent}08`, border: `1px solid ${accent}20`, borderRadius: 5 }}>
                      <div style={{ fontSize: 8, fontFamily: "'Barlow', sans-serif", color: `${accent}80`, textTransform: "uppercase", letterSpacing: "0.08em", marginBottom: 3 }}>Recommended Action</div>
                      <div style={{ fontSize: 10, color: `${accent}cc`, fontFamily: "'Barlow', sans-serif" }}>{ins.action}</div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        )}

        {/* ── Option 5: View / Export Case ── */}
        {selectedMenu === 5 && (
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
                <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, color: "#ffffff80", textTransform: "uppercase", letterSpacing: "0.1em" }}>Case Summary</div>
                <button onClick={downloadReport} style={{ padding: "5px 12px", borderRadius: 5, border: "1px solid #10b98150",
                  background: "#10b98115", color: "#10b981", fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", cursor: "pointer", fontWeight: 600 }}>⬇ DOWNLOAD TXT</button>
              </div>
              {[["Case ID",DISPLAY_DATA.caseId,"#00d4ff"],["Image",DISPLAY_DATA.image || "unknown","#ffffff80"],["Product",DISPLAY_DATA.productType,"#ffffff60"],["Status",DISPLAY_DATA.status,"#10b981"],["Defect",DISPLAY_DATA.defectType,"#ef4444"],["Severity",DISPLAY_DATA.severity,"#ef4444"],["Taxonomy",DISPLAY_DATA.taxonomy,"#8b5cf6"],["Disposition",DISPLAY_DATA.disposition,"#ef4444"],["Confidence",DISPLAY_DATA.rootCauseConfidence,"#10b981"],["Runtime",DISPLAY_DATA.pipelineRuntimeMs ? `${DISPLAY_DATA.pipelineRuntimeMs/1000}s` : "~13s","#ffffff60"],["Human Review",DISPLAY_DATA.humanReviewRequired?"YES":"No","#ffffff40"]].map(([l,v,c]) => (
                <div key={l} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "5px 0", borderBottom: "1px solid #ffffff06" }}>
                  <span style={{ fontSize: 9, color: "#ffffff35", fontFamily: "'Barlow', sans-serif", textTransform: "uppercase", letterSpacing: "0.07em" }}>{l}</span>
                  <span style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: c, fontWeight: 600 }}>{v}</span>
                </div>
              ))}
              <div style={{ marginTop: 12 }}>
                <div style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff30", marginBottom: 6 }}>POLICY VIOLATIONS</div>
                <div style={{ padding: "6px 10px", background: "#ef444410", border: "1px solid #ef444430", borderRadius: 5, fontSize: 10, color: "#ef4444", fontFamily: "'Barlow', sans-serif" }}>
                  [QUARANTINE] high severity defect detected on active production batch
                </div>
              </div>
            </div>
            <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16, overflow: "hidden" }}>
              <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, color: "#ffffff80", textTransform: "uppercase", letterSpacing: "0.1em", marginBottom: 12 }}>Full Trace Log</div>
              <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>
                {(DISPLAY_DATA.trace?.length > 0 ? DISPLAY_DATA.trace : TRACE_LOG).map((t, i) => {
                  const tool = t.tool || t.tool;
                  const outcome = t.outcome || "";
                  const detail = t.detail || "";
                  const time = t.timestamp ? new Date(t.timestamp).toLocaleTimeString() : t.time || "";
                  const isErr = outcome.includes("violation") || outcome.includes("fail");
                  const isWarn = outcome.includes("warn") || outcome.includes("fallback");
                  const color = isErr ? "#ef4444" : isWarn ? "#f59e0b80" : "#ffffff40";
                  return (
                    <div key={i} style={{ display: "flex", gap: 8, padding: "4px 6px", background: isErr ? "#ef444408" : "transparent", borderRadius: 3 }}>
                      <span style={{ fontSize: 8, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff25", flexShrink: 0, paddingTop: 1 }}>{time}</span>
                      <span style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color, flexShrink: 0, width: 210 }}>{tool} → {outcome}</span>
                      <span style={{ fontSize: 9, color: "#ffffff40", fontFamily: "'Barlow', sans-serif", lineHeight: 1.4 }}>{detail}</span>
                    </div>
                  );
                })}
              </div>
            </div>
          </div>
        )}

        {/* Main 3-column layout */}
        {selectedMenu === 1 && <div style={{ display: "grid", gridTemplateColumns: "300px 1fr 280px", gap: 16 }}>

          {/* COL 1: Pipeline */}
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            <div style={{
              background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: "16px",
              flex: 1,
            }}>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
                <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, letterSpacing: "0.1em", color: "#ffffff80", textTransform: "uppercase" }}>
                  MCP Pipeline
                </div>
              <button
                  onClick={handleRealRun}
                  disabled={apiRunning || running}
                  style={{
                    padding: "5px 12px", borderRadius: 5, border: "1px solid #00d4ff50",
                    background: (apiRunning || running) ? "#00d4ff10" : "#00d4ff20",
                    color: (apiRunning || running) ? "#00d4ff80" : "#00d4ff",
                    fontSize: 9, fontFamily: "'IBM Plex Mono', monospace",
                    cursor: (apiRunning || running) ? "not-allowed" : "pointer",
                    fontWeight: 600, letterSpacing: "0.05em",
                  }}
                >
                  {apiRunning ? "⏳ RUNNING REAL AI..." : running ? "ANIMATING..." : "▶ RUN"}
                </button>
              </div>

              {apiError && (
                <div style={{ fontSize: 9, color: "#ef4444", fontFamily: "'IBM Plex Mono', monospace", padding: "4px 8px", background: "#ef444410", borderRadius: 4, marginBottom: 8 }}>
                  {apiError}
                </div>
              )}
              {apiStatus === false && (
                <div style={{ fontSize: 9, color: "#f59e0b80", fontFamily: "'Barlow', sans-serif", marginBottom: 8 }}>
                  ⚠️ API offline — start the .NET app to enable real AI
                </div>
              )}
              {apiImages.length > 0 && !uploadedFileName && (
                <div style={{ marginBottom: 8, fontSize: 9, color: "#ffffff25", fontFamily: "'IBM Plex Mono', monospace" }}>
                  {apiImages.length} images available in dataset
                </div>
              )}

              {/* Image selector */}
              <div
                onClick={() => fileInputRef.current?.click()}
                style={{
                  padding: "12px 10px", background: "#ffffff05", border: `1px dashed ${uploadedImageUrl ? "#00d4ff40" : "#ffffff20"}`,
                  borderRadius: 6, marginBottom: 12, cursor: "pointer",
                  transition: "border-color 0.2s",
                }}
              >
                <input ref={fileInputRef} type="file" accept="image/*" style={{ display: "none" }} onChange={handleImageUpload} />
                {uploadedImageUrl ? (
                  <>
                    <img src={uploadedImageUrl} alt="uploaded" style={{ width: "100%", borderRadius: 4, marginBottom: 6, border: "1px solid #00d4ff20", objectFit: "contain", maxHeight: 140 }} />
                    <div style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#00d4ff90", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", textAlign: "center" }}>
                      {uploadedFileName}
                    </div>
                    <div style={{ fontSize: 8, color: "#ffffff30", fontFamily: "'Barlow', sans-serif", marginTop: 2, textAlign: "center" }}>click to change image</div>
                  </>
                ) : (
                  <div style={{ textAlign: "center", padding: "8px 0" }}>
                    <div style={{ fontSize: 18, marginBottom: 4 }}>⬆</div>
                    <div style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#00d4ff70", fontWeight: 600 }}>UPLOAD IMAGE</div>
                    <div style={{ fontSize: 9, color: "#ffffff30", fontFamily: "'Barlow', sans-serif", marginTop: 3 }}>click to browse</div>
                  </div>
                )}
              </div>

              <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                {PIPELINE_STEPS.map((step, idx) => (
                  <PipelineStep
                    key={step.id}
                    step={step}
                    idx={idx}
                    isActive={activeStep === idx}
                    isDone={completedSteps.includes(idx)}
                  />
                ))}
              </div>

              {done && (
                <div style={{
                  marginTop: 12, padding: "8px 12px",
                  background: "#10b98110", border: "1px solid #10b98140",
                  borderRadius: 6, textAlign: "center",
                  fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#10b981",
                }}>
                  ✅ {DISPLAY_DATA.agentActions?.length || 5} actions taken{DISPLAY_DATA.pipelineRuntimeMs ? ` · ${DISPLAY_DATA.pipelineRuntimeMs / 1000}s` : ""}
                </div>
              )}
            </div>
          </div>

          {/* COL 2: Case output + recent cases */}
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>

            {/* Case Summary */}
            <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
                <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, letterSpacing: "0.1em", color: "#ffffff80", textTransform: "uppercase" }}>
                  Live Case Output
                </div>
                <button
                  onClick={downloadReport}
                  style={{
                    display: "flex", alignItems: "center", gap: 5,
                    padding: "5px 12px", borderRadius: 5, border: "1px solid #10b98150",
                    background: "#10b98115", color: "#10b981",
                    fontSize: 9, fontFamily: "'IBM Plex Mono', monospace",
                    cursor: "pointer", fontWeight: 600, letterSpacing: "0.05em",
                  }}
                >⬇ DOWNLOAD REPORT</button>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10, marginBottom: 14 }}>
                {[
                  { label: "Case ID", value: liveData ? (DISPLAY_DATA.caseId || "—") : "—", color: "#00d4ff" },
                  { label: "Defect Type", value: liveData ? (DISPLAY_DATA.defectType || "—") : "—", color: "#ef4444" },
                  { label: "Severity", value: liveData ? (DISPLAY_DATA.severity || "—") : "—", color: liveData && DISPLAY_DATA.severity === "HIGH" ? "#ef4444" : liveData && DISPLAY_DATA.severity === "LOW" ? "#10b981" : "#f59e0b" },
                  { label: "Confidence", value: liveData ? (DISPLAY_DATA.rootCauseConfidence || "—") : "—", color: "#10b981" },
                  { label: "Taxonomy", value: liveData ? (DISPLAY_DATA.taxonomy || "—") : "—", color: "#8b5cf6" },
                  { label: "Disposition", value: liveData ? (DISPLAY_DATA.disposition || "—") : "PENDING", color: liveData ? (DISPLAY_DATA.disposition === "REJECT" ? "#ef4444" : "#10b981") : "#ffffff30" },
                ].map(item => (
                  <div key={item.label} style={{
                    padding: "8px 12px", background: "#ffffff04",
                    border: "1px solid #ffffff08", borderRadius: 6,
                  }}>
                    <div style={{ fontSize: 9, fontFamily: "'Barlow', sans-serif", color: "#ffffff35", textTransform: "uppercase", letterSpacing: "0.08em", marginBottom: 3 }}>{item.label}</div>
                    <div style={{ fontSize: 12, fontFamily: "'IBM Plex Mono', monospace", fontWeight: 600, color: item.color }}>{item.value}</div>
                  </div>
                ))}
              </div>

              {/* Root cause — only shown after a real pipeline run */}
              {!liveData && (
                <div style={{ padding: "24px 12px", background: "#ffffff04", border: "1px dashed #ffffff15", borderRadius: 6, marginBottom: 10, textAlign: "center" }}>
                  <div style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff25", marginBottom: 6 }}>ROOT CAUSE · IPC COMPLIANCE</div>
                  <div style={{ fontSize: 11, fontFamily: "'Barlow', sans-serif", color: "#ffffff35", lineHeight: 1.6 }}>
                    Press <span style={{ color: "#00d4ff60", fontWeight: 600 }}>RUN</span> to execute the AI pipeline<br />and see live analysis results here.
                  </div>
                </div>
              )}
              {liveData && <div style={{ padding: "10px 12px", background: "#f59e0b08", border: "1px solid #f59e0b20", borderRadius: 6, marginBottom: 10 }}>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 6 }}>
                  <div style={{ fontSize: 9, fontFamily: "'Barlow', sans-serif", color: "#f59e0b80", textTransform: "uppercase", letterSpacing: "0.08em" }}>Root Cause · Nova Lite · {DISPLAY_DATA.rootCauseConfidence} confidence</div>
                  <span style={{ fontSize: 8, fontFamily: "'IBM Plex Mono', monospace", color: "#f59e0b60", padding: "1px 5px", border: "1px solid #f59e0b30", borderRadius: 3 }}>STEP 4</span>
                </div>
                <div style={{ fontSize: 11, color: "#ffffff80", lineHeight: 1.7, fontFamily: "'Barlow', sans-serif", marginBottom: 8 }}>
                  {DISPLAY_DATA.rootCause}
                </div>
                <div style={{ fontSize: 9, color: "#f59e0b60", fontFamily: "'Barlow', sans-serif", fontStyle: "italic", marginBottom: 8, lineHeight: 1.5 }}>
                  {DISPLAY_DATA.rootCauseReasoning}
                </div>
                <div style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff35", marginBottom: 4 }}>CONTRIBUTING FACTORS</div>
                <div style={{ display: "flex", flexDirection: "column", gap: 3, marginBottom: 8 }}>
                  {DISPLAY_DATA.contributingFactors.map((f, i) => (
                    <div key={i} style={{ display: "flex", gap: 6, alignItems: "flex-start" }}>
                      <span style={{ color: "#f59e0b60", fontSize: 9, flexShrink: 0, marginTop: 1 }}>▸</span>
                      <span style={{ fontSize: 10, color: "#ffffff55", fontFamily: "'Barlow', sans-serif", lineHeight: 1.5 }}>{f}</span>
                    </div>
                  ))}
                </div>
                <div style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff35", marginBottom: 4 }}>RECOMMENDED ACTIONS</div>
                <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
                  {DISPLAY_DATA.rootCauseActions.map((a, i) => {
                    const pc = a.priority === "P1" ? "#ef4444" : a.priority === "P2" ? "#f59e0b" : "#10b981";
                    return (
                      <div key={i} style={{ display: "flex", gap: 6, alignItems: "flex-start" }}>
                        <span style={{ fontSize: 8, fontFamily: "'IBM Plex Mono', monospace", color: pc, padding: "1px 5px", border: `1px solid ${pc}40`, borderRadius: 3, flexShrink: 0, marginTop: 1 }}>{a.priority}</span>
                        <div>
                          <div style={{ fontSize: 10, color: "#ffffff70", fontFamily: "'Barlow', sans-serif", lineHeight: 1.5 }}>{a.action}</div>
                          <div style={{ fontSize: 9, color: "#ffffff30", fontFamily: "'IBM Plex Mono', monospace" }}>→ {a.owner}{a.contextRef ? ` · ${a.contextRef}` : ""}</div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>}

              {/* IPC compliance — only shown after a real pipeline run */}
              {liveData && <div style={{ padding: "10px 12px", background: "#8b5cf608", border: "1px solid #8b5cf620", borderRadius: 6 }}>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 6 }}>
                  <div style={{ fontSize: 9, fontFamily: "'Barlow', sans-serif", color: "#8b5cf680", textTransform: "uppercase", letterSpacing: "0.08em" }}>IPC-A-600J Compliance · RAG Retrieved</div>
                  <span style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#8b5cf660" }}>{DISPLAY_DATA.compliance.classification}</span>
                </div>
                <div style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: DISPLAY_DATA.compliance.disposition === "REJECT" ? "#ef4444a0" : "#10b981a0", marginBottom: 8, padding: "3px 6px", background: DISPLAY_DATA.compliance.disposition === "REJECT" ? "#ef444410" : "#10b98110", borderRadius: 3, border: `1px solid ${DISPLAY_DATA.compliance.disposition === "REJECT" ? "#ef444420" : "#10b98120"}` }}>
                  {DISPLAY_DATA.compliance.disposition}
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                  {DISPLAY_DATA.complianceChecks.map(item => (
                    <div key={item.ref}>
                      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                        <div style={{
                          width: 14, height: 14, borderRadius: "50%", display: "flex", alignItems: "center", justifyContent: "center",
                          background: item.passed ? "#10b98120" : "#ef444420", border: `1px solid ${item.passed ? "#10b98140" : "#ef444440"}`,
                          fontSize: 7, color: item.passed ? "#10b981" : "#ef4444", flexShrink: 0,
                        }}>{item.passed ? "✓" : "✗"}</div>
                        <span style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff40" }}>{item.ref}</span>
                        <span style={{ fontSize: 10, color: "#ffffff50", fontFamily: "'Barlow', sans-serif" }}>{item.title}</span>
                      </div>
                      {item.detail && (
                        <div style={{ fontSize: 9, color: item.passed ? "#10b98150" : "#ef444460", fontFamily: "'Barlow', sans-serif", marginLeft: 22, marginTop: 2, lineHeight: 1.4 }}>{item.detail}</div>
                      )}
                    </div>
                  ))}
                </div>
              </div>}
            </div>

            {/* Recent Cases Table */}
            <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
              <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, letterSpacing: "0.1em", color: "#ffffff80", textTransform: "uppercase", marginBottom: 12 }}>
                Recent Inspections
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                {RECENT_CASES.map((c, i) => (
                  <div key={c.id} style={{
                    display: "flex", alignItems: "center", gap: 10, padding: "8px 10px",
                    background: i === 0 ? "#00d4ff06" : "#ffffff03",
                    border: `1px solid ${i === 0 ? "#00d4ff20" : "#ffffff08"}`,
                    borderRadius: 6,
                  }}>
                    <div style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#00d4ff60", width: 60, flexShrink: 0 }}>
                      #{c.id.slice(0, 6)}
                    </div>
                    <div style={{ flex: 1 }}>
                      <div style={{ fontSize: 10, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff70" }}>{c.image}</div>
                      <div style={{ fontSize: 9, color: "#ffffff30", fontFamily: "'Barlow', sans-serif" }}>{c.defect}</div>
                    </div>
                    <div style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: SEVERITY_COLORS[c.severity.toLowerCase()] || "#f59e0b", width: 32, flexShrink: 0 }}>
                      {c.severity}
                    </div>
                    <div style={{
                      padding: "2px 8px", borderRadius: 3,
                      background: `${STATUS_COLORS[c.status]}15`,
                      border: `1px solid ${STATUS_COLORS[c.status]}30`,
                      fontSize: 8, fontFamily: "'IBM Plex Mono', monospace",
                      color: STATUS_COLORS[c.status], fontWeight: 700, flexShrink: 0,
                    }}>{c.status}</div>
                    <div style={{ fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff25", width: 20, textAlign: "right" }}>
                      {c.time}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* COL 3: Agent actions + defect stats */}
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>

            {/* Agent Actions */}
            <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 12 }}>
                <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, letterSpacing: "0.1em", color: "#ffffff80", textTransform: "uppercase" }}>
                  Agent Actions
                </div>
                <div style={{
                  marginLeft: "auto", padding: "2px 8px", borderRadius: 3,
                  background: "#10b98115", border: "1px solid #10b98130",
                  fontSize: 9, fontFamily: "'IBM Plex Mono', monospace", color: "#10b981",
                }}>AUTONOMOUS</div>
              </div>

              {!done && !running && (
                <div style={{ textAlign: "center", padding: "20px 0", color: "#ffffff20", fontSize: 11, fontFamily: "'IBM Plex Mono', monospace" }}>
                  Press RUN to see agent actions
                </div>
              )}
              {running && activeStep < 7 && (
                <div style={{ textAlign: "center", padding: "20px 0", color: "#00d4ff40", fontSize: 10, fontFamily: "'IBM Plex Mono', monospace" }}>
                  Pipeline running...<br />
                  <span style={{ fontSize: 9, color: "#ffffff20" }}>Agent loop fires after gate</span>
                </div>
              )}
              {(done || (running && activeStep >= 6)) && AGENT_ACTIONS.map((action, i) => (
                <AgentActionCard key={i} action={action} delay={i * 300} />
              ))}
            </div>

            {/* Defect Distribution */}
            <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
              <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, letterSpacing: "0.1em", color: "#ffffff80", textTransform: "uppercase", marginBottom: 12 }}>
                Defect Distribution
              </div>
              {DEFECT_DATA.map((d, i) => (
                <DefectBar key={d.type} defect={d} delay={i * 150} />
              ))}
              <div style={{ marginTop: 10, paddingTop: 10, borderTop: "1px solid #ffffff08", display: "flex", gap: 12 }}>
                {["high", "medium", "low"].map(s => (
                  <div key={s} style={{ display: "flex", alignItems: "center", gap: 4 }}>
                    <div style={{ width: 6, height: 6, borderRadius: 1, background: SEVERITY_COLORS[s] }} />
                    <span style={{ fontSize: 9, fontFamily: "'Barlow', sans-serif", color: "#ffffff40" }}>{s}</span>
                  </div>
                ))}
              </div>
            </div>

            {/* Knowledge Graph */}
            <div style={{ background: "#0d1117", border: "1px solid #ffffff10", borderRadius: 10, padding: 16 }}>
              <div style={{ fontFamily: "'Barlow Condensed', sans-serif", fontSize: 13, fontWeight: 700, letterSpacing: "0.1em", color: "#ffffff80", textTransform: "uppercase", marginBottom: 12 }}>
                Knowledge Graph
              </div>
              {[
                { label: "Nodes", value: "388", color: "#8b5cf6" },
                { label: "Relationships", value: "1,308", color: "#8b5cf6" },
                { label: "Images", value: "50", color: "#00d4ff" },
                { label: "Equipment Types", value: "7", color: "#f59e0b" },
                { label: "IPC Sections", value: "9", color: "#10b981" },
              ].map(item => (
                <div key={item.label} style={{
                  display: "flex", justifyContent: "space-between", alignItems: "center",
                  padding: "5px 0", borderBottom: "1px solid #ffffff06",
                }}>
                  <span style={{ fontSize: 10, fontFamily: "'Barlow', sans-serif", color: "#ffffff40" }}>{item.label}</span>
                  <span style={{ fontSize: 11, fontFamily: "'IBM Plex Mono', monospace", fontWeight: 600, color: item.color }}>{item.value}</span>
                </div>
              ))}
              <div style={{ marginTop: 10, padding: "6px 10px", background: "#8b5cf608", border: "1px solid #8b5cf620", borderRadius: 5 }}>
                <div style={{ fontSize: 9, fontFamily: "'Barlow', sans-serif", color: "#8b5cf680", marginBottom: 2 }}>Self-improving</div>
                <div style={{ fontSize: 9, color: "#ffffff40", fontFamily: "'Barlow', sans-serif", lineHeight: 1.5 }}>
                  Graph updates after every inspection. Co-occurrence edges + severity feedback compound over time.
                </div>
              </div>
            </div>
          </div>
        </div>}

        {/* Footer */}
        <div style={{
          marginTop: 20, paddingTop: 14, borderTop: "1px solid #ffffff08",
          display: "flex", justifyContent: "space-between", alignItems: "center",
        }}>
          <div style={{ fontFamily: "'IBM Plex Mono', monospace", fontSize: 9, color: "#ffffff25" }}>
            github.com/Ashahet1/AmazonNOVAHackathon · Amazon Nova AI Hackathon · March 2026
          </div>
          <div style={{ display: "flex", gap: 16 }}>
            {["Amazon Nova", "AWS Bedrock", ".NET 9", "MCP", "IPC-A-600J"].map(tag => (
              <span key={tag} style={{
                fontSize: 8, fontFamily: "'IBM Plex Mono', monospace", color: "#ffffff25",
                padding: "2px 6px", border: "1px solid #ffffff10", borderRadius: 3,
              }}>{tag}</span>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
