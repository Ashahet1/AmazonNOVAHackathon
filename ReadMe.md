# Manufacturing Knowledge Graph — PCB Quality Control with Amazon Nova

> **Agentic AI inspection system** built on **Amazon Bedrock (Amazon Nova Pro + Nova Lite)**
> and the **Model Context Protocol (MCP)** — demonstrating autonomous tool-calling, guardrails,
> knowledge graph reasoning, and IPC-A-600J compliance on real industrial PCB data.

---

## What Was Built

A **.NET 9 console application** that inspects PCB images through a **7-step MCP agentic
pipeline** powered entirely by **Amazon Bedrock**. When a defect is confirmed, an autonomous
agent loop calls real tools — quarantine records, work orders, and knowledge graph updates —
with no human input required.

The system moved completely off Azure. There is no Azure OpenAI, no Azure AI Vision, and no
Azure SDK dependency. Every AI call goes to **Amazon Nova** via the **Bedrock Converse API**.
---

## Architecture

```
+===========================================================================+
|           PCB IMAGE (input)                                               |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  STEP 1 — analyze_image_with_vision          [Amazon Nova Pro]           |
|  Image encoded as base64 → Bedrock Converse API (multimodal)             |
|  Outputs: defect caption, type, confidence, tags, object list             |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  STEP 2 — normalize_defect_with_ai           [Amazon Nova Lite]          |
|  Maps raw vision output to canonical defect taxonomy                      |
|  Outputs: DefectType, Severity, Taxonomy ID, InspectionMethods,          |
|           AI-inferred equipment (e.g. Etching machine)                   |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  STEP 3 — query_knowledge_graph              [In-Memory Graph]           |
|  Queries 388-node / 1308-edge knowledge graph for:                       |
|  * Related historical defects                                             |
|  * Equipment nodes linked to this defect type                             |
|  * IPC-A-600J / IPC-6012E standard references                            |
|  * Co-occurring defect patterns                                           |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  STEP 4 — root_cause_enriched                [Amazon Nova Lite]          |
|  Deep reasoning over defect + graph context                               |
|  Outputs: root cause, confidence %, contributing factors, 3 actions      |
|           (immediate / short-term / long-term) with IPC traceability     |
|  Validated: JSON schema + context ID citation check                      |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  STEP 5 — compliance_with_rag                [Amazon Nova Lite]          |
|  RAG: fetches relevant IPC sections for this defect type                 |
|  Runs compliance checklist against IPC-A-600J / IPC-6012E               |
|  Outputs: disposition (accept/reject), checklist items, coverage score   |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  [P] — policy_checks                         [Rule Engine]               |
|  Content policy: unsafe content, PII, confidence threshold               |
|  Flags or escalates to human review if triggered                         |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  [G] — final_review_gate                     [Rule Engine]               |
|  All steps must pass before Step 7 fires                                 |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  STEP 7 — agentic_action_loop                [Amazon Nova Lite]          |
|  Nova reasons over the full case and CALLS TOOLS autonomously:           |
|                                                                           |
|  Tool 1: quarantine_batch                                                 |
|    Writes record to outputs/quarantine_log.jsonl                          |
|                                                                           |
|  Tool 2: update_knowledge_graph                                           |
|    Adds co-occurrence edges, records severity feedback                    |
|    Saves updated graph to knowledge_graph.json                           |
|                                                                           |
|  Tool 3: file_work_order                                                  |
|    Creates WO-*.json in outputs/work_orders/ (one per action)            |
|    P1 / P2 / P3 priority assigned by Nova, per-assignee                  |
|                                                                           |
|  Loop: Nova observes tool results, calls more tools if needed            |
|  Exits on end_turn (max 5 iterations)                                    |
+===========================================================================+
                 |
                 v
+===========================================================================+
|  CASE FILE EXPORT                                                         |
|  Saved to outputs/cases/Case_*.txt + Case_*.json                         |
|  Vision · Defect · Graph context · Root cause · Compliance               |
|  AgentActions (ToolName, Input, Result, ExecutedAt) · full trace         |
+===========================================================================+
```

---

## Models Used

| Model | Used In | Why |
|---|---|---|
| `us.amazon.nova-pro-v1:0` | Step 1 — Vision analysis | Multimodal; handles base64 image input |
| `us.amazon.nova-lite-v1:0` | Steps 2, 4, 5, 7 — Reasoning + agentic loop | Fast, cost-efficient; supports tool use via Converse API |

Both accessed via **AWS Bedrock Converse API**. Region: `us-east-1`.

---

## Agentic Loop — Real Run Output

From `Case_0cd5493612dd` (defect: open circuit, high severity):

