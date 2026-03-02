# Manufacturing Knowledge Graph - Cross-Modal Intelligence for PCB Quality Control

> **AI-powered PCB defect inspection system** built on Azure OpenAI, Azure AI Vision,
> and the Model Context Protocol (MCP) - demonstrating agentic AI, guardrails, evaluation
> metrics, and knowledge graph reasoning on real industrial data.

---

## Architecture Diagram

```
+=======================================================================================+
|         MANUFACTURING KNOWLEDGE GRAPH - FULL SYSTEM ARCHITECTURE                     |
+=======================================================================================+
|                                                                                       |
|  +----------------------------------------------------------------------+            |
|  |                       DEVELOPER LAYER                                |            |
|  |                                                                      |            |
|  |  +----------------------+    +----------------------------------+   |            |
|  |  |   GitHub Copilot     |    |   Azure AI Foundry               |   |            |
|  |  |  ------------------- |    |  -------------------------------- |   |            |
|  |  |  * Code generation   |    |  * Model catalog (GPT-4.1,       |   |            |
|  |  |  * Inline suggest    |    |    GPT-4.1-nano, o4-mini,        |   |            |
|  |  |  * Chat (this arch)  |    |    GPT-4.1-mini)                 |   |            |
|  |  |  * PR review         |    |  * Prompt flow orchestration     |   |            |
|  |  |  * Test generation   |    |  * Safety & content filters      |   |            |
|  |  +----------------------+    |  * Evaluation framework          |   |            |
|  |                              |  * Deployment & versioning       |   |            |
|  |                              +----------------------------------+   |            |
|  +----------------------------------------------------------------------+            |
|                                         |                                            |
|                                         v                                            |
|  +----------------------------------------------------------------------+            |
|  |               MCP AGENTIC INSPECTION PIPELINE                       |            |
|  |           (Model Context Protocol - 6-step Agent Loop)              |            |
|  |                                                                      |            |
|  |   PCB Image                                                          |            |
|  |       |                                                              |            |
|  |       v                                                              |            |
|  |  +---------+   +---------+   +----------+   +------------------+   |            |
|  |  | Step 1  |-->| Step 2  |-->|  Step 3  |-->|     Step 4       |   |            |
|  |  | Vision  |   | Context |   |Guardrails|   |  Root-Cause      |   |            |
|  |  |Analysis |   |Retrieval|   | & Policy |   |  Reasoning       |   |            |
|  |  |GPT-4.1  |   | (Graph) |   | Checks   |   |  (o4-mini)       |   |            |
|  |  +---------+   +---------+   +----------+   +------------------+   |            |
|  |                                                       |             |            |
|  |  +----------------------------------------------+    v             |            |
|  |  |              Step 6                          | +--------------+ |            |
|  |  |         Case File Export                     | |   Step 5     | |            |
|  |  |   (JSON / TXT audit-ready report)            | | IPC-A-610    | |            |
|  |  +----------------------------------------------+ | Compliance   | |            |
|  |                                                   | Check        | |            |
|  |                                                   |(GPT-4.1-mini)| |            |
|  |                                                   +--------------+ |            |
|  +----------------------------------------------------------------------+            |
|                                         |                                            |
|              +---------------------------+---------------------------+               |
|              v                           v                           v               |
|  +------------------+      +--------------------+      +--------------------+       |
|  |  AZURE OPENAI    |      |  KNOWLEDGE GRAPH   |      |  AZURE AI VISION   |       |
|  |  --------------  |      |  ---------------   |      |  ----------------  |       |
|  |  GPT-4.1         |      |  * 388+ nodes      |      |  * Image Analysis  |       |
|  |  (Vision+Chat)   |      |  * 1300+ edges     |      |  * Caption & Tags  |       |
|  |                  |      |  * 6 defect types  |      |  * Object detect   |       |
|  |  GPT-4.1-mini    |      |  * Equipment nodes |      |  * OCR (bbox)      |       |
|  |  (Compliance)    |      |  * ISO standards   |      |                    |       |
|  |                  |      |  * JSON persist    |      |                    |       |
|  |  GPT-4.1-nano    |      |  * Queryable API   |      |                    |       |
|  |  (Classif.)      |      |                    |      |                    |       |
|  |                  |      |                    |      |                    |       |
|  |  o4-mini         |      |                    |      |                    |       |
|  |  (Reasoning)     |      |                    |      |                    |       |
|  +------------------+      +--------------------+      +--------------------+       |
|                                         |                                            |
|                                         v                                            |
|  +----------------------------------------------------------------------+            |
|  |                    DATA LAYER - DeepPCB Dataset                     |            |
|  |                                                                      |            |
|  |   PCBData/                                                           |            |
|  |   +-- group00041/  +-- group12000/  +-- group12100/ ... (9 groups)  |            |
|  |   |   +-- 00041/   |   +-- 12000/  |   +-- 12100/                  |            |
|  |   |   |  *_test.jpg|   |  *_test.. |   |  *_test..  (test images)  |            |
|  |   |   +-- 00041_not|   +-- 12000_nt|   +-- 12100_nt (annotations)  |            |
|  |   |      *.txt     |      *.txt    |      *.txt  (x1 y1 x2 y2 cls) |            |
|  |                                                                      |            |
|  |   Defect classes: 0=open  1=short  2=mousebite                      |            |
|  |                   3=spur  4=pin_hole  5=spurious_copper              |            |
|  +----------------------------------------------------------------------+            |
|                                         |                                            |
|                                         v                                            |
|  +----------------------------------------------------------------------+            |
|  |               EVALUATION & GOVERNANCE LAYER                        |            |
|  |                                                                      |            |
|  |  +------------------+  +------------------+  +------------------+  |            |
|  |  | Triage Accuracy  |  | JSON Validity    |  | Traceability     |  |            |
|  |  | Type + Severity  |  | Schema checks    |  | Context cite     |  |            |
|  |  +------------------+  +------------------+  +------------------+  |            |
|  |  +------------------+  +------------------+  +------------------+  |            |
|  |  | Policy Violation |  | Compliance       |  | Human Review     |  |            |
|  |  | Rate (Guardrails)|  | Completeness     |  | Escalation Rate  |  |            |
|  |  +------------------+  +------------------+  +------------------+  |            |
|  +----------------------------------------------------------------------+            |
+=======================================================================================+
```

