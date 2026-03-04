$file = "c:\Users\rishah\OneDrive\Pictures\Desktop\MicrosoftML.NET\AzureAITalk--main\AzureAITalk--main\DashboardApiServer.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

$cleaned = $lines | ForEach-Object {
    $line = $_
    # Replace box-drawing / arrow garbage sequences
    $line = $line -replace '\x{e2}\x{94}[\x{80}-\x{bf}]', '-'
    # Replace every non-ASCII character with a safe substitute
    $line = [System.Text.RegularExpressions.Regex]::Replace($line, '[^\x00-\x7F]+', {
        param($m)
        $s = $m.Value
        # Common patterns
        if ($s -match 'â{2,}') { return '---' }
        if ($s -match '^â$') { return '->' }
        return ''
    })
    $line
}

[System.IO.File]::WriteAllLines($file, $cleaned, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Done. Verifying..."
$remaining = $cleaned | Where-Object { $_ -match '[^\x00-\x7F]' }
if ($remaining) {
    Write-Host "Still has non-ASCII:"
    $remaining | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host "SUCCESS: DashboardApiServer.cs is now all plain ASCII."
}
