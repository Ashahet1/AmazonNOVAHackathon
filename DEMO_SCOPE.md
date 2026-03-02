# Demo Scope — Beyond Text (Azure Vision + .NET)

This repo demonstrates how Azure Vision can turn images into structured metadata that enables search and automation in Manufacturing / DAM workflows.

## Implemented Today (Ready for Demo)
### Manufacturing Defects Mode (MVTec AD)
- Input: inspection / defect images
- Azure Vision: Image Analysis (caption, tags, objects)
- Output: structured JSON + cross-modal knowledge graph
- Experience: interactive console menu with analytics + exports

## In Progress (Planned in the Same Repo)
### Flowcharts & Technical Diagrams Mode
Goal: extend “beyond text” to engineering visuals (flowcharts/diagrams) by adding:
- OCR extraction (text + bounding regions)
- Lightweight post-processing to infer:
  - steps and decisions (flowcharts)
  - entities and labels (technical diagrams)
- Outputs will follow the same structured JSON pattern used by defects mode

## Non-Goals (Not part of this repo demo)
- Face recognition / identity verification
- Perfect diagram semantics (flowchart graph edges are best-effort)
- Production-grade governance implementation (only demo-level guidance)

## Success Criteria for the Talk
- Show that an image is no longer a “dumb file”
- Show structured output that a C# app can consume
- Show one concrete downstream value: search, insights, or automation trigger
