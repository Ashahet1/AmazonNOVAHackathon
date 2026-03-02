using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ManufacturingKnowledgeGraph
{
    // ───────────────────────────────────────────────────────────────────
    //  Guardrails — Responsible AI / Safety-by-design layer
    //
    //  Three tiers:
    //    1) Structured-output validation  (hard guardrail)
    //    2) Policy checks                 (soft guardrail)
    //    3) Human-in-the-loop triggers    (escalation)
    //
    //  Every decision is recorded in the CaseFile trace.
    // ───────────────────────────────────────────────────────────────────

    public static class Guardrails
    {
        // ── Configuration ──
        public const double VisionConfidenceThreshold = 0.40;
        public const int MaxSchemaRepairAttempts = 1;

        // ── Patterns for sensitive data detection ──
        private static readonly Regex[] SensitivePatterns = new[]
        {
            new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled),                     // email
            new Regex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", RegexOptions.Compiled),                                               // phone
            new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled),                                                       // SSN
            new Regex(@"(?i)(password|secret|api[_-]?key|token|credential)\s*[:=]\s*\S+", RegexOptions.Compiled),              // secrets
            new Regex(@"(?i)(sk-|Bearer\s+)[A-Za-z0-9+/=_-]{20,}", RegexOptions.Compiled),                                    // API keys
        };

        // ── Disallowed content patterns ──
        private static readonly string[] DisallowedPhrases = new[]
        {
            "fabricated", "I made this up", "hypothetical defect",
            "assume the following", "let's pretend"
        };

        // ═══════════════════════════════════════════════════════════════
        //  1) STRUCTURED-OUTPUT VALIDATION (hard guardrail)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Validate that a JSON string can be deserialized to <typeparamref name="T"/>.
        /// Returns (success, parsed object or null, error message).
        /// </summary>
        public static (bool Valid, T? Result, string? Error) ValidateJsonSchema<T>(string json) where T : class
        {
            try
            {
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var result = JsonSerializer.Deserialize<T>(json, opts);
                if (result == null)
                    return (false, null, "Deserialization returned null");
                return (true, result, null);
            }
            catch (JsonException ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Attempt to validate JSON; if invalid, try a simple repair (strip markdown fences, etc.)
        /// and re-validate.  If still invalid, return a safe-template fallback and flag human review.
        /// </summary>
        public static (T Result, bool UsedFallback) ValidateOrRepair<T>(
            string rawJson, CaseFile caseFile, string toolName) where T : class, new()
        {
            // First attempt
            var (valid, result, error) = ValidateJsonSchema<T>(rawJson);
            if (valid && result != null)
            {
                caseFile.AddTrace(toolName, "schema_valid", "Output passed JSON schema validation");
                return (result, false);
            }

            caseFile.AddTrace(toolName, "schema_invalid", $"Validation error: {error}");

            // Repair attempt: strip markdown code fences
            var repaired = rawJson.Trim();
            if (repaired.StartsWith("```"))
            {
                var firstNewline = repaired.IndexOf('\n');
                if (firstNewline > 0)
                    repaired = repaired[(firstNewline + 1)..];
                if (repaired.EndsWith("```"))
                    repaired = repaired[..^3];
                repaired = repaired.Trim();
            }

            var (valid2, result2, error2) = ValidateJsonSchema<T>(repaired);
            if (valid2 && result2 != null)
            {
                caseFile.AddTrace(toolName, "schema_repaired", "Output repaired (stripped markdown fences) and now valid");
                return (result2, false);
            }

            // Fallback: safe template
            caseFile.AddTrace(toolName, "schema_fallback",
                $"Repair failed ({error2}). Using safe template. Human review required.");
            caseFile.FlagForHumanReview($"Schema validation failed for {toolName} after repair attempt");

            return (new T(), true);
        }

        // ═══════════════════════════════════════════════════════════════
        //  2) POLICY CHECKS (soft guardrail)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Scan text for sensitive data and disallowed content.
        /// Returns the (possibly redacted) text and a list of violations.
        /// </summary>
        public static (string CleanText, List<PolicyViolation> Violations) RunPolicyChecks(
            string text, CaseFile caseFile)
        {
            var violations = new List<PolicyViolation>();
            var cleanText = text;

            // ── Sensitive-data scan ──
            foreach (var pattern in SensitivePatterns)
            {
                var matches = pattern.Matches(cleanText);
                foreach (Match m in matches)
                {
                    var v = new PolicyViolation
                    {
                        Rule = "sensitive_data_detected",
                        Detail = $"Pattern matched: {pattern} → \"{m.Value[..Math.Min(8, m.Value.Length)]}...\"",
                        Action = "redacted"
                    };
                    violations.Add(v);
                    cleanText = cleanText.Replace(m.Value, "[REDACTED]");
                }
            }

            // ── Disallowed content ──
            foreach (var phrase in DisallowedPhrases)
            {
                if (cleanText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new PolicyViolation
                    {
                        Rule = "disallowed_content",
                        Detail = $"Disallowed phrase detected: \"{phrase}\"",
                        Action = "flagged"
                    });
                }
            }

            // ── No-fabrication: outputs must reference retrieved context IDs ──
            // (checked separately via CheckTraceability)

            // Record
            if (violations.Any())
            {
                caseFile.PolicyViolations.AddRange(violations);
                caseFile.AddTrace("policy_check", "violations_found",
                    $"{violations.Count} violation(s): {string.Join("; ", violations.Select(v => v.Rule))}");
            }
            else
            {
                caseFile.AddTrace("policy_check", "clean", "No policy violations detected");
            }

            return (cleanText, violations);
        }

        /// <summary>
        /// Traceability check: verify that reasoning outputs reference at least one
        /// retrieved context ID (graph node / ISO snippet).
        /// </summary>
        public static bool CheckTraceability(List<string> referencedIds, CaseFile caseFile, string toolName)
        {
            if (referencedIds == null || !referencedIds.Any())
            {
                caseFile.AddTrace(toolName, "traceability_fail",
                    "Output does not reference any retrieved context IDs — potential fabrication");
                caseFile.FlagForHumanReview($"{toolName}: no context IDs referenced (fabrication risk)");
                return false;
            }

            caseFile.AddTrace(toolName, "traceability_pass",
                $"Output references {referencedIds.Count} context ID(s)");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  3) HUMAN-IN-THE-LOOP TRIGGERS (escalation)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Check whether vision confidence is below the safe threshold.
        /// </summary>
        public static void CheckVisionConfidence(double confidence, CaseFile caseFile)
        {
            if (confidence < VisionConfidenceThreshold)
            {
                caseFile.FlagForHumanReview(
                    $"Vision confidence {confidence:P1} is below threshold {VisionConfidenceThreshold:P1}");
                caseFile.AddTrace("confidence_check", "below_threshold",
                    $"Confidence {confidence:P1} < {VisionConfidenceThreshold:P1}");
            }
            else
            {
                caseFile.AddTrace("confidence_check", "above_threshold",
                    $"Confidence {confidence:P1} >= {VisionConfidenceThreshold:P1}");
            }
        }

        /// <summary>
        /// Final gate: if any policy violations raised risk flags, flag for QE sign-off.
        /// </summary>
        public static void FinalReviewGate(CaseFile caseFile)
        {
            var riskViolations = caseFile.PolicyViolations
                .Where(v => v.Action == "flagged" || v.Action == "blocked")
                .ToList();

            if (riskViolations.Any())
            {
                caseFile.FlagForHumanReview(
                    $"Policy check raised {riskViolations.Count} risk flag(s)");
            }

            // Mark final status
            if (!caseFile.HumanReviewRequired)
                caseFile.Status = CaseStatus.Completed;

            caseFile.AddTrace("final_gate", caseFile.Status.ToString(),
                caseFile.HumanReviewRequired
                    ? $"Case flagged: {string.Join("; ", caseFile.HumanReviewReasons)}"
                    : "Case passed all guardrails");
        }
    }
}