### Service -> Role Mapping

| Microsoft / Azure Service | Role in This System |
|---|---|
| **Azure AI Foundry** | Model catalog, deployment management, per-step model selection (GPT-4.1 / GPT-4.1-mini / GPT-4.1-nano / o4-mini), content safety filters |
| **Azure OpenAI - GPT-4.1** | Step 1 vision analysis, step 2 context enrichment, AI insights (option 8), co-occurrence insights |
| **Azure OpenAI - o4-mini** | Step 4 root-cause reasoning (cost-efficient, deep logical chain) |
| **Azure OpenAI - GPT-4.1-mini** | Step 5 IPC-A-610 compliance checking (fast, instruction-following) |
| **Azure OpenAI - GPT-4.1-nano** | Step 2 defect classification (ultra-fast normalization) |
| **Azure AI Vision** | Raw image analysis - Caption, Tags, Object detection, OCR with bounding boxes |
| **Azure Model Context Protocol (MCP)** | Agent loop backbone: Tools = Vision, Context Retrieval, Guardrails, Reasoning, Compliance |
| **GitHub Copilot** | Entire codebase developed with Copilot Chat + inline suggestions; architecture designed in Copilot Chat |

### Business Value

| Business Problem | This System Solves It By |
|---|---|
| **Manual PCB inspection is slow & error-prone** | Automated 6-step AI pipeline with structured outputs in < 30 sec / board |
| **Defect patterns are siloed per board** | Knowledge graph accumulates cross-board learning - patterns compound over time |
| **No audit trail for AI decisions** | Full CaseFile with timestamped trace per inspection step, exportable to JSON/TXT |
| **AI outputs can be unsafe or hallucinated** | Guardrails layer (Step 3) redacts unsafe content and flags policy violations before actions |
| **Hard to prove AI quality to quality managers** | Evaluation suite (option 15) reports Triage Accuracy, JSON Validity, Traceability Score |
| **Different defects need different AI models** | Per-step model routing: reasoning tasks -> o4-mini; compliance -> GPT-4.1-mini |
| **Repair teams don't know co-occurring defects** | Co-occurrence analysis (option 2) surfaces defect pairs with GPT-generated combined fix |

