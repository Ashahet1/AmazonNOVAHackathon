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
        // ── Amazon Nova / AWS Bedrock ──
        public static string AwsRegion { get; }              // e.g. "us-east-1"
        public static string AwsAccessKeyId { get; }         // leave blank to use IAM role / env chain
        public static string AwsSecretAccessKey { get; }
        // Nova model IDs — use cross-region inference prefix (us.) for latest models
        public static string NovaVisionModel { get; }        // Step 1 — multimodal  (default: us.amazon.nova-pro-v1:0)
        public static string NovaReasoningModel { get; }     // Step 4 — reasoning   (default: us.amazon.nova-lite-v1:0)
        public static string NovaComplianceModel { get; }    // Step 5 — compliance  (default: us.amazon.nova-lite-v1:0)
        public static string NovaAgentModel { get; }         // Step 7 — tool use    (v1 only — v2 cross-region profile does not support ToolConfig)

        static AppConfig()
        {
            // Locate appsettings.json relative to the executing assembly
            string baseDir = AppContext.BaseDirectory;
            string settingsPath = Path.Combine(baseDir, "appsettings.json");

            // Fallback: also look in the current working directory (useful when running from project root)
            if (!File.Exists(settingsPath))
                settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

            // Amazon Nova defaults
            string awsRegion = "us-east-1";
            string awsAccessKeyId = "";
            string awsSecretAccessKey = "";
            string novaVisionModel = "us.amazon.nova-pro-v1:0";
            string novaReasoningModel = "us.amazon.nova-lite-v1:0";
            string novaComplianceModel = "us.amazon.nova-lite-v1:0";
            string novaAgentModel = "us.amazon.nova-lite-v1:0";

            if (File.Exists(settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(settingsPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("AmazonNova", out var nova))
                    {
                        awsRegion = nova.TryGetProperty("AwsRegion", out var rgn) ? rgn.GetString() ?? awsRegion : awsRegion;
                        awsAccessKeyId = nova.TryGetProperty("AwsAccessKeyId", out var ak) ? ak.GetString() ?? "" : "";
                        awsSecretAccessKey = nova.TryGetProperty("AwsSecretAccessKey", out var sk) ? sk.GetString() ?? "" : "";
                        novaVisionModel = nova.TryGetProperty("NovaVisionModel", out var nvm) ? nvm.GetString() ?? novaVisionModel : novaVisionModel;
                        novaReasoningModel = nova.TryGetProperty("NovaReasoningModel", out var nrm) ? nrm.GetString() ?? novaReasoningModel : novaReasoningModel;
                        novaComplianceModel = nova.TryGetProperty("NovaComplianceModel", out var ncm) ? ncm.GetString() ?? novaComplianceModel : novaComplianceModel;
                        novaAgentModel = nova.TryGetProperty("NovaAgentModel", out var nam) ? nam.GetString() ?? novaAgentModel : novaAgentModel;
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

            // Amazon Nova / Bedrock
            AwsRegion = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? awsRegion;
            AwsAccessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? awsAccessKeyId;
            AwsSecretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? awsSecretAccessKey;
            NovaVisionModel = Environment.GetEnvironmentVariable("NOVA_VISION_MODEL") ?? novaVisionModel;
            NovaReasoningModel = Environment.GetEnvironmentVariable("NOVA_REASONING_MODEL") ?? novaReasoningModel;
            NovaComplianceModel = Environment.GetEnvironmentVariable("NOVA_COMPLIANCE_MODEL") ?? novaComplianceModel;
            NovaAgentModel = Environment.GetEnvironmentVariable("NOVA_AGENT_MODEL") ?? novaAgentModel;
        }
    }
}
