$f = "c:\Users\rishah\OneDrive\Pictures\Desktop\MicrosoftML.NET\AzureAITalk--main\AzureAITalk--main\DashboardApiServer.cs"
$bytes = [System.IO.File]::ReadAllBytes($f)
$text  = [System.Text.Encoding]::UTF8.GetString($bytes)

# Fix 1: broken Console.WriteLine with embedded newline before "/api/insights: Nova responded successfully"
$text = [System.Text.RegularExpressions.Regex]::Replace(
    $text,
    '(?s)Console\.WriteLine\("  [^\r\n"]*\r?\n\s*/api/insights: Nova responded successfully[^\r\n]*\r?\n\s*;',
    'Console.WriteLine("  [OK] /api/insights: Nova responded successfully.");'
)

# Fix 2: broken Console.WriteLine with embedded newline before "/api/insights: Nova call failed"
$text = [System.Text.RegularExpressions.Regex]::Replace(
    $text,
    '(?s)Console\.WriteLine\(\$"  [^\r\n"]*\r?\n\s*/api/insights: Nova call failed[^\r\n]*\r?\n\s*;',
    'Console.WriteLine($"  [WARN] /api/insights: Nova call failed - {ex.Message}");'
)

[System.IO.File]::WriteAllText($f, $text, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Fix applied."

# Verify
$i = $text.IndexOf("Nova responded")
$snippet = $text.Substring([Math]::Max(0,$i-30), 80)
Write-Host "After fix: $snippet"
