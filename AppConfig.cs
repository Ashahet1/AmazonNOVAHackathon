using System;
using System.IO;
using System.Text.Json;

namespace ManufacturingKnowledgeGraph
{
    /// <summary>
    /// Centralized configuration reader.
    /// Loads values from appsettings.json (next to the executable) and
    /// allows environment-variable overrides for CI / production use.
    /// </summary>
    public static class AppConfig
    {
        // ── Azure AI Vision ──
        public static string VisionEndpoint { get; }
        public static string VisionKey { get; }

        // ── Azure OpenAI ──
        public static string OpenAIEndpoint { get; }
        public static string OpenAIKey { get; }
        public static string OpenAIDeployment { get; }
        public static string OpenAIApiVersion { get; }

        // ── Per-step model deployments ──
        // Each pipeline step can target a different deployment.
        // Defaults fall back to the general OpenAIDeployment.
        public static string VisionModel { get; }           // Step 1 — image analysis (default: gpt-4.1)
        public static string ClassificationModel { get; }   // Step 2 — normalize / equipment inference (default: gpt-4.1-nano)
        public static string ReasoningModel { get; }        // Step 4 — root-cause reasoning (default: o4-mini)
        public static string ComplianceModel { get; }       // Step 5 — IPC compliance (default: gpt-4.1-mini)

        static AppConfig()
        {
            // Locate appsettings.json relative to the executing assembly
            string baseDir = AppContext.BaseDirectory;
            string settingsPath = Path.Combine(baseDir, "appsettings.json");

            // Fallback: also look in the current working directory (useful when running from project root)
            if (!File.Exists(settingsPath))
                settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

            string visionEndpoint = "";
            string visionKey = "";
            string openAIEndpoint = "";
            string openAIKey = "";
            string openAIDeployment = "gpt-4.1";
            string openAIApiVersion = "2025-01-01-preview";
            string visionModel = "";
            string classificationModel = "";
            string reasoningModel = "";
            string complianceModel = "";

            if (File.Exists(settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("AzureVision", out var vision))
                    {
                        visionEndpoint = vision.TryGetProperty("Endpoint", out var ep) ? ep.GetString() ?? "" : "";
                        visionKey = vision.TryGetProperty("Key", out var k) ? k.GetString() ?? "" : "";
                    }

                    if (root.TryGetProperty("AzureOpenAI", out var openai))
                    {
                        var rawEndpoint = openai.TryGetProperty("Endpoint", out var ep) ? ep.GetString() ?? "" : "";
                        // If user pasted the full URL (with /openai/deployments/... path), strip to base only
                        if (rawEndpoint.Contains("/openai/deployments/"))
                            rawEndpoint = rawEndpoint[..rawEndpoint.IndexOf("/openai/deployments/")];
                        openAIEndpoint = rawEndpoint.TrimEnd('/');
                        openAIKey = openai.TryGetProperty("Key", out var k) ? k.GetString() ?? "" : "";
                        openAIDeployment = openai.TryGetProperty("DeploymentName", out var d) ? d.GetString() ?? openAIDeployment : openAIDeployment;
                        openAIApiVersion = openai.TryGetProperty("ApiVersion", out var v) ? v.GetString() ?? openAIApiVersion : openAIApiVersion;

                        // Per-step model overrides (optional — fall back to OpenAIDeployment)
                        visionModel = openai.TryGetProperty("VisionModel", out var vm) ? vm.GetString() ?? "" : "";
                        classificationModel = openai.TryGetProperty("ClassificationModel", out var cm) ? cm.GetString() ?? "" : "";
                        reasoningModel = openai.TryGetProperty("ReasoningModel", out var rm) ? rm.GetString() ?? "" : "";
                        complianceModel = openai.TryGetProperty("ComplianceModel", out var cpm) ? cpm.GetString() ?? "" : "";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Warning: Could not parse appsettings.json – {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("⚠️ Warning: appsettings.json not found. Using environment variables only.");
            }

            // Environment variables override the JSON values (if set)
            VisionEndpoint = Environment.GetEnvironmentVariable("VISION_ENDPOINT") ?? visionEndpoint;
            VisionKey = Environment.GetEnvironmentVariable("VISION_KEY") ?? visionKey;
            OpenAIEndpoint = (Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? openAIEndpoint).TrimEnd('/');
            OpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? openAIKey;
            OpenAIDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? openAIDeployment;
            OpenAIApiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? openAIApiVersion;

            // Per-step models: env var → appsettings → sensible default → general deployment
            VisionModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_VISION_MODEL")
                ?? (string.IsNullOrEmpty(visionModel) ? OpenAIDeployment : visionModel);
            ClassificationModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_CLASSIFICATION_MODEL")
                ?? (string.IsNullOrEmpty(classificationModel) ? OpenAIDeployment : classificationModel);
            ReasoningModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_REASONING_MODEL")
                ?? (string.IsNullOrEmpty(reasoningModel) ? OpenAIDeployment : reasoningModel);
            ComplianceModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_COMPLIANCE_MODEL")
                ?? (string.IsNullOrEmpty(complianceModel) ? OpenAIDeployment : complianceModel);
        }
    }
}
