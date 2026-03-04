$f = "c:\Users\rishah\OneDrive\Pictures\Desktop\MicrosoftML.NET\AzureAITalk--main\AzureAITalk--main\DashboardApiServer.cs"
$lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
Write-Host "Total lines: $($lines.Count)"
for ($i = 344; $i -le 355; $i++) {
    $l = $lines[$i]
    Write-Host ("Line {0} len={1}: {2}" -f ($i+1), $l.Length, ($l -replace '[^\x20-\x7E]','?'))
}