---

## Project Overview

A **.NET 9 console application** that builds a cross-modal knowledge graph for PCB quality
control, then exposes it through an **MCP-style 6-step agentic inspection pipeline**. The
system connects visual defect data from real PCB images (**DeepPCB dataset**) with process
knowledge, inspection equipment, and IPC-A-610 compliance standards - all surfaced through
an **interactive 18-option menu** with analytics, AI-powered insights, batch processing,
and a full evaluation suite.

### Two Azure AI Services Working Together

| Service | Role | What It Does |
|---|---|---|
| **Azure AI Vision** | The Eyes | Image Analysis (Caption, Tags, Objects), OCR with bounding-box coordinates |
| **Azure OpenAI (GPT-4.1 family + o4-mini)** | The Brain | Vision analysis, defect classification, root-cause reasoning, compliance checking |

Azure Vision extracts raw visual data. Azure OpenAI reasons on top - via a multi-step MCP
agent loop that produces structured, traceable, auditable inspection reports.

---

## MCP Inspection Pipeline (6 Steps)

Option **13 / 14** runs the full agentic loop on any PCB image:

```
PCB Image File
      |
      v
+--------------------------------------------------------------------------+
|  STEP 1 - VISION ANALYSIS                              [GPT-4.1]        |
|  Sends image as base64 to GPT-4.1 vision endpoint.                     |
|  Outputs: defect_type, confidence, bounding region, raw description.   |
|  Status: OK / FAIL (null if API unreachable)                            |
+--------------------------------------------------------------------------+
      |
      v
+--------------------------------------------------------------------------+
|  STEP 2 - CONTEXT RETRIEVAL                        [Knowledge Graph]    |
|  Queries the in-memory knowledge graph for:                             |
|  * Historical defects matching this type                                |
|  * Equipment recommended for this defect                                |
|  * Similar defects across all PCB groups                                |
|  Injects retrieved context IDs into the case file.                     |
+--------------------------------------------------------------------------+
      |
      v
+--------------------------------------------------------------------------+
|  STEP 3 - GUARDRAILS & POLICY                      [Rule Engine + AI]  |
|  Checks the vision output against:                                      |
|  * Content policy (unsafe / PII / IP leakage)                          |
|  * Confidence threshold (< 0.5 -> human review flag)                   |
|  * Redaction rules (masks fields matching policy patterns)              |
|  Violations logged to CaseFile.PolicyViolations[]                      |
+--------------------------------------------------------------------------+
      |
      v
+--------------------------------------------------------------------------+
|  STEP 4 - ROOT-CAUSE REASONING                           [o4-mini]     |
|  Deep reasoning chain prompted with:                                    |
|  * Defect type + confidence from Step 1                                 |
|  * Retrieved historical context from Step 2                             |
|  Outputs: root cause, recommended action, estimated severity.           |
|  o4-mini used here for cost-efficient multi-step logical reasoning.     |
+--------------------------------------------------------------------------+
      |
      v
+--------------------------------------------------------------------------+
|  STEP 5 - IPC-A-610 COMPLIANCE CHECK              [GPT-4.1-mini]      |
|  Verifies the recommended action against IPC-A-610 standard:           |
|  * Checks required checklist items coverage                             |
|  * Flags incomplete or non-compliant recommendations                    |
|  * Sets HumanReviewRequired = true if confidence < threshold            |
+--------------------------------------------------------------------------+
      |
      v
+--------------------------------------------------------------------------+
|  STEP 6 - CASE FILE EXPORT                                              |
|  Assembles full CaseFile:                                               |
|  * Per-step results with OK/FAIL/WARNING status                         |
|  * Timestamped audit trace (tool, outcome, detail, ms timing)           |
|  * PolicyViolations[], HumanReviewRequired flag                         |
|  Exports to JSON or TXT for ERP / QMS integration.                     |
+--------------------------------------------------------------------------+
```

