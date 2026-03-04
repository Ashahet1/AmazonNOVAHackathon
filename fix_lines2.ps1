$f = "c:\Users\rishah\OneDrive\Pictures\Desktop\MicrosoftML.NET\AzureAITalk--main\AzureAITalk--main\DashboardApiServer.cs"
$lines = [System.Collections.Generic.List[string]]([System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8))

# Line at index 347 (line number 348) contains an embedded \n inside a string literal.
# Split it into parts, then reconstruct as a single clean line.
$badLine = $lines[347]
if ($badLine -match "Console\.WriteLine\(" -and $badLine -match "`n") {
    # Collapse embedded newline(s) and join parts
    $fixed = $badLine -replace "`r?`n\s*", " "
    # Now replace the broken emoji+path with clean text
    $fixed = $fixed -replace 'Console\.WriteLine\("  [^ ]+ /api/insights: Nova responded successfully', 'Console.WriteLine("  [OK] /api/insights: Nova responded successfully'
    $lines[347] = $fixed
    Write-Host "Fixed line 348:"
    Write-Host $lines[347]
} else {
    Write-Host "Line 348 looks OK or no embedded newline found."
    Write-Host $badLine
}

[System.IO.File]::WriteAllLines($f, $lines, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Written."
