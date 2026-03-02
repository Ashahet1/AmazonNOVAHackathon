using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ManufacturingKnowledgeGraph
{
    public class GraphBuilder
    {
        private readonly KnowledgeGraph graph;
        private readonly AzureVisionAnalyzer analyzer;

        public GraphBuilder(KnowledgeGraph graph, AzureVisionAnalyzer analyzer)
        {
            this.graph = graph;
            this.analyzer = analyzer;
        }

        /// <summary>
        /// Process the DeepPCB dataset using the dedicated processor.
        /// This does NOT require Azure AI Vision — it reads annotations directly.
        /// </summary>
        public async Task ProcessDeepPCBDataset(string deepPcbPath, int maxImages = 50)
        {
            var processor = new DeepPCBProcessor(graph);
            await processor.ProcessDataset(deepPcbPath, maxImages);
        }

        public async Task ProcessMVTecDataset(string mvtecPath, int maxImagesPerProduct = 3)
        {
            Console.WriteLine("🔍 Processing MVTec Dataset...\n");

            if (!Directory.Exists(mvtecPath))
            {
                Console.WriteLine($"❌ Error: Directory not found: {mvtecPath}");
                return;
            }

            var productFolders = Directory.GetDirectories(mvtecPath);
            Console.WriteLine($"Found {productFolders.Length} product categories\n");

            foreach (var productFolder in productFolders)
            {
                var productName = Path.GetFileName(productFolder);
                Console.WriteLine($"📦 Processing product: {productName}");

                // Look for defect images in test folder
                var testPath = Path.Combine(productFolder, "test");
                if (!Directory.Exists(testPath))
                {
                    Console.WriteLine($"   ⚠️  No test folder found, skipping...\n");
                    continue;
                }

                var defectFolders = Directory.GetDirectories(testPath)
                    .Where(d => !d.EndsWith("good")); // Skip non-defect images

                foreach (var defectFolder in defectFolders)
                {
                    var defectCategory = Path.GetFileName(defectFolder);
                    var images = Directory.GetFiles(defectFolder, "*.png")
                        .Take(maxImagesPerProduct);

                    int count = 0;
                    foreach (var imagePath in images)
                    {
                        try
                        {
                            await ProcessSingleImage(imagePath, productName, defectCategory);
                            count++;
                            Console.Write($".");
                            
                            // Rate limiting for free tier (20 calls/min)
                            await Task.Delay(3500); // ~17 calls/min to be safe
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\n⚠️  Error processing {Path.GetFileName(imagePath)}: {ex.Message}");
                        }
                    }
                    Console.WriteLine($" ({count} images)");
                }
                Console.WriteLine();
            }

            // Add knowledge rules
            AddDomainKnowledge();
            
            Console.WriteLine("✅ Processing complete!\n");
        }

        private async Task ProcessSingleImage(string imagePath, string productName, string defectCategory)
        {
            // Analyze with Azure Vision
            var result = await analyzer.AnalyzeImageAsync(imagePath);
            var (defectType, description) = analyzer.ExtractDefectInfo(result, defectCategory);

            // Create image node
            var imageNode = new Node
            {
                Id = $"img_{productName}_{Path.GetFileNameWithoutExtension(imagePath)}",
                Type = "image",
                Properties = new()
                {
                    ["path"] = imagePath,
                    ["product"] = productName,
                    ["defect_category"] = defectCategory,
                    ["caption"] = description
                }
            };
            graph.AddNode(imageNode);

            // Create defect node
            var defectNode = new Node
            {
                Id = $"defect_{productName}_{defectType}_{Guid.NewGuid().ToString().Substring(0, 8)}",
                Type = "defect",
                Properties = new()
                {
                    ["name"] = defectType,
                    ["product"] = productName,
                    ["severity"] = DetermineSeverity(defectCategory)
                }
            };
            graph.AddNode(defectNode);

            // Create relationship
            graph.AddRelationship(new Relationship
            {
                FromNodeId = imageNode.Id,
                ToNodeId = defectNode.Id,
                RelationType = "has_defect",
                Confidence = 0.85
            });
        }

        private void AddDomainKnowledge()
        {
            Console.WriteLine("📚 Adding domain knowledge (equipment, standards)...\n");
            
            // Add equipment nodes
            var equipmentData = new[]
            {
                ("eq_microscope", "High-resolution microscope"),
                ("eq_backlight", "Backlight illumination"),
                ("eq_xray", "X-ray scanner"),
                ("eq_camera", "High-speed camera"),
                ("eq_laser", "3D Laser scanner")
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

            // Add standards nodes
            graph.AddNode(new Node
            {
                Id = "standard_iso9001",
                Type = "standard",
                Properties = new() { ["name"] = "ISO 9001", ["section"] = "8.5 - Production and service provision" }
            });

            // Link defects to equipment (simplified rules)
            var defectNodes = graph.GetNodesByType("defect");
            foreach (var defect in defectNodes)
            {
                var defectName = defect.Properties["name"].ToString().ToLower();
                
                if (defectName.Contains("crack") || defectName.Contains("scratch"))
                {
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id,
                        ToNodeId = "eq_microscope",
                        RelationType = "requires_equipment",
                        Confidence = 0.9
                    });
                }
                
                if (defectName.Contains("hole") || defectName.Contains("contamination") || defectName.Contains("broken"))
                {
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id,
                        ToNodeId = "eq_backlight",
                        RelationType = "requires_equipment",
                        Confidence = 0.85
                    });
                }
                
                if (defectName.Contains("bent") || defectName.Contains("color"))
                {
                    graph.AddRelationship(new Relationship
                    {
                        FromNodeId = defect.Id,
                        ToNodeId = "eq_camera",
                        RelationType = "requires_equipment",
                        Confidence = 0.8
                    });
                }
            }
        }

        private string DetermineSeverity(string defectCategory)
        {
            defectCategory = defectCategory.ToLower();
            
            if (defectCategory.Contains("large") || defectCategory.Contains("severe"))
                return "high";
            else if (defectCategory.Contains("small") || defectCategory.Contains("minor"))
                return "low";
            else
                return "medium";
        }
    }
}