---

## Dataset

**DeepPCB** - Real PCB defect dataset from Peking University

```
datasets/
+-- PCBData/
    +-- group00041/
    |   +-- 00041/                       <- test images
    |   |   +-- 00041000_test.jpg
    |   |   +-- 00041000_temp.jpg        <- reference (template) image
    |   |   +-- ...
    |   +-- 00041_not/                   <- annotation files
    |       +-- 00041000.txt             <- format: x1 y1 x2 y2 class_id
    |       +-- ...
    +-- group12000/
    +-- group12100/
    +-- group12300/
    +-- group13000/
    +-- group20085/
    +-- group44000/
    +-- group50600/
    +-- group77000/
    +-- group90100/
```

**Defect classes:** `0` open . `1` short . `2` mousebite . `3` spur . `4` pin_hole . `5` spurious_copper

Loading 50 images produces: **~388 nodes, ~1308 relationships, ~327 defects**

---

## Interactive Menu (18 Options)

```
INTERACTIVE QUERY MENU  [DeepPCB . 1500 images . 6 defect types]
======================================================================

  -- KNOWLEDGE GRAPH ---------------------------------------------
  1.  PCB defect statistics (by category)
  2.  Find co-occurring defects  (NOVEL!)
  3.  Equipment recommendations by defect type
  4.  Browse defects by category
  5.  Custom defect search
  6.  Generate visual diagram
  7.  Export graph to file
  8.  AI-generated insights (GPT-4.1)
  9.  VIEW COMPLETE DASHBOARD WITH VISUALIZATIONS  ***

  -- CACHE MANAGEMENT --------------------------------------------
  10. Save graph to cache
  11. Rebuild graph from DeepPCB dataset
  12. Delete cache

  -- MCP INSPECTION PIPELINE ------------------------------------
  13. MCP Pipeline - single image  ***
  14. MCP Pipeline - batch (N images per category)
  15. Run full evaluation suite
  16. View / export case report
  17. Compare evaluation results

  18. Exit
======================================================================
```

### Option Highlights

| Option | Description |
|--------|-------------|
| **1** | Query all 6 DeepPCB defect types - includes 0-count categories with "not in current sample" note |
| **2** | Co-occurrence analysis: defect-type pairs that appear on the same boards, GPT-4.1 combined-fix insight per pair |
| **8** | Azure OpenAI generates 4 actionable business insights from graph data |
| **9** | Full dashboard: metrics, bar charts, pie chart, heatmap, network diagram, 7 local insights |
| **13** | Run the 6-step MCP pipeline on a single image of your choice |
| **14** | Batch-run MCP pipeline on N random images per defect category, aggregated results table |
| **15** | Evaluation suite: runs labeled test cases, reports Triage Accuracy / JSON Validity / Traceability / Compliance |
| **16** | Inspect the last case file - full timestamped trace, export JSON or TXT |
| **17** | Side-by-side comparison of two evaluation reports with pass-rate delta |

---

## Evaluation Suite (Option 15)

Runs labeled test cases from `test_cases/expected_labels.json` through the full MCP pipeline
and produces a governance metrics report:

| Metric | What It Measures |
|--------|-----------------|
| **Triage Accuracy (Type)** | % of cases where AI-detected defect type matches the annotation label |
| **Triage Accuracy (Severity)** | % of cases where AI-assigned severity matches expected |
| **JSON Validity Rate** | % of agent outputs passing the output schema validation |
| **Traceability Score** | % of outputs that cite retrieved knowledge graph context IDs |
| **Policy Violation Rate** | % of outputs that triggered redaction or guardrail flags |
| **Compliance Completeness** | % coverage of required IPC-A-610 checklist items |
| **Human Review Rate** | % of cases flagged for human sign-off (low confidence / policy violations) |

Reports are exported to `outputs/evaluations/` as timestamped JSON. Use option 17 to compare
two runs side-by-side and measure improvement.

---

## Quick Start

### Prerequisites

- **.NET 9.0 SDK**
- **Azure OpenAI** resource with `gpt-4.1` deployment
- **Azure AI Vision** resource (optional - required only for rebuild)
- **DeepPCB dataset** extracted to `datasets/PCBData/`

