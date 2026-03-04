$f = "c:\Users\rishah\OneDrive\Pictures\Desktop\MicrosoftML.NET\AzureAITalk--main\AzureAITalk--main\DashboardApiServer.cs"
$lines = [System.Collections.Generic.List[string]]([System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8))
$changed = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "Nova responded successfully") {
        $lines[$i] = '                Console.WriteLine("  [OK] /api/insights: Nova responded successfully.");'
        Write-Host "Fixed line $($i+1): Nova responded"
        $changed++
    }
    if ($lines[$i] -match "Nova call failed") {
        $lines[$i] = '                Console.WriteLine($"  [WARN] /api/insights: Nova call failed - {ex.Message}");'
        Write-Host "Fixed line $($i+1): Nova call failed"
        $changed++
    }
}
Write-Host "Total fixes: $changed"
[System.IO.File]::WriteAllLines($f, $lines, (New-Object System.Text.UTF8Encoding $false))
Write-Host "File written."
