# Building an Agentic PCB Defect Inspector with Amazon Nova: From Seeing to Acting

> *How I used Amazon Nova Pro, Nova Lite, and the Bedrock Converse API to build a system
> that doesn't just detect defects — it autonomously quarantines batches, files work orders,
> and updates its own knowledge base.*

---

## The Problem: AI That Observes But Doesn't Act

Most AI-powered inspection systems stop at the same point: they analyze an image and produce a
report. Someone reads the report, decides what to do, and then sends emails or opens tickets.
The AI was just a smarter camera.

In PCB manufacturing, a single defective batch that reaches assembly can cause field failures,
warranty claims, and in safety-critical electronics, real harm. The decision loop — detect,
decide, act, escalate — needs to happen in seconds, not hours.

This project started with one question: **can I build a system where Amazon Nova not only
detects the defect, but decides what to do about it and does it?**

---

## What I Built

**Manufacturing Knowledge Graph** is a .NET 9 console application that runs a 7-step agentic
inspection pipeline on PCB images using Amazon Nova on AWS Bedrock. It connects visual defect
data from the real-world **DeepPCB dataset** (Peking University) with process knowledge,
IPC-A-600J compliance standards, and a live manufacturing knowledge graph.

When a defect is confirmed, an autonomous agent loop (Step 7) calls real tools — it doesn't
recommend actions, it takes them.

**GitHub:** https://github.com/Ashahet1/AmazonNOVAHackathon

---

## The Architecture: 7 Steps from Pixel to Action

```
PCB Image
    │
    ▼
[Step 1] Nova Pro — Vision Analysis
         Base64 image → Bedrock Converse API (multimodal)
         Output: defect type, confidence 85%, tags, objects
    │
    ▼
[Step 2] Nova Lite — Defect Normalization
         Maps raw vision to canonical taxonomy (DEF-OPEN-PCB)
         AI-infers likely equipment: Etching machine, Drilling machine
    │
    ▼
[Step 3] Knowledge Graph Query
         388-node graph: related defects, IPC sections, co-occurrence patterns
    │
    ▼
[Step 4] Nova Lite — Root Cause Reasoning
         Deep reasoning over defect + graph context
         Output: root cause (etching process anomaly), 3 actions with IPC traceability
         Validated: JSON schema + context citation check
    │
    ▼
[Step 5] Nova Lite — IPC Compliance (RAG)
         Retrieves IPC-A-600J / IPC-6012E sections for this defect type
         Produces checklist, disposition: reject
    │
    ▼
[Policy + Gate] Guardrails check → must pass before Step 7
    │
    ▼
[Step 7] Nova Lite — Agentic Action Loop ← THE NEW PART
         Nova reasons over the full case, calls tools autonomously
         No human instruction at this stage
```

**Two Amazon Nova models, one Bedrock API** — Nova Pro handles the vision step (multimodal
image understanding), Nova Lite handles all reasoning, compliance, and the agentic loop
(fast, cost-efficient, full tool-use support via Converse API).

---

## The Agentic Loop: Nova Calling Tools

This is the core innovation. I defined three tools with JSON schemas and passed them to
Nova Lite via the Bedrock Converse API `ToolConfig`:

```csharp
// From BedrockNovaClient.cs — the tool-calling loop
var request = new ConverseRequest
{
    ModelId = _liteModelId,
    System = new List<SystemContentBlock> { new() { Text = systemPrompt } },
    Messages = messages,
    ToolConfig = new ToolConfiguration { Tools = tools }
};

// Loop: Nova picks tools, we execute, Nova observes results, repeat
while (iteration < maxIterations)
{
    var response = await _client.ConverseAsync(request);
    if (response.StopReason == "end_turn") break;

    // Execute every tool Nova called
    foreach (var block in response.Output.Message.Content
                          .Where(b => b.ToolUse != null))
    {
        var result = await toolExecutor(block.ToolUse.Name, block.ToolUse.Input);
        // Feed result back so Nova can reason about what happened
        messages.Add(new Message { Role = "user", Content = [toolResultBlock] });
    }
    iteration++;
}
```

**The three tools Nova can call:**

| Tool | What It Does | Output |
|---|---|---|
| `quarantine_batch` | Writes a permanent quarantine record | `outputs/quarantine_log.jsonl` |
| `file_work_order` | Creates a structured work order JSON | `outputs/work_orders/WO-*.json` |
| `update_knowledge_graph` | Adds co-occurrence edges, records severity feedback | Updates `knowledge_graph.json` |

---

## A Real Inspection Run

Here is the actual output from inspecting `00041005_test.jpg` (open circuit + pinhole):

```
[1/6] analyze_image_with_vision  (Nova Pro)    ... OK
[2/6] normalize_defect_with_ai   (Nova Lite)   ... OK
[3/6] query_knowledge_graph                    ... OK
[4/6] root_cause_enriched        (Nova Lite)   ... OK
       Root cause: Defects in the etching process (85% confidence)
       Actions: [immediate] Inspect etching machine ← IPC-A-600-2.2
                [short-term] Check etching solution ← IPC-A-600-1.0
                [long-term]  Update maintenance schedule ← IPC-A-600-2.3
[5/6] compliance_with_rag        (Nova Lite)   ... OK
       Disposition: REJECT — IPC-A-600J § 2.2
[P]   policy_checks                            ... DONE
[G]   final_review_gate                        ... PASSED
[7]   agentic_action_loop        (Nova Lite)   ...

  ⚙️  Executing tool: quarantine_batch
     → ✅ Batch 'BATCH-20260303-0CD549' quarantined (severity: high)

  ⚙️  Executing tool: update_knowledge_graph
     → ✅ Knowledge graph updated: open → severity increase

  ⚙️  Executing tool: file_work_order (P1, process_engineer)
     → ✅ WO-20260303-180947-0CD549 filed

  ⚙️  Executing tool: file_work_order (P2, qa_team)
     → ✅ WO-20260303-180947-0CD549 filed

  ⚙️  Executing tool: file_work_order (P3, maintenance)
     → ✅ WO-20260303-180947-0CD549 filed

  ✅ Agentic loop complete — 5 action(s) taken.
```

Nova selected every tool, assigned priority levels (P1/P2/P3) and the correct assignees,
and filed three separate work orders corresponding exactly to the three actions identified
in Step 4 — **with no additional human instruction**.

The entire pipeline ran in approximately **13 seconds**.

---

## The Knowledge Graph: Learning Over Time

One of the most important features is not the tool-calling, but what happens to the knowledge
graph after each inspection. The `update_knowledge_graph` tool records:
- Co-occurrence patterns (which defects appear together)
- Severity feedback (confirmed high severity adjusts future reasoning)
- Root cause confirmations (equipment → defect type associations)

This means the system gets measurably smarter with every inspection. The second open-circuit
board inspected after the first will have graph context populated by the first run —
Nova's Step 4 reasoning references that history, leading to higher-confidence root cause
identification.

The graph starts at 388 nodes / 1308 relationships (from 50 DeepPCB training images) and
grows with every production inspection.

---

## The Business Impact

**What the agent replaces in a real factory:**

| Manual process | Agent action | Time saved |
|---|---|---|
| Quality engineer reads report, decides to quarantine | `quarantine_batch` fires in < 1 sec | 20-60 minutes |
| Engineer emails manufacturing team | P1 work order filed automatically | 30 minutes + follow-up |
| QA manager opens QMS ticket | P2 work order with action pre-filled | 20 minutes |
| Someone updates the process FMEA | Knowledge graph updated automatically | Hours or never |

The current outputs are local files — `quarantine_log.jsonl` and `WO-*.json`. The
architecture is designed so that each file write is a single swap to an HTTP client call:
replace `File.AppendAllTextAsync` in `AgentTools.ExecuteAsync` with a POST to SAP QM,
ServiceNow, or Oracle MES. The decision logic, priority assignment, and tool selection
are all done by Nova.

---

## What Made This Possible with Amazon Nova

**Nova Pro's multimodal capability** was essential for Step 1. Sending a real PCB image
as base64 to the Bedrock Converse API and getting back a structured defect description
(`open circuit in upper section, pinhole in copper pad, confidence 85%`) in a single API
call is powerful. No separate vision service, no intermediate format conversion.

**Nova Lite's tool-use support** via the Converse API was the key to Step 7. The ability
to define tools with JSON schemas, have Nova reason about which tools to call and in what
order, and feed tool results back into the conversation loop — this is what makes the
system genuinely agentic rather than just a pipeline with text outputs.

**Price-performance** mattered too. All reasoning steps (2, 4, 5, 7) use Nova Lite.
Running a complete 7-step inspection including the multi-tool agentic loop keeps costs
low enough to run on every board in a production line, not just a sample.

---

## Try It Yourself

```bash
git clone https://github.com/Ashahet1/AmazonNOVAHackathon
cd AmazonNOVAHackathon

# Add your AWS credentials to appsettings.json
# Enable Nova Pro + Nova Lite in Bedrock Model Access (us-east-1)
# Download DeepPCB dataset to datasets/PCBData/

dotnet run
# Select 1 → press Enter for sample image → watch all 7 steps + agent loop
```

The knowledge graph loads from cache (~388 nodes, instant). The full pipeline including
the agentic tool loop runs in ~13 seconds on a real PCB image.

---

## What's Next

The architecture supports adding new tools without any changes to the agent loop — Nova
discovers available tools from the schema definitions passed at runtime. Near-term
additions could include:

- `notify_shift_supervisor` — Teams/Slack webhook for critical defects
- `trigger_retest_sequence` — API call to test equipment controller
- `update_incoming_inspection` — Flag upstream supplier batch for audit

These would each be ~30 lines of code in `AgentTools.cs`.

The knowledge graph self-improvement loop means the system's accuracy compounds over
time — a property that rule-based inspection systems cannot replicate.

---

## Community Impact

PCB manufacturing quality control is not a solved problem. Defective boards in consumer
electronics, medical devices, and industrial controls cause failures that range from
inconvenient to dangerous. Current inspection is either manual (slow, inconsistent) or
rule-based automated (handles known patterns, misses new failure modes).

This architecture introduces three things traditional systems lack:
1. **Multimodal reasoning** — understands images in context, not just pixel thresholds
2. **Autonomous action** — closes the loop from detection to remediation
3. **Accumulating knowledge** — gets better with every inspection, not just every
   software update

These properties are not PCB-specific. The same pattern — Nova vision + Nova reasoning
+ domain knowledge graph + tool-calling loop — applies to any physical inspection domain:
semiconductor wafer inspection, pharmaceutical tablet QC, food safety visual inspection,
structural crack detection. The `AgentTools.cs` integration point and the knowledge graph
schema are the only components that change.

---

*Built for the Amazon Nova AI Hackathon (March 2026) using Amazon Nova Pro, Amazon Nova Lite,
AWS Bedrock Converse API, .NET 9, and the DeepPCB dataset.*

*#AmazonNova*