### 1. Configure Credentials

Edit `appsettings.json`:

```json
{
  "AzureVision": {
    "Endpoint": "https://YOUR-VISION.cognitiveservices.azure.com/",
    "Key": "YOUR-VISION-KEY"
  },
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-OPENAI.openai.azure.com/",
    "Key": "YOUR-OPENAI-KEY",
    "DeploymentName": "gpt-4.1",
    "ApiVersion": "2025-01-01-preview",
    "VisionModel": "gpt-4.1",
    "ClassificationModel": "gpt-4.1-nano",
    "ReasoningModel": "o4-mini",
    "ComplianceModel": "gpt-4.1-mini"
  }
}
```

> **Note:** `Endpoint` must be the **base URL only** (e.g. `https://myresource.openai.azure.com/`).
> Do not include `/openai/deployments/...` - the code constructs the full URL automatically.

**Or use environment variables:**

```powershell
$env:AZURE_OPENAI_ENDPOINT   = "https://myresource.openai.azure.com/"
$env:AZURE_OPENAI_KEY        = "your-key"
$env:AZURE_OPENAI_DEPLOYMENT = "gpt-4.1"
```

### 2. Build & Run

```bash
dotnet restore
dotnet build
dotnet run
```

### 3. First Run - Load the Graph

At the startup prompt, load from cache (option **1** at the dataset menu) if
`knowledge_graph.json` exists - loads instantly. Otherwise choose option **3** to build
from the DeepPCB dataset (~50 images, ~1 min).

### 4. Try the MCP Pipeline (option 13)

```
Enter image path: datasets\PCBData\group00041\00041\00041000_test.jpg
```

The 6-step pipeline runs, each step shows OK/FAIL, and a full case report is printed and saved.

---

## Project Structure

```
ManufacturingVisionAnalyzer/
+-- ReadMe.md                              <- This file
+-- appsettings.json                       <- Azure credentials (base URL only for OpenAI)
+-- ManufacturingVisionAnalyzer.csproj     <- .NET 9.0 project
|
+-- Program.cs                             <- Entry point + 18-option menu + all query workflows
+-- AppConfig.cs                           <- Centralized config reader (appsettings + env vars)
+-- KnowledgeGraph.cs                      <- Graph model, queries, analytics, JSON persistence
+-- DeepPCBProcessor.cs                    <- DeepPCB dataset parser (_not/ annotation convention)
+-- OpenAIVisionAnalyzer.cs                <- Vision step: base64 image -> GPT-4.1 vision API
+-- McpOrchestrator.cs                     <- 6-step MCP loop: Vision->Context->Guard->Reason->Comply->Export
+-- EvaluationRunner.cs                    <- Evaluation suite: labeled test cases + 7 metrics
+-- GuardrailsEngine.cs                    <- Content policy, redaction, human-review escalation
+-- AzureVisionAnalyzer.cs                 <- Azure AI Vision SDK (rebuild mode)
+-- GraphBuilder.cs                        <- Domain knowledge rules, equipment/standard nodes
+-- ChartGenerator.cs                      <- Console charts: bar, pie, heatmap, network diagram
|
+-- test_cases/
|   +-- expected_labels.json               <- 15 labeled PCB images for evaluation
|
+-- datasets/
|   +-- PCBData/                           <- DeepPCB dataset (9 groups)
|
+-- outputs/
|   +-- evaluations/                       <- Evaluation reports (timestamped JSON)
|   +-- cases/                             <- Individual MCP case files
|
+-- knowledge_graph.json                   <- Cached graph (auto-generated, ~388 nodes)
```

---

## Architecture - Source Files

