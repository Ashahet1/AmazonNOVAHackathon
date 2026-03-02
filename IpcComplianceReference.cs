using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  IpcComplianceReference — lightweight, in-process RAG store for
    //  publicly-available IPC standard section metadata.
    //
    //  Instead of letting the LLM hallucinate standard numbers and
    //  sections, we RETRIEVE the relevant sections first and hand them
    //  to the model.  The model may only reference sections that appear
    //  in the retrieved context — fabrication is checked by Guardrails.
    //
    //  Sources:
    //    IPC-A-600J  – Acceptability of Printed Boards (visual criteria)
    //    IPC-6012E   – Qualification & Performance Specification
    //                   for Rigid Printed Boards
    //
    //  These are publicly-available section titles and brief scope
    //  descriptions; full normative text requires an IPC license.
    // ───────────────────────────────────────────────────────────────────

    public static class IpcComplianceReference
    {
        // ─── Immutable section catalogue ─────────────────────────────

        public static readonly List<IpcSection> Sections = new()
        {
            // ═══ IPC-A-600J — Acceptability of Printed Boards ═══

            new IpcSection
            {
                Id          = "IPC-A-600-1.0",
                Standard    = "IPC-A-600J",
                Section     = "1.0",
                Title       = "Scope & General Information",
                Description = "Covers scope, purpose, classification (Class 1/2/3), and general acceptability criteria for printed boards.",
                ApplicableDefects = new() { "open", "short", "mousebite", "spur", "pin_hole", "spurious_copper" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "General Electronic Products  — consumer-grade, limited life",
                    ["Class 2"] = "Dedicated Service Products   — extended life, uninterrupted service desired",
                    ["Class 3"] = "High-Reliability Products    — continued performance critical (medical, aerospace)"
                }
            },

            new IpcSection
            {
                Id          = "IPC-A-600-2.1",
                Standard    = "IPC-A-600J",
                Section     = "2.1",
                Title       = "Board Substrate Conditions",
                Description = "Criteria for base material defects: measling, crazing, delamination, weave texture, and resin conditions.",
                ApplicableDefects = new() { "pin_hole", "spurious_copper" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "Measling/crazing acceptable if not affecting function",
                    ["Class 2"] = "Measling/crazing limited to small areas, no delamination",
                    ["Class 3"] = "No measling/crazing allowed; substrate must be defect-free"
                }
            },

            new IpcSection
            {
                Id          = "IPC-A-600-2.2",
                Standard    = "IPC-A-600J",
                Section     = "2.2",
                Title       = "Conductor / Land Integrity",
                Description = "Acceptability criteria for conductors: minimum width, nicks, scratches, pinholes, edge roughness, and conductor spacing. Covers open circuits, trace irregularities, and conductor edge definitions.",
                ApplicableDefects = new() { "open", "mousebite", "spur", "pin_hole" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "Conductor width reduced up to 30% acceptable; minor nicks OK if no open circuit",
                    ["Class 2"] = "Conductor width reduced up to 20%; nicks must not exceed 20% of width",
                    ["Class 3"] = "Conductor width reduction max 10%; no nicks, scratches or pinholes on conductors"
                }
            },

            new IpcSection
            {
                Id          = "IPC-A-600-2.3",
                Standard    = "IPC-A-600J",
                Section     = "2.3",
                Title       = "Annular Ring & Land Conditions",
                Description = "Criteria for annular ring width, breakout, land lifting, and pad integrity around drilled holes.",
                ApplicableDefects = new() { "open", "pin_hole" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "90° breakout acceptable; annular ring may be reduced",
                    ["Class 2"] = "180° breakout max; minimum annular ring maintained",
                    ["Class 3"] = "No breakout allowed; full annular ring required"
                }
            },

            new IpcSection
            {
                Id          = "IPC-A-600-2.4",
                Standard    = "IPC-A-600J",
                Section     = "2.4",
                Title       = "Conductor Spacing & Foreign Material",
                Description = "Minimum conductor spacing requirements and acceptability of foreign material (metallic particles, flux residue, spurious copper) between conductors.",
                ApplicableDefects = new() { "short", "spur", "spurious_copper" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "Foreign material acceptable if spacing still meets minimum; shorts are reject",
                    ["Class 2"] = "No metallic foreign material reducing spacing below minimum; shorts are reject",
                    ["Class 3"] = "No foreign material allowed between conductors; spacing must exceed minimum by design margin"
                }
            },

            new IpcSection
            {
                Id          = "IPC-A-600-2.5",
                Standard    = "IPC-A-600J",
                Section     = "2.5",
                Title       = "Plating & Coating Conditions",
                Description = "Acceptability of plating voids, pinholes in plating, thickness criteria, and surface finish.",
                ApplicableDefects = new() { "pin_hole", "spurious_copper" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "Plating voids up to 10% of area acceptable",
                    ["Class 2"] = "Plating voids up to 5% of area; no pinholes exposing base material",
                    ["Class 3"] = "No plating voids; uniform thickness required; no pinholes"
                }
            },

            new IpcSection
            {
                Id          = "IPC-A-600-3.1",
                Standard    = "IPC-A-600J",
                Section     = "3.1",
                Title       = "Solder Mask / Resist Conditions",
                Description = "Criteria for solder mask alignment, coverage, adhesion, and damage including lifted or missing resist.",
                ApplicableDefects = new() { "spur", "spurious_copper", "mousebite" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "Misregistration acceptable if no exposed conductor in minimum spacing areas",
                    ["Class 2"] = "Solder mask must cover all conductors; minor misregistration acceptable",
                    ["Class 3"] = "Full coverage required; no misregistration; no adhesion failures"
                }
            },

            // ═══ IPC-6012E — Rigid Printed Board Qualification ═══

            new IpcSection
            {
                Id          = "IPC-6012-3.2",
                Standard    = "IPC-6012E",
                Section     = "3.2",
                Title       = "Visual & Dimensional Requirements",
                Description = "Visual inspection requirements for printed boards including workmanship defects, dimensional tolerances, and surface conditions.",
                ApplicableDefects = new() { "open", "short", "mousebite", "spur", "pin_hole", "spurious_copper" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "Functional; cosmetic defects acceptable",
                    ["Class 2"] = "Workmanship standard applies; defects limited per Table 3-2",
                    ["Class 3"] = "Stringent inspection; defects per Table 3-2 with tighter limits"
                }
            },

            new IpcSection
            {
                Id          = "IPC-6012-3.3",
                Standard    = "IPC-6012E",
                Section     = "3.3",
                Title       = "Conductor Width, Spacing & Integrity",
                Description = "Performance criteria for conductor width (minimum after processing), conductor spacing, opens, shorts, and conductor damage.",
                ApplicableDefects = new() { "open", "short", "mousebite", "spur" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "Min conductor width after processing ≥ 80% of design; no shorts or opens",
                    ["Class 2"] = "Min conductor width after processing ≥ 80% of design; no shorts or opens; nick/pinhole depth ≤ 20%",
                    ["Class 3"] = "Min conductor width after processing ≥ 80% of design; no shorts or opens; nick/pinhole depth ≤ 10%"
                }
            },

            new IpcSection
            {
                Id          = "IPC-6012-3.6",
                Standard    = "IPC-6012E",
                Section     = "3.6",
                Title       = "Electrical Requirements",
                Description = "Continuity (no opens), isolation (no shorts), and electrical performance requirements for all conductor nets.",
                ApplicableDefects = new() { "open", "short" },
                ClassCriteria = new Dictionary<string, string>
                {
                    ["Class 1"] = "100% net-list continuity test required; isolation per applicable spec",
                    ["Class 2"] = "100% continuity and isolation test; resistance thresholds per Table 3-6",
                    ["Class 3"] = "100% continuity + isolation; tighter resistance limits; 100% Hi-pot where specified"
                }
            }
        };

        // ─── Retrieval API ───────────────────────────────────────────

        /// <summary>
        /// Retrieve all IPC sections whose ApplicableDefects list
        /// contains the given defect type (case-insensitive match).
        /// </summary>
        public static List<IpcSection> GetSectionsForDefect(string defectType)
        {
            if (string.IsNullOrWhiteSpace(defectType))
                return new List<IpcSection>();

            return Sections
                .Where(s => s.ApplicableDefects
                    .Any(d => d.Equals(defectType, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        /// <summary>
        /// Retrieve sections matching ANY of the given defect types.
        /// Useful when an image contains multiple co-occurring defects.
        /// </summary>
        public static List<IpcSection> GetSectionsForDefects(IEnumerable<string> defectTypes)
        {
            var types = defectTypes
                .Select(d => d.Trim().ToLowerInvariant())
                .Where(d => !string.IsNullOrEmpty(d))
                .ToHashSet();

            return Sections
                .Where(s => s.ApplicableDefects.Any(d => types.Contains(d.ToLowerInvariant())))
                .ToList();
        }

        /// <summary>
        /// Build a formatted context block that can be injected into
        /// the compliance model's prompt.  The model may ONLY reference
        /// sections present in this block — anything else is fabrication.
        /// </summary>
        public static string BuildComplianceContext(string defectType, string? productClass = null)
        {
            var sections = GetSectionsForDefect(defectType);
            if (!sections.Any())
                return $"No IPC sections on file for defect type '{defectType}'.";

            var sb = new StringBuilder();
            sb.AppendLine("══ RETRIEVED IPC REFERENCE SECTIONS ══");
            sb.AppendLine($"Query: defect_type = \"{defectType}\"");
            sb.AppendLine($"Sections retrieved: {sections.Count}");
            sb.AppendLine();

            foreach (var s in sections)
            {
                sb.AppendLine($"── [{s.Id}] {s.Standard} § {s.Section} — {s.Title} ──");
                sb.AppendLine($"   Scope: {s.Description}");

                if (productClass != null && s.ClassCriteria.ContainsKey(productClass))
                {
                    sb.AppendLine($"   {productClass} criteria: {s.ClassCriteria[productClass]}");
                }
                else
                {
                    foreach (var kv in s.ClassCriteria)
                        sb.AppendLine($"   {kv.Key}: {kv.Value}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("══ END OF RETRIEVED SECTIONS ══");
            sb.AppendLine("You may ONLY cite sections listed above. Do NOT fabricate section numbers or criteria.");
            return sb.ToString();
        }
    }

    // ─── Data model ──────────────────────────────────────────────────

    public class IpcSection
    {
        public string Id { get; set; } = "";
        public string Standard { get; set; } = "";
        public string Section { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> ApplicableDefects { get; set; } = new();
        public Dictionary<string, string> ClassCriteria { get; set; } = new();
    }
}
