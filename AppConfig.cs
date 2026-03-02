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

        // ── Amazon Nova / AWS Bedrock ──
        public static string AwsRegion { get; }              // e.g. "us-east-1"
        public static string AwsAccessKeyId { get; }         // leave blank to use IAM role / env chain
        public static string AwsSecretAccessKey { get; }
        // Nova model IDs — use cross-region inference prefix (us.) for latest models
        public static string NovaVisionModel { get; }        // Step 1 — multimodal  (default: us.amazon.nova-pro-v1:0)
        public static string NovaReasoningModel { get; }     // Step 4 — reasoning   (default: us.amazon.nova-lite-v1:0)
        public static string NovaComplianceModel { get; }    // Step 5 — compliance  (default: us.amazon.nova-lite-v1:0)

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

            // Amazon Nova defaults
            string awsRegion = "us-east-1";
            string awsAccessKeyId = "";
            string awsSecretAccessKey = "";
            string novaVisionModel = "us.amazon.nova-pro-v1:0";
            string novaReasoningModel = "us.amazon.nova-lite-v1:0";
            string novaComplianceModel = "us.amazon.nova-lite-v1:0";

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

                    if (root.TryGetProperty("AmazonNova", out var nova))
                    {
                        awsRegion = nova.TryGetProperty("AwsRegion", out var rgn) ? rgn.GetString() ?? awsRegion : awsRegion;
                        awsAccessKeyId = nova.TryGetProperty("AwsAccessKeyId", out var ak) ? ak.GetString() ?? "" : "";
                        awsSecretAccessKey = nova.TryGetProperty("AwsSecretAccessKey", out var sk) ? sk.GetString() ?? "" : "";
                        novaVisionModel = nova.TryGetProperty("NovaVisionModel", out var nvm) ? nvm.GetString() ?? novaVisionModel : novaVisionModel;
                        novaReasoningModel = nova.TryGetProperty("NovaReasoningModel", out var nrm) ? nrm.GetString() ?? novaReasoningModel : novaReasoningModel;
                        novaComplianceModel = nova.TryGetProperty("NovaComplianceModel", out var ncm) ? ncm.GetString() ?? novaComplianceModel : novaComplianceModel;
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

            // Amazon Nova / Bedrock
            AwsRegion = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? awsRegion;
            AwsAccessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? awsAccessKeyId;
            AwsSecretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? awsSecretAccessKey;
            NovaVisionModel = Environment.GetEnvironmentVariable("NOVA_VISION_MODEL") ?? novaVisionModel;
            NovaReasoningModel = Environment.GetEnvironmentVariable("NOVA_REASONING_MODEL") ?? novaReasoningModel;
            NovaComplianceModel = Environment.GetEnvironmentVariable("NOVA_COMPLIANCE_MODEL") ?? novaComplianceModel;
        }
    }
}
