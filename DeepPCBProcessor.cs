using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  DeepPCBProcessor — ingests the DeepPCB dataset and builds a
    //  knowledge graph from annotated PCB defect images.
    //
    //  Dataset: https://github.com/tangsanli5201/DeepPCB
    //  Structure:
    //    DeepPCB/PCBData/
    //      group{XXXXX}/
    //        {YYYYY}/
    //          {YYYYY}_temp.jpg   — defect-free template
    //          {YYYYY}_test.jpg   — tested image with defects
    //          {YYYYY}.txt        — annotations (one line per defect)
    //
    //  Annotation format:  x1 y1 x2 y2 defect_class
    //    0 = open           — broken trace / open circuit
    //    1 = short          — unintended copper bridge
    //    2 = mousebite      — irregular edge nibbling
    //    3 = spur           — unwanted copper protrusion
    //    4 = pin_hole       — tiny hole in copper pad/trace
    //    5 = spurious_copper — extra copper where there shouldn't be
    // ───────────────────────────────────────────────────────────────────

    public class DeepPCBProcessor
    {
        /// <summary>DeepPCB defect class ID → human-readable name</summary>
        public static readonly Dictionary<int, string> DefectClassNames = new()
        {
            [0] = "open",
            [1] = "short",
            [2] = "mousebite",
            [3] = "spur",
            [4] = "pin_hole",
            [5] = "spurious_copper"
        };

        /// <summary>DeepPCB defect → severity mapping for PCB manufacturing</summary>
        public static readonly Dictionary<string, string> DefectSeverity = new()
        {
            ["open"]            = "high",     // broken trace = circuit failure
            ["short"]           = "high",     // unintended bridge = short circuit
            ["mousebite"]       = "medium",   // weakened trace edge
            ["spur"]            = "medium",   // protrusion, risk of short under stress
            ["pin_hole"]        = "low",      // cosmetic, rarely affects function
            ["spurious_copper"] = "medium"    // extra copper, may cause bridging
        };

        private readonly KnowledgeGraph graph;

        public DeepPCBProcessor(KnowledgeGraph graph)
        {
            this.graph = graph;
        }

        // ═══════════════════════════════════════════════════════════════
        //  PROCESS DATASET
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Walk the DeepPCB folder tree, parse annotations, and build
        /// knowledge graph nodes + relationships.
        /// </summary>
        public async Task ProcessDataset(string deepPcbRoot, int maxImages = 50)
        {
            Console.WriteLine("🔍 Processing DeepPCB Dataset...\n");

            if (!Directory.Exists(deepPcbRoot))
            {
                Console.WriteLine($"❌ Error: Directory not found: {deepPcbRoot}");
                return;
            }

            // Find annotation files — they follow the pattern {id}.txt
            var annotationFiles = FindAnnotationFiles(deepPcbRoot)
                .Take(maxImages)
                .ToList();

            Console.WriteLine($"  Found {annotationFiles.Count} annotated image pair(s) (max {maxImages})\n");

            int processed = 0;
            int totalDefects = 0;

            foreach (var annoFile in annotationFiles)
            {
                try
                {
                    var parsed = ParseAnnotationFile(annoFile);
                    if (parsed == null || parsed.Defects.Count == 0)
                    {
                        Console.Write(".");
                        continue;
                    }

                    // Corresponding test image
                    var testImagePath = parsed.TestImagePath;
                    if (!File.Exists(testImagePath))
                    {
                        Console.Write("?");
                        continue;
                    }

                    // Create image node
                    var imageId = $"img_pcb_{parsed.ImageId}";
                    var imageNode = new Node
                    {
                        Id = imageId,
                        Type = "image",
                        Properties = new()
                        {
                            ["path"] = testImagePath,
                            ["product"] = "pcb",
                            ["defect_category"] = string.Join(",", parsed.Defects.Select(d => d.ClassName).Distinct()),
                            ["caption"] = $"PCB test image with {parsed.Defects.Count} defect(s): {string.Join(", ", parsed.Defects.Select(d => d.ClassName).Distinct())}",
                            ["template_path"] = parsed.TemplatePath,
                            ["defect_count"] = parsed.Defects.Count
                        }
                    };
                    graph.AddNode(imageNode);

                    // Create defect nodes
                    foreach (var defect in parsed.Defects)
                    {
                        var defectNode = new Node
                        {
                            Id = $"defect_pcb_{parsed.ImageId}_{defect.ClassName}_{totalDefects}",
                            Type = "defect",
                            Properties = new()
                            {
                                ["name"]      = defect.ClassName,
                                ["product"]   = defect.ClassName,  // enables category queries (open/short/etc)
                                ["pcb_group"] = "pcb",
                                ["severity"]  = DefectSeverity.GetValueOrDefault(defect.ClassName, "medium"),
                                ["bbox_x1"]   = defect.X1,
                                ["bbox_y1"]   = defect.Y1,
                                ["bbox_x2"]   = defect.X2,
                                ["bbox_y2"]   = defect.Y2,
                                ["class_id"]  = defect.ClassId
                            }
                        };
                        graph.AddNode(defectNode);

                        graph.AddRelationship(new Relationship
                        {
                            FromNodeId = imageId,
                            ToNodeId = defectNode.Id,
                            RelationType = "has_defect",
                            Confidence = 1.0  // ground-truth annotations
                        });

                        totalDefects++;
                    }

                    processed++;
                    Console.Write(processed % 10 == 0 ? $"[{processed}]" : ".");
                }
                catch (Exception ex)
                {
                    Console.Write($"E({ex.Message[..Math.Min(20, ex.Message.Length)]})");
                }
            }

            Console.WriteLine($"\n\n  Processed: {processed} images, {totalDefects} defects\n");

            // Add PCB domain knowledge
            AddPCBDomainKnowledge();

            Console.WriteLine("✅ DeepPCB processing complete!\n");
            await Task.CompletedTask;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ANNOTATION PARSING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Find all annotation .txt files in the DeepPCB tree.
        /// Handles both the nested group structure and flat layouts.
        /// </summary>
        private List<string> FindAnnotationFiles(string root)
        {
            var files = new List<string>();

            // Pattern 1: DeepPCB/PCBData/group{X}/{Y}_not/{Z}.txt   ← this distribution
            // Pattern 2: DeepPCB/PCBData/group{X}/{Y}/{Z}.txt        ← standard distribution
            var pcbDataDir = Path.Combine(root, "PCBData");
            var searchRoot = Directory.Exists(pcbDataDir) ? pcbDataDir : root;

            files.AddRange(Directory.GetFiles(searchRoot, "*.txt", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    // Exclude known index/list files
                    return !name.Equals("readme.txt", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("license.txt", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("test.txt", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("trainval.txt", StringComparison.OrdinalIgnoreCase);
                }));

            return files.OrderBy(f => f).ToList();
        }

        /// <summary>
        /// Parse a DeepPCB annotation file.
        /// Each line: x1 y1 x2 y2 class_id
        /// </summary>
        private ParsedAnnotation? ParseAnnotationFile(string annoPath)
        {
            var annoDir = Path.GetDirectoryName(annoPath) ?? "";
            var baseName = Path.GetFileNameWithoutExtension(annoPath);

            // Resolve the image directory.
            // This distribution:  group{X}/00041_not/00041000.txt  →  images in group{X}/00041/
            // Standard distribution: group{X}/00041/00041000.txt   →  images in same folder
            var annoFolderName = Path.GetFileName(annoDir);
            string imageDir;
            if (annoFolderName.EndsWith("_not", StringComparison.OrdinalIgnoreCase))
            {
                // Strip "_not" suffix to get the sibling image folder
                var stem = annoFolderName[..^4];  // remove last 4 chars ("_not")
                imageDir = Path.Combine(Path.GetDirectoryName(annoDir) ?? "", stem);
            }
            else
            {
                imageDir = annoDir;  // standard: annotations alongside images
            }

            var testImage  = FindImage(imageDir, baseName, "_test");
            var tempImage  = FindImage(imageDir, baseName, "_temp");
            if (testImage == null) return null;

            var lines = File.ReadAllLines(annoPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            var defects = new List<DefectAnnotation>();

            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 &&
                    int.TryParse(parts[0], out var x1) &&
                    int.TryParse(parts[1], out var y1) &&
                    int.TryParse(parts[2], out var x2) &&
                    int.TryParse(parts[3], out var y2) &&
                    int.TryParse(parts[4], out var classId) &&
                    classId >= 0 && classId <= 5)
                {
                    defects.Add(new DefectAnnotation
                    {
                        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                        ClassId = classId,
                        ClassName = DefectClassNames.GetValueOrDefault(classId, "unknown")
                    });
                }
            }

            return new ParsedAnnotation
            {
                ImageId = baseName,
                TestImagePath = testImage,
                TemplatePath = tempImage ?? "",
                Defects = defects
            };
        }

        private string? FindImage(string dir, string baseName, string suffix)
        {
            // Try suffixed name first (standard DeepPCB: _test / _temp)
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
            {
                var path = Path.Combine(dir, baseName + suffix + ext);
                if (File.Exists(path)) return path;
            }

            // Alternate suffix conventions used in some DeepPCB distributions:
            //   _test  → (no suffix)    e.g. 00041.jpg
            //   _temp  → _not           e.g. 00041_not.jpg
            var altSuffix = suffix switch
            {
                "_test" => "",      // bare filename = defect image
                "_temp" => "_not",  // _not = reference/good image
                _ => null
            };

            if (altSuffix != null)
            {
                foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
                {
                    var path = Path.Combine(dir, baseName + altSuffix + ext);
                    if (File.Exists(path)) return path;
                }
            }

            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  PCB DOMAIN KNOWLEDGE
        // ═══════════════════════════════════════════════════════════════

        private void AddPCBDomainKnowledge()
        {
            Console.WriteLine("📚 Adding PCB domain knowledge (equipment, standards, rules)...\n");

            // ── Equipment nodes (PCB-specific) ──
            var equipmentData = new[]
            {
                ("eq_aoi", "Automated Optical Inspection (AOI) system"),
                ("eq_microscope", "High-resolution microscope"),
                ("eq_xray", "X-ray inspection system"),
                ("eq_ict", "In-Circuit Test (ICT) probe"),
                ("eq_flying_probe", "Flying probe tester"),
                ("eq_spi", "Solder Paste Inspection (SPI)"),
                ("eq_camera", "High-speed line-scan camera")
            };

            foreach (var (id, name) in equipmentData)
            {
                graph.AddNode(new Node
                {
                    Id = id,
                    Type = "equipment",
                    Properties = new() { ["name"] = name }
                });
            }

            // ── Standard nodes (PCB-relevant) ──
            var standards = new[]
            {
                ("standard_ipc_a_600", "IPC-A-600", "Acceptability of Printed Boards"),
                ("standard_ipc_6012", "IPC-6012", "Qualification and Performance Specification for Rigid PCBs"),
                ("standard_iso9001",  "ISO 9001", "8.5 - Production and service provision"),
                ("standard_ipc_2221", "IPC-2221", "Generic Standard on Printed Board Design")
            };

            foreach (var (id, name, section) in standards)
            {
                graph.AddNode(new Node
                {
                    Id = id,
                    Type = "standard",
                    Properties = new() { ["name"] = name, ["section"] = section }
                });
            }

            // ── Defect → Equipment mappings ──
            var defectNodes = graph.GetNodesByType("defect");
            foreach (var defect in defectNodes)
            {
                var defectName = defect.Properties["name"]?.ToString()?.ToLower() ?? "";

                // Open / Short → electrical test + AOI
                if (defectName == "open" || defectName == "short")
                {
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "eq_ict",
                        RelationType = "requires_equipment", Confidence = 0.95
                    });
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "eq_aoi",
                        RelationType = "requires_equipment", Confidence = 0.90
                    });
                    // IPC-A-600 covers open/short acceptance criteria
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "standard_ipc_a_600",
                        RelationType = "governed_by", Confidence = 0.95
                    });
                }

                // Mousebite / Spur → AOI + microscope
                if (defectName == "mousebite" || defectName == "spur")
                {
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "eq_aoi",
                        RelationType = "requires_equipment", Confidence = 0.90
                    });
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "eq_microscope",
                        RelationType = "requires_equipment", Confidence = 0.85
                    });
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "standard_ipc_a_600",
                        RelationType = "governed_by", Confidence = 0.90
                    });
                }

                // Pin hole → X-ray + microscope
                if (defectName == "pin_hole")
                {
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "eq_xray",
                        RelationType = "requires_equipment", Confidence = 0.85
                    });
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "eq_microscope",
                        RelationType = "requires_equipment", Confidence = 0.80
                    });
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "standard_ipc_6012",
                        RelationType = "governed_by", Confidence = 0.85
                    });
                }

                // Spurious copper → AOI + flying probe
                if (defectName == "spurious_copper")
                {
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "eq_aoi",
                        RelationType = "requires_equipment", Confidence = 0.90
                    });
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "eq_flying_probe",
                        RelationType = "requires_equipment", Confidence = 0.80
                    });
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id, ToNodeId = "standard_ipc_a_600",
                        RelationType = "governed_by", Confidence = 0.88
                    });
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPER: Get dataset summary
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Scan a DeepPCB root and return a quick summary without processing.
        /// </summary>
        public static DatasetSummary ScanDataset(string deepPcbRoot)
        {
            var summary = new DatasetSummary();

            if (!Directory.Exists(deepPcbRoot))
                return summary;

            var pcbDataDir = Path.Combine(deepPcbRoot, "PCBData");
            var searchRoot = Directory.Exists(pcbDataDir) ? pcbDataDir : deepPcbRoot;

            var txtFiles = Directory.GetFiles(searchRoot, "*.txt", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).Equals("readme.txt", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).Equals("license.txt", StringComparison.OrdinalIgnoreCase))
                .ToList();

            summary.TotalAnnotationFiles = txtFiles.Count;

            // Quick defect count from first 100 files
            int sampleCount = 0;
            foreach (var f in txtFiles.Take(100))
            {
                try
                {
                    var lines = File.ReadAllLines(f).Where(l => !string.IsNullOrWhiteSpace(l));
                    foreach (var line in lines)
                    {
                        var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5 && int.TryParse(parts[4], out var cls) && cls >= 0 && cls <= 5)
                        {
                            var name = DefectClassNames.GetValueOrDefault(cls, "unknown");
                            summary.DefectCounts[name] = summary.DefectCounts.GetValueOrDefault(name, 0) + 1;
                            sampleCount++;
                        }
                    }
                }
                catch { /* skip bad files */ }
            }

            summary.SampledDefects = sampleCount;
            return summary;
        }

        // ── Data classes ──

        private class ParsedAnnotation
        {
            public string ImageId { get; set; } = "";
            public string TestImagePath { get; set; } = "";
            public string TemplatePath { get; set; } = "";
            public List<DefectAnnotation> Defects { get; set; } = new();
        }

        private class DefectAnnotation
        {
            public int X1 { get; set; }
            public int Y1 { get; set; }
            public int X2 { get; set; }
            public int Y2 { get; set; }
            public int ClassId { get; set; }
            public string ClassName { get; set; } = "";
        }
    }

    public class DatasetSummary
    {
        public int TotalAnnotationFiles { get; set; }
        public int SampledDefects { get; set; }
        public Dictionary<string, int> DefectCounts { get; set; } = new();
    }
}
