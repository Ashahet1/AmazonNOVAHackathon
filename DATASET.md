# Datasets for the Beyond Text Demo

This repo uses small dataset samples to keep the live demo reproducible.

## 1) Manufacturing Defects (Implemented)
### MVTec Anomaly Detection (MVTec AD)
- Use case: manufacturing inspection / defect images
- Source: https://www.mvtec.com/company/research/datasets/mvtec-ad/downloads
- Notes: dataset is typically used for research/education; follow MVTec license terms.

**Local folder expectation (example):**
data/mvtec_anomaly_detection/

## 2) Flowcharts (In Progress — same repo)
Goal: use flowchart images to demonstrate OCR + step/decision extraction.

**Planned sample location:**
data/flowcharts_sample/

**How samples will be used:**
- OCR extracts text and text regions
- Post-processing classifies nodes (process vs decision)
- Output becomes structured JSON (steps/decisions)

## 3) Technical Diagrams (In Progress — same repo)
Goal: use diagram images to demonstrate OCR + entity extraction.

**Planned sample location:**
data/diagrams_sample/

**How samples will be used:**
- OCR extracts labels and text regions
- Post-processing normalizes technical terms (dictionary/synonyms)
- Output becomes structured JSON (entities/labels)

## Recommended demo practice
To keep the repository lightweight, store only a small sample set (10–20 images per mode),
or provide a script that downloads the sample images into the `data/` folder.

## License note
Before committing any dataset images into the repo:
- verify redistribution is allowed
- otherwise commit only a download script + instructions
