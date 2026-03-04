$f = "c:\Users\rishah\OneDrive\Pictures\Desktop\MicrosoftML.NET\AzureAITalk--main\AzureAITalk--main\DashboardApiServer.cs"
$lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)

# Find start of HandleStats method
$startIdx = -1
$endIdx   = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'private async Task HandleStats') { $startIdx = $i; break }
}

if ($startIdx -lt 0) { Write-Error "HandleStats not found"; exit 1 }

# Find end: the next "private async Task" or "private List" after startIdx
for ($i = $startIdx + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s+// .* GET /api/(images|batch)' -or
        $lines[$i] -match 'private (async Task|List<) Handle' -or
        $lines[$i] -match 'private List<ImageEntry>') {
        $endIdx = $i - 1; break
    }
}

if ($endIdx -lt 0) { Write-Error "Could not find end of HandleStats"; exit 1 }

Write-Host "HandleStats: lines $($startIdx+1) to $($endIdx+1)"

$newBody = @'
        private async Task HandleStats(HttpListenerResponse res)
        {
            // Uses the EXACT same KnowledgeGraph methods as ShowCompleteDashboard (Program.cs case "3")
            var defects   = _graph.GetNodesByType("defect");
            var images    = _graph.GetNodesByType("image");
            var equipment = _graph.GetNodesByType("equipment");
            var standards = _graph.GetNodesByType("standard");

            // If graph is empty, return zeros with a helpful flag
            if (defects.Count == 0 && images.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  [WARN] /api/stats: Knowledge graph is empty.");
                Console.ResetColor();
                await WriteJson(res, new
                {
                    ok = false, empty = true,
                    message = "Knowledge graph is empty. At startup choose option 1 (load from cache) or option 2 (rebuild from dataset).",
                    totalNodes = 0, totalEdges = 0, totalImages = 0, totalDefects = 0,
                    totalEquipment = 0, totalStandards = 0, totalProducts = 0,
                    severityHigh = 0, severityMedium = 0, severityLow = 0, crossProductPatterns = 0,
                    byCat = Array.Empty<object>(), byProduct = Array.Empty<object>(),
                    equipmentHubs = Array.Empty<object>(), insights = Array.Empty<string>()
                });
                return;
            }

            // Same calls as ShowCompleteDashboard
            var defectFreq    = _graph.GetDefectFrequency();
            var severityDist  = _graph.GetSeverityDistribution();
            var productCounts = _graph.GetProductDefectCounts();
            var equipUsage    = _graph.GetEquipmentUsage();
            var similarities  = _graph.FindSimilarDefectsAcrossProducts();
            var insights      = _graph.GenerateInsights();
            var (heatProds, _, heatData) = _graph.GetQualityHeatmap();
            var allRels       = _graph.AllRelationships;

            var byCat = defectFreq
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new { category = kv.Key, defects = kv.Value })
                .ToList();

            var byProduct = productCounts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new { product = kv.Key, count = kv.Value })
                .ToList();

            var equipmentHubs = equipUsage
                .Select(kv => new { name = kv.Key, edges = kv.Value })
                .ToList();

            int sevHigh   = severityDist.Where(kv => kv.Key.StartsWith("High",   StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
            int sevMedium = severityDist.Where(kv => kv.Key.StartsWith("Medium", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
            int sevLow    = severityDist.Where(kv => kv.Key.StartsWith("Low",    StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);

            var heatmap = Enumerable.Range(0, heatProds.Length).Select(i => new
            {
                product = heatProds[i],
                low     = heatData[i, 0],
                medium  = heatData[i, 1],
                high    = heatData[i, 2],
                total   = heatData[i, 0] + heatData[i, 1] + heatData[i, 2],
            }).ToList();

            await WriteJson(res, new
            {
                ok = true, empty = false,
                totalNodes           = images.Count + defects.Count + equipment.Count + standards.Count,
                totalEdges           = allRels.Count,
                totalImages          = images.Count,
                totalDefects         = defects.Count,
                totalEquipment       = equipment.Count,
                totalStandards       = standards.Count,
                totalProducts        = byProduct.Count,
                avgDefectsPerProduct = byProduct.Count > 0 ? Math.Round((double)defects.Count / byProduct.Count, 1) : 0,
                severityHigh         = sevHigh,
                severityMedium       = sevMedium,
                severityLow          = sevLow,
                crossProductPatterns = similarities.Count,
                mostCommonDefect     = byCat.FirstOrDefault()?.category ?? "",
                mostCommonCount      = byCat.FirstOrDefault()?.defects  ?? 0,
                byCat,
                byProduct,
                equipmentHubs,
                heatmap,
                insights,
            });

            await Task.CompletedTask;
        }
'@

# Split into lines
$newLines = $newBody -split "`r?`n"

# Build new file: before + new body + after
$before = $lines[0..($startIdx-1)]
$after  = $lines[($endIdx+1)..($lines.Count-1)]

$result = [System.Collections.Generic.List[string]]::new()
$result.AddRange([string[]]$before)
$result.AddRange([string[]]$newLines)
$result.AddRange([string[]]$after)

[System.IO.File]::WriteAllLines($f, $result, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Done. New file has $($result.Count) lines."
