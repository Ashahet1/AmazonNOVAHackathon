using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  OpenAIVisionAnalyzer — uses GPT-4.1 (or GPT-4o) vision to
    //  analyze manufacturing images.  Sends the image as base64 in a
    //  chat completion request and asks for structured JSON output.
    //
    //  This replaces the Azure AI Vision SDK for the MCP pipeline,
    //  giving much richer, manufacturing-aware defect descriptions
    //  instead of generic captions like "a close up of a device".
    // ───────────────────────────────────────────────────────────────────

    public class OpenAIVisionAnalyzer
    {
        private readonly string endpoint;
        private readonly string apiKey;
        private readonly string deployment;
        private readonly string apiVersion;

        public OpenAIVisionAnalyzer()
        {
            endpoint = AppConfig.OpenAIEndpoint;
            apiKey = AppConfig.OpenAIKey;
            deployment = AppConfig.VisionModel;       // uses per-step VisionModel config
            apiVersion = AppConfig.OpenAIApiVersion;
        }

        /// <summary>
        /// Analyze an image using Azure OpenAI GPT-4.1 vision capabilities.
        /// Returns a structured VisionStepResult with caption, tags, objects,
        /// defect information, and confidence.
        /// </summary>
        public async Task<VisionStepResult> AnalyzeImageAsync(string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image file not found.", imagePath);

            // Read image and encode as base64 data URL
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var base64Image = Convert.ToBase64String(imageBytes);
            var extension = Path.GetExtension(imagePath).ToLower().TrimStart('.');

            // Map extension to MIME type
            var mimeType = extension switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "bmp" => "image/bmp",
                "gif" => "image/gif",
                "tif" or "tiff" => "image/tiff",
                "webp" => "image/webp",
                _ => "image/png"
            };

            var dataUrl = $"data:{mimeType};base64,{base64Image}";

            // Build the chat completion request with image content
            var requestBody = new
            {
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = @"You are an expert manufacturing quality inspector with deep knowledge of defect taxonomy, severity classification, and visual inspection standards.

Analyze the provided image of a manufactured part or PCB (Printed Circuit Board) and respond with ONLY valid JSON matching this exact schema:
{
  ""caption"": ""A detailed description of what you see in the image, focusing on any defects or anomalies"",
  ""confidence"": 0.85,
  ""defectAnalysis"": {
    ""defectType"": ""open|short|mousebite|spur|pin_hole|spurious_copper|crack|scratch|hole|bent|contamination|broken|color|misplaced|missing|deformed|good|other"",
    ""severity"": ""low|medium|high"",
    ""description"": ""Specific description of the defect, its location, and characteristics""
  },
  ""tags"": [""tag1"", ""tag2"", ""tag3""],
  ""objects"": [
    { ""name"": ""object_name"", ""confidence"": 0.9 }
  ]
}

PCB Defect Definitions:
- open: broken trace or open circuit on a PCB
- short: unintended copper bridge between traces
- mousebite: irregular nibbling/notch on trace edges
- spur: unwanted copper protrusion from a trace
- pin_hole: tiny hole in copper pad or trace
- spurious_copper: extra copper residue where none should be

Rules:
- confidence: your overall confidence in the analysis (0.0 to 1.0)
- defectType: pick the most specific applicable type from the list; prefer PCB-specific types for circuit board images
- severity: high = structural/functional failure risk (open, short), medium = cosmetic or minor functional (mousebite, spur, spurious_copper), low = barely visible / within tolerance (pin_hole)
- tags: relevant visual/material keywords (e.g., ""pcb"", ""trace"", ""copper"", ""circuit_board"", ""solder"")
- objects: distinct objects or features detected in the image
- Return raw JSON only, no markdown fences, no extra text."
                    },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = $"Analyze this manufacturing part image for defects. File: {Path.GetFileName(imagePath)}" },
                            new { type = "image_url", image_url = new { url = dataUrl, detail = "high" } }
                        }
                    }
                },
                max_tokens = 500,
                temperature = 0.1  // low temperature for consistent structured output
            };

            var json = JsonSerializer.Serialize(requestBody);
            var url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"OpenAI Vision API error {response.StatusCode}: {errorBody}");
            }

            var apiResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(apiResponse);
            var rawOutput = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "{}";

            // Parse the structured response
            return ParseVisionResponse(rawOutput);
        }

        /// <summary>
        /// Extract defect info from a VisionStepResult (parallel to the
        /// AzureVisionAnalyzer.ExtractDefectInfo method signature).
        /// Since GPT-4.1 already provides rich defect classification,
        /// this primarily returns the data it already has.
        /// </summary>
        public (string defectType, string description) ExtractDefectInfo(
            VisionStepResult result, string folderName)
        {
            // GPT-4.1 gives us rich defect classification directly
            // Fall back to folder name if the model said "other" or "good"
            var defectType = result.DefectType;
            if (string.IsNullOrEmpty(defectType) ||
                defectType.Equals("good", StringComparison.OrdinalIgnoreCase) ||
                defectType.Equals("other", StringComparison.OrdinalIgnoreCase))
            {
                defectType = folderName;
            }

            return (defectType, result.Caption);
        }

        private VisionStepResult ParseVisionResponse(string rawJson)
        {
            // Strip markdown fences if present
            var cleaned = rawJson.Trim();
            if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                if (firstNewline > 0)
                    cleaned = cleaned[(firstNewline + 1)..];
                if (cleaned.EndsWith("```"))
                    cleaned = cleaned[..^3];
                cleaned = cleaned.Trim();
            }

            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;

                var result = new VisionStepResult
                {
                    Caption = GetStringProp(root, "caption", "Image analyzed"),
                    Confidence = GetDoubleProp(root, "confidence", 0.5),
                    Tags = new List<string>(),
                    Objects = new List<DetectedObjectInfo>()
                };

                // Parse defect analysis
                if (root.TryGetProperty("defectAnalysis", out var defectEl))
                {
                    result.DefectType = GetStringProp(defectEl, "defectType", "");
                    result.DefectSeverity = GetStringProp(defectEl, "severity", "medium");
                    result.DefectDescription = GetStringProp(defectEl, "description", "");
                }

                // Parse tags
                if (root.TryGetProperty("tags", out var tagsEl) &&
                    tagsEl.ValueKind == JsonValueKind.Array)
                {
                    result.Tags = tagsEl.EnumerateArray()
                        .Select(t => t.GetString() ?? "")
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList();
                }

                // Parse objects
                if (root.TryGetProperty("objects", out var objsEl) &&
                    objsEl.ValueKind == JsonValueKind.Array)
                {
                    result.Objects = objsEl.EnumerateArray()
                        .Select(o => new DetectedObjectInfo
                        {
                            Name = GetStringProp(o, "name", "unknown"),
                            Confidence = GetDoubleProp(o, "confidence", 0.5)
                        })
                        .ToList();
                }

                return result;
            }
            catch (JsonException)
            {
                // If JSON parsing fails, return a basic result from raw text
                return new VisionStepResult
                {
                    Caption = rawJson.Length > 200 ? rawJson[..200] : rawJson,
                    Confidence = 0.3,
                    Tags = new List<string>(),
                    Objects = new List<DetectedObjectInfo>()
                };
            }
        }

        private static string GetStringProp(JsonElement el, string name, string defaultVal)
        {
            return el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString() ?? defaultVal
                : defaultVal;
        }

        private static double GetDoubleProp(JsonElement el, string name, double defaultVal)
        {
            if (el.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetDouble();
            }
            return defaultVal;
        }
    }
}
