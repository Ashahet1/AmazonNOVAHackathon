# Limits & Gotchas (and how we handle them)

This demo uses Azure Vision for image analysis and OCR. Results depend heavily on input quality and diagram structure.

## 1) Image quality & format
**Issue:** low-resolution scans, glare, skew, compression reduce accuracy  
**Mitigation:** export higher DPI, crop to relevant region, straighten/clean image

## 2) Diagrams are not “natively semantic”
**Issue:** vision extracts text/regions but not full flowchart logic automatically  
**Mitigation:** lightweight post-processing (detect boxes/arrows, map connections)

## 3) OCR edge cases
**Issue:** small fonts, rotated text, handwriting, dense symbols can fail  
**Mitigation:** zoom/crop regions, use consistent templates, prefer clean exports

## 4) Ambiguous arrows and crossings
**Issue:** tight layouts and crossing lines confuse “what connects to what”  
**Mitigation:** cleaner diagram exports + heuristics (arrowhead detection)

## 5) Domain vocabulary
**Issue:** abbreviations/acronyms may be misread or mistagged  
**Mitigation:** small domain dictionary + synonym map + ERP/PIM term validation

## 6) Governance & compliance
**Issue:** sensitive data in images requires controls  
**Mitigation:** access control, logging, redaction, policy checks before sharing