| File | Purpose |
|---|---|
| **Program.cs** | 18-option interactive menu, all query/analytics/pipeline workflows |
| **AppConfig.cs** | Single config source: reads `appsettings.json`, applies env-var overrides, defensively strips malformed endpoints |
| **McpOrchestrator.cs** | MCP agent loop - coordinates the 6 steps, writes the audit trace, manages CaseFile lifecycle |
| **OpenAIVisionAnalyzer.cs** | Step 1 implementation - encodes image to base64, calls GPT-4.1 vision endpoint, parses vision response |
| **GuardrailsEngine.cs** | Step 3 - content policy checks, PII redaction, confidence thresholding, human-review escalation |
| **EvaluationRunner.cs** | Option 15 - loads `expected_labels.json`, runs pipeline per case, computes 7 governance metrics |
| **DeepPCBProcessor.cs** | Reads DeepPCB `_not/` annotation files, maps bounding-box defect codes to class names, links to image nodes |
| **KnowledgeGraph.cs** | In-memory graph (List<Node>, List<Relationship>), query API, 7 local analytics, JSON persistence |
| **AzureVisionAnalyzer.cs** | Azure AI Vision SDK (used during graph rebuild - Caption, Tags, Objects) |
| **GraphBuilder.cs** | Domain rules - maps defect types to equipment/standard nodes |
| **ChartGenerator.cs** | Console-based bar charts, pie charts, heatmaps, dashboards (no external dependencies) |

---

## Troubleshooting

| Problem | Solution |
|---|---|
| **MCP Step 1 FAIL** | Check `appsettings.json` endpoint is base URL only: `https://myresource.openai.azure.com/` - no path suffix |
| **OpenAI 404** | Verify `DeploymentName` matches exactly (e.g. `gpt-4.1`). Verify `ApiVersion = 2025-01-01-preview`. |
| **Option 14 "dataset not found"** | Ensure dataset is at `datasets/PCBData/` not `datasets/DeepPCB/PCBData/` |
| **Option 15 all cases skipped** | `test_cases/expected_labels.json` paths must match `datasets/PCBData/...` - already fixed to real paths |
| **0 defects in graph** | Run option 11 to rebuild. Annotations are in `*_not/` subfolders - handled automatically by `DeepPCBProcessor` |
| **"open" shows 0** | Correct - the 50-image sample may not contain "open" defects. Option 1 now shows all 6 types with 0-count note |
| **Vision auth failed** | Verify endpoint includes `https://` and trailing `/`. Key has no extra spaces. |

---

## Technical Details

| | |
|---|---|
| **Language** | C# / .NET 9.0 |
| **Azure OpenAI** | GPT-4.1 (vision + chat), GPT-4.1-nano, GPT-4.1-mini, o4-mini - Chat Completions API |
| **Azure AI Vision** | Image Analysis 4.0 (Caption, Tags, Objects, OCR) |
| **Protocol** | Azure MCP pattern (Tool -> Context -> Guard -> Reason -> Comply -> Export) |
| **NuGet** | `Azure.AI.Vision.ImageAnalysis 1.0.0-beta.3` |
| **Serialization** | `System.Text.Json` (built-in) |
| **Graph** | In-memory (List<Node>, List<Relationship>), persisted as JSON |
| **Evaluation** | 7 governance metrics, exportable JSON reports, side-by-side comparison |
| **Memory** | < 50 MB for 50-image DeepPCB graph |

---

## References

- **Azure AI Foundry** - https://ai.azure.com
- **Azure OpenAI Service** - https://learn.microsoft.com/azure/ai-services/openai/
- **Azure AI Vision** - https://learn.microsoft.com/azure/ai-services/computer-vision/
- **Model Context Protocol (MCP)** - https://modelcontextprotocol.io
- **DeepPCB Dataset** - Ding et al., "TDD-net: a tiny defect detection network for printed circuit boards", CAAI Transactions 2019
- **IPC-A-610** - Acceptability of Electronic Assemblies standard
- **GitHub Copilot** - https://github.com/features/copilot

---

## License

Educational / demo project.

- **Azure OpenAI / Azure AI Vision**: Requires Azure subscription
- **DeepPCB Dataset**: Free for research and educational use (see dataset readme)

---

**Last Updated**: 2026-03-02
**Version**: 4.0 - MCP agentic pipeline, DeepPCB, 18-option menu, Evaluation suite, Guardrails, Azure AI Foundry multi-model routing
**Status**: Complete
**Built with**: GitHub Copilot + Azure AI Foundry + Azure OpenAI + Azure AI Vision