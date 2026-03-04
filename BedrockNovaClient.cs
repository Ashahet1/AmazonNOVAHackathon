using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  BedrockNovaClient — Amazon Bedrock client for Amazon Nova models.
    //
    //  Supports calls used in the MCP inspection pipeline:
    //    Step 1  — AnalyzeImageAsync  (Nova Pro / Nova Lite, multimodal)
    //    Step 4  — InvokeTextAsync    (Nova Lite, root-cause reasoning)
    //    Step 5  — InvokeTextAsync    (Nova Lite, IPC-A-610 compliance)
    //
    //  Authentication (in priority order):
    //    1. Explicit keys from appsettings.json / env vars
    //    2. AWS default credentials chain (IAM role, ~/.aws/credentials, env)
    //
    //  Model IDs (cross-region inference — us. prefix):
    //    us.amazon.nova-pro-v1:0   — multimodal vision + text (Step 1)
    //    us.amazon.nova-lite-v1:0  — fast text reasoning (Steps 4 & 5)
    //
    //  Amazon Nova 2 (reasoning) ID when available:
    //    us.amazon.nova-lite-v2:0  — set via NovaReasoningModel in config
    // ───────────────────────────────────────────────────────────────────

    public class BedrockNovaClient
    {
        private readonly AmazonBedrockRuntimeClient _bedrock;
        private readonly string _visionModelId;
        private readonly string _reasoningModelId;
        private readonly string _complianceModelId;
        private readonly string _agentModelId;   // v1 only — tool use via ToolConfig

        public BedrockNovaClient()
        {
            var region = RegionEndpoint.GetBySystemName(AppConfig.AwsRegion);

            // Use explicit credentials if provided; otherwise fall through to SDK credential chain
            AWSCredentials? credentials = null;
            if (!string.IsNullOrWhiteSpace(AppConfig.AwsAccessKeyId) &&
                !string.IsNullOrWhiteSpace(AppConfig.AwsSecretAccessKey))
            {
                credentials = new BasicAWSCredentials(AppConfig.AwsAccessKeyId, AppConfig.AwsSecretAccessKey);
            }

            _bedrock = credentials != null
                ? new AmazonBedrockRuntimeClient(credentials, region)
                : new AmazonBedrockRuntimeClient(region);  // uses default credential chain

            _visionModelId    = AppConfig.NovaVisionModel;
            _reasoningModelId = AppConfig.NovaReasoningModel;
            _complianceModelId = AppConfig.NovaComplianceModel;
            _agentModelId     = AppConfig.NovaAgentModel;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Step 1 — Multimodal vision analysis (Nova Pro)
        //  Sends image + prompt to Amazon Nova via Bedrock Converse API.
        //  Returns a VisionStepResult with the same shape as the standard path.
        // ═══════════════════════════════════════════════════════════════

        public async Task<VisionStepResult> AnalyzeImageAsync(string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image file not found.", imagePath);

            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var ext = Path.GetExtension(imagePath).ToLower().TrimStart('.');
            var fmt = ext switch
            {
                "jpg" or "jpeg" => ImageFormat.Jpeg,
                "png"           => ImageFormat.Png,
                "gif"           => ImageFormat.Gif,
                "webp"          => ImageFormat.Webp,
                _               => ImageFormat.Jpeg
            };

            const string systemPrompt = @"You are an expert manufacturing quality inspector with deep knowledge of
PCB defect taxonomy, severity classification, and visual inspection standards.

Analyse the provided PCB image and respond with ONLY valid JSON matching this exact schema:
{
  ""caption"": ""Detailed description focusing on defects or anomalies"",
  ""confidence"": 0.85,
  ""defectAnalysis"": {
    ""defectType"": ""open|short|mousebite|spur|pin_hole|spurious_copper|good|other"",
    ""severity"": ""low|medium|high"",
    ""description"": ""Specific description of the defect, location, and characteristics""
  },
  ""tags"": [""tag1"", ""tag2""],
  ""objects"": [{ ""name"": ""object_name"", ""confidence"": 0.9 }]
}

PCB defect definitions:
  open            - broken trace / open circuit
  short           - unintended copper bridge between traces
  mousebite       - irregular nibbling/notch on trace edges
  spur            - unwanted copper protrusion from a trace
  pin_hole        - tiny hole in copper pad or trace
  spurious_copper - extra copper residue where none should be

Rules: Return raw JSON only, no markdown, no extra text.";

            var request = new ConverseRequest
            {
                ModelId = _visionModelId,
                System = new List<SystemContentBlock>
                {
                    new SystemContentBlock { Text = systemPrompt }
                },
                Messages = new List<Message>
                {
                    new Message
                    {
                        Role = ConversationRole.User,
                        Content = new List<ContentBlock>
                        {
                            new ContentBlock
                            {
                                Image = new ImageBlock
                                {
                                    Format = fmt,
                                    Source = new ImageSource { Bytes = new MemoryStream(imageBytes) }
                                }
                            },
                            new ContentBlock
                            {
                                Text = $"Analyse this PCB image for manufacturing defects. File: {Path.GetFileName(imagePath)}"
                            }
                        }
                    }
                },
                InferenceConfig = new InferenceConfiguration
                {
                    MaxTokens = 600,
                    Temperature = 0.1f
                }
            };

            try
            {
                var response = await _bedrock.ConverseAsync(request);
                var rawJson = response.Output.Message.Content[0].Text ?? "{}";
                return ParseVisionResponse(rawJson);
            }
            catch (Exception ex)
            {
                return new VisionStepResult
                {
                    Caption = $"Nova vision error: {ex.Message}",
                    Confidence = 0.0,
                    Tags = new System.Collections.Generic.List<string>(),
                    Objects = new System.Collections.Generic.List<DetectedObjectInfo>()
                };
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Steps 4 & 5 — Text reasoning / compliance  (Nova Lite)
        //  Mirrors the signature of McpOrchestrator.CallAIModel so it
        //  can be used as a drop-in replacement.
        // ═══════════════════════════════════════════════════════════════

        public async Task<string?> InvokeTextAsync(
            string systemPrompt,
            string userPrompt,
            bool isReasoningStep,       // true = Step 4 (reasoning), false = Step 5 (compliance)
            string tool,
            CaseFile? caseFile = null)
        {
            var modelId = isReasoningStep ? _reasoningModelId : _complianceModelId;

            var request = new ConverseRequest
            {
                ModelId = modelId,
                System = new List<SystemContentBlock>
                {
                    new SystemContentBlock { Text = systemPrompt }
                },
                Messages = new List<Message>
                {
                    new Message
                    {
                        Role = ConversationRole.User,
                        Content = new List<ContentBlock>
                        {
                            new ContentBlock { Text = userPrompt }
                        }
                    }
                },
                InferenceConfig = new InferenceConfiguration
                {
                    MaxTokens = 1200,
                    Temperature = 0.15f
                }
            };

            try
            {
                var response = await _bedrock.ConverseAsync(request);
                var text = response.Output.Message.Content[0].Text;
                caseFile?.AddTrace(tool, "nova_success", $"[{modelId}] Tokens used: {response.Usage?.TotalTokens}");
                return text;
            }
            catch (Exception ex)
            {
                caseFile?.AddTrace(tool, "nova_error", $"[{modelId}] {ex.Message}");
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Step 7 — Agentic loop with tool calling  (Nova Lite)
        //
        //  Sends a ConverseRequest with ToolConfig to Nova.
        //  When Nova responds with stopReason == "tool_use", the caller's
        //  executor delegate is invoked for each tool call.  The result is
        //  fed back as a ToolResult and the conversation resumes.
        //  Loops until Nova produces "end_turn" or maxIterations is reached.
        // ═══════════════════════════════════════════════════════════════

        public async Task<string> InvokeAgentLoopAsync(
            string systemPrompt,
            string userPrompt,
            List<Amazon.BedrockRuntime.Model.Tool> tools,
            Func<string, Amazon.Runtime.Documents.Document, Task<string>> toolExecutor,
            int maxIterations = 5)
        {
            var messages = new List<Amazon.BedrockRuntime.Model.Message>
            {
                new Amazon.BedrockRuntime.Model.Message
                {
                    Role    = ConversationRole.User,
                    Content = new List<ContentBlock> { new ContentBlock { Text = userPrompt } }
                }
            };

            string finalText = "";

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                var request = new ConverseRequest
                {
                    ModelId    = _agentModelId,
                    System     = new List<SystemContentBlock> { new SystemContentBlock { Text = systemPrompt } },
                    Messages   = messages,
                    ToolConfig = new Amazon.BedrockRuntime.Model.ToolConfiguration { Tools = tools },
                    InferenceConfig = new InferenceConfiguration { MaxTokens = 1500, Temperature = 0.1f }
                };

                var response = await _bedrock.ConverseAsync(request);
                var assistantMessage = response.Output.Message;

                // Always add assistant turn to history
                messages.Add(assistantMessage);

                // Capture any text in this turn
                foreach (var block in assistantMessage.Content)
                    if (block.Text != null) finalText = block.Text;

                // If Nova is done reasoning — exit loop
                if (response.StopReason == "end_turn" || response.StopReason == "max_tokens")
                    break;

                // If Nova wants to call tools
                if (response.StopReason == "tool_use")
                {
                    var toolResultBlocks = new List<ContentBlock>();

                    foreach (var block in assistantMessage.Content)
                    {
                        if (block.ToolUse == null) continue;

                        var toolResult = await toolExecutor(block.ToolUse.Name, block.ToolUse.Input);

                        toolResultBlocks.Add(new ContentBlock
                        {
                            ToolResult = new Amazon.BedrockRuntime.Model.ToolResultBlock
                            {
                                ToolUseId = block.ToolUse.ToolUseId,
                                Content   = new List<Amazon.BedrockRuntime.Model.ToolResultContentBlock>
                                {
                                    new Amazon.BedrockRuntime.Model.ToolResultContentBlock { Text = toolResult }
                                }
                            }
                        });
                    }

                    // Feed tool results back to Nova
                    messages.Add(new Amazon.BedrockRuntime.Model.Message
                    {
                        Role    = ConversationRole.User,
                        Content = toolResultBlocks
                    });
                }
            }

            return finalText;
        }

        // ═══════════════════════════════════════════════════════════════
        //  JSON parser (identical shape to OpenAIVisionAnalyzer)
        // ═══════════════════════════════════════════════════════════════

        private static VisionStepResult ParseVisionResponse(string rawJson)
        {
            var cleaned = rawJson.Trim();
            if (cleaned.StartsWith("```"))
            {
                var nl = cleaned.IndexOf('\n');
                if (nl > 0) cleaned = cleaned[(nl + 1)..];
                if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
                cleaned = cleaned.Trim();
            }

            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;

                var result = new VisionStepResult
                {
                    Caption    = GetStr(root, "caption", "Image analyzed"),
                    Confidence = GetDbl(root, "confidence", 0.5),
                    Tags       = new System.Collections.Generic.List<string>(),
                    Objects    = new System.Collections.Generic.List<DetectedObjectInfo>()
                };

                if (root.TryGetProperty("defectAnalysis", out var da))
                {
                    result.DefectType        = GetStr(da, "defectType", "");
                    result.DefectSeverity    = GetStr(da, "severity", "medium");
                    result.DefectDescription = GetStr(da, "description", "");
                }

                if (root.TryGetProperty("tags", out var tagsEl) &&
                    tagsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tagsEl.EnumerateArray())
                    {
                        var s = t.GetString();
                        if (!string.IsNullOrEmpty(s)) result.Tags.Add(s);
                    }
                }

                if (root.TryGetProperty("objects", out var objs) &&
                    objs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in objs.EnumerateArray())
                    {
                        result.Objects.Add(new DetectedObjectInfo
                        {
                            Name       = GetStr(o, "name", "unknown"),
                            Confidence = GetDbl(o, "confidence", 0.5)
                        });
                    }
                }

                return result;
            }
            catch
            {
                return new VisionStepResult
                {
                    Caption    = rawJson.Length > 200 ? rawJson[..200] : rawJson,
                    Confidence = 0.3,
                    Tags       = new System.Collections.Generic.List<string>(),
                    Objects    = new System.Collections.Generic.List<DetectedObjectInfo>()
                };
            }
        }

        private static string GetStr(JsonElement el, string name, string def) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() ?? def : def;

        private static double GetDbl(JsonElement el, string name, double def)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number)
                return p.GetDouble();
            return def;
        }
    }
}
