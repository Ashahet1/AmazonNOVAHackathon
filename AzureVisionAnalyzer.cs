using Azure;
using Azure.AI.Vision.ImageAnalysis;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ManufacturingKnowledgeGraph
{
    public class AzureVisionAnalyzer
    {
        private readonly ImageAnalysisClient client;

        public AzureVisionAnalyzer(string endpoint, string key)
        {
            client = new ImageAnalysisClient(
                new Uri(endpoint),
                new AzureKeyCredential(key)
            );
        }

        public async Task<ImageAnalysisResult> AnalyzeImageAsync(string imagePath)
        {
            using var stream = File.OpenRead(imagePath);
            var imageData = BinaryData.FromStream(stream);

            var result = await client.AnalyzeAsync(
                imageData,
                VisualFeatures.Caption | 
                VisualFeatures.Tags | 
                VisualFeatures.Objects
            );

            return result.Value;
        }

        public (string defectType, string description) ExtractDefectInfo(ImageAnalysisResult result, string folderName)
        {
            var caption = result.Caption?.Text ?? "unknown";
            var tags = result.Tags?.Values;

            // Use folder name as primary defect type (MVTec structure)
            string defectType = folderName;
            
            // Try to enhance with AI detection
            if (caption.Contains("crack") || tags?.Any(t => t.Name.Contains("crack")) == true)
                defectType = "crack";
            else if (caption.Contains("scratch") || tags?.Any(t => t.Name.Contains("scratch")) == true)
                defectType = "scratch";
            else if (caption.Contains("hole") || caption.Contains("missing"))
                defectType = "hole";
            else if (caption.Contains("bent") || caption.Contains("deformed"))
                defectType = "bent";
            else if (caption.Contains("contamination") || caption.Contains("dirty"))
                defectType = "contamination";

            return (defectType, caption);
        }
    }
}