```
[7]  agentic_action_loop (Nova us.amazon.nova-lite-v1:0) ...

  ⚙️  Executing tool: quarantine_batch
     → ✅ Batch 'BATCH-20260303-0CD549' quarantined (severity: high).
          Record appended to outputs/quarantine_log.jsonl. Status: QUARANTINED

  ⚙️  Executing tool: update_knowledge_graph
     → ✅ Knowledge graph updated for 'open': severity feedback recorded:
          open → increase; notes logged: Confirmed high severity...

  ⚙️  Executing tool: file_work_order
     → ✅ WO-20260303-180947-0CD549 filed (P1, process_engineer).
          Action: "Inspect and calibrate the etching machine settings"

  ⚙️  Executing tool: file_work_order
     → ✅ WO-20260303-180947-0CD549 filed (P2, qa_team).
          Action: "Perform a thorough check of the etching solution"

  ⚙️  Executing tool: file_work_order
     → ✅ WO-20260303-180947-0CD549 filed (P3, maintenance).
          Action: "Review and update the maintenance schedule"

  ✅ Agentic loop complete — 5 action(s) taken.
```

Nova selected every tool, chose priorities and assignees, and filed three work orders
matching the three root-cause actions from Step 4 — with zero human instruction.

---

## Business Value

| What the Agent Does | Why It Matters |
|---|---|
| **Quarantines the batch** | Prevents defective PCBs shipping — record can block ERP/WMS/SAP downstream |
| **Files P1 WO → Manufacturing Engineer** | Etching machine calibration happens this shift, not next week |
| **Files P2 WO → QA Team** | Chemical check is assigned before anyone reads the report |
| **Files P3 WO → Maintenance** | PM schedule update is tracked and owned |
| **Updates knowledge graph** | Every future inspection of this defect type benefits from confirmed severity data |
| **Full audit trail in CaseFile** | 21 timestamped trace entries per case — ready for QMS integration |

**Current state:** outputs are local files.  
**Path to production:** replace `File.AppendAllTextAsync` in `AgentTools.cs` with an HTTP client
call to SAP QM, ServiceNow, or Oracle MES — the tool executor (`ExecuteAsync`) is the single
integration point.

---

## Interactive Menu (6 Options)

```
══════════════════════════════════════════════════════════════════════
  PCB DEFECT INSPECTION  [Amazon Nova · DeepPCB · 6 defect types]
══════════════════════════════════════════════════════════════════════
  1. 🏭  Inspect single PCB image  (MCP pipeline · Nova Pro)   ← runs all 7 steps
  2. 🔬  Batch inspect  (N images per defect category)
  3. 📈  Defect statistics & full dashboard
  4. 🧠  AI insights from knowledge graph  (Nova Lite)
  5. 📂  View / export last case report
  6. ❌  Exit
══════════════════════════════════════════════════════════════════════
```

Option 1 runs the complete 7-step agentic pipeline. Step 7 fires automatically after the
final review gate passes — no extra menu selection needed.

---

## Dataset

**DeepPCB** — Real PCB defect dataset from Peking University

```
datasets/PCBData/
  group00041/  group12000/  group12100/  group12300/
  group13000/  group20085/  group44000/  group50600/
  group77000/  group90100/
```

Each group: `<id>/` (test images) + `<id>_not/` (annotations, format: `x1 y1 x2 y2 class_id`)

**Defect classes:** `0` open · `1` short · `2` mousebite · `3` spur · `4` pin_hole · `5` spurious_copper

Loading 50 images → **388 nodes, 1308 relationships, ~327 defects**

---

## Project Structure

```
ManufacturingVisionAnalyzer/
├── ReadMe.md
├── appsettings.json                    ← AWS credentials (AmazonNova section only)
├── ManufacturingVisionAnalyzer.csproj  ← .NET 9, AWSSDK.BedrockRuntime only
│
├── Program.cs                          ← Entry point + 6-option menu
├── AppConfig.cs                        ← Config reader (appsettings + env vars)
├── BedrockNovaClient.cs                ← All Bedrock calls:
│                                          InvokeAsync (text)
│                                          InvokeWithImageAsync (vision, Nova Pro)
│                                          InvokeAgentLoopAsync (tool-calling loop)
├── AgentTools.cs                       ← 3 tool definitions + ExecuteAsync dispatcher
│                                          quarantine_batch
│                                          update_knowledge_graph
│                                          file_work_order
├── McpOrchestrator.cs                  ← 7-step MCP pipeline + RunAgenticActionLoop (Step 7)
├── CaseFile.cs                         ← Case model: Vision, Defect, Graph, RootCause,
│                                          Compliance, AgentActions, Trace
├── KnowledgeGraph.cs                   ← Graph (388 nodes), query API, JSON persistence
├── DeepPCBProcessor.cs                 ← DeepPCB dataset parser
├── EvaluationRunner.cs                 ← Evaluation metrics suite
├── Guardrails.cs                       ← Content policy, confidence threshold, human review
├── IpcComplianceReference.cs           ← IPC-A-600J / IPC-6012E sections (RAG source)
├── ChartGenerator.cs                   ← Console bar/pie/heatmap charts
│
├── outputs/
│   ├── cases/                          ← Case_*.txt + Case_*.json per inspection
│   ├── work_orders/                    ← WO-*.json filed by Step 7
│   └── quarantine_log.jsonl            ← One JSON line per quarantine event
│
├── datasets/PCBData/                   ← DeepPCB dataset
└── knowledge_graph.json                ← Cached graph (auto-updated by Step 7)
```

**Deleted (not in project):** `AzureVisionAnalyzer.cs`, `OpenAIVisionAnalyzer.cs`,
`GraphBuilder.cs`, `FlowchartFolderProcessor.cs`

---

## Source File Reference

| File | Role |
|---|---|
| **BedrockNovaClient.cs** | All Bedrock calls. `InvokeAgentLoopAsync` runs the tool-calling loop with Converse API `ToolConfig` |
| **AgentTools.cs** | `GetToolDefinitions()` returns JSON schemas for all 3 tools. `ExecuteAsync` dispatches by tool name and records to `CaseFile.AgentActions` |
| **McpOrchestrator.cs** | Orchestrates all 7 steps. `RunAgenticActionLoop` builds context, calls `InvokeAgentLoopAsync`, logs results |
| **CaseFile.cs** | `AgentActions = List<AgentAction>` — each entry has ToolName, Input, Result, ExecutedAt |
| **KnowledgeGraph.cs** | `AddRelationship`, `GetNodeById`, `SaveToFile` — updated live by Step 7 |
| **IpcComplianceReference.cs** | Hardcoded IPC sections used as RAG context in Step 5 compliance check |
| **Guardrails.cs** | Policy checks that run between Step 5 and Step 7 |

---

## Quick Start

### Prerequisites

- **.NET 9.0 SDK**
- **AWS account** with Bedrock access in `us-east-1`
- Amazon Nova Pro and Nova Lite **enabled** in Bedrock Model Access
- **DeepPCB dataset** extracted to `datasets/PCBData/`

### 1. Configure AWS Credentials

Edit `appsettings.json`:

```json
{
  "AmazonNova": {
    "AccessKey":     "YOUR-ACCESS-KEY-ID",
    "SecretKey":     "YOUR-SECRET-ACCESS-KEY",
    "Region":        "us-east-1",
    "VisionModelId": "us.amazon.nova-pro-v1:0",
    "LiteModelId":   "us.amazon.nova-lite-v1:0"
  }
}
```

### 2. Build & Run

```bash
dotnet restore
dotnet build
dotnet run
```

### 3. First Run — Load the Graph

At startup press **Enter** for default dataset path, then choose **1** to load from cache
(instant). If no cache exists, choose **2** to build from dataset (~1 min for 50 images).

### 4. Run the Full Agentic Pipeline

Select **Option 1** → press Enter for the sample image → all 7 steps run automatically.

---

## Technical Details

| | |
|---|---|
| **Language** | C# / .NET 9.0 |
| **AI Provider** | AWS Bedrock — Amazon Nova Pro + Nova Lite |
| **API** | Bedrock Converse API (`AmazonBedrockRuntimeClient.ConverseAsync`) |
| **Tool Calling** | Converse API `ToolConfig` with `ToolInputSchema` (Amazon.Runtime.Documents.Document) |
| **NuGet** | `AWSSDK.BedrockRuntime` only |
| **Serialization** | `System.Text.Json` (built-in) |
| **Graph** | In-memory, persisted as `knowledge_graph.json` |
| **Region** | `us-east-1` (Nova cross-region inference profile) |
| **Memory** | < 50 MB for 50-image graph |

---

## Troubleshooting

| Problem | Solution |
|---|---|
| **Step 1 fails** | Confirm `VisionModelId = us.amazon.nova-pro-v1:0` and Nova Pro enabled in Bedrock Model Access |
| **Step 7 skipped** | Step 7 only runs if `final_review_gate` PASSED — check for policy violations in trace |
| **Tool loop error** | Check that `block.ToolUse.Input` is non-null in `InvokeAgentLoopAsync` |
| **Sample image not found** | Ensure dataset is at `datasets/PCBData/` not nested deeper |
| **Knowledge graph empty** | Choose option 2 at startup to rebuild from dataset |

---

## References

- **Amazon Bedrock** — https://aws.amazon.com/bedrock/
- **Amazon Nova** — https://aws.amazon.com/bedrock/nova/
- **Bedrock Converse API** — https://docs.aws.amazon.com/bedrock/latest/userguide/conversation-inference.html
- **Model Context Protocol** — https://modelcontextprotocol.io
- **DeepPCB Dataset** — Ding et al., "TDD-net: a tiny defect detection network for printed circuit boards", CAAI Transactions 2019
- **IPC-A-600J / IPC-6012E** — PCB acceptability and performance standards
- **GitHub Copilot** — https://github.com/features/copilot

---

## License

Educational / hackathon project.

- **Amazon Bedrock / Nova**: Requires AWS account with Bedrock access
- **DeepPCB Dataset**: Free for research and educational use

---

**Last Updated**: 2026-03-03  
**Version**: 5.0 — Amazon Nova · 7-step agentic pipeline · 3 autonomous tools · No Azure dependencies  
**Status**: Working end-to-end (all 7 steps confirmed live)  
**Built with**: GitHub Copilot + Amazon Bedrock + Amazon Nova Pro/Lite  
**Repository**: https://github.com/Ashahet1/AmazonNOVAHackathon  
**Repository**: https://github.com/Ashahet1/AmazonNOVAHackathon
