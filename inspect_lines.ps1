$f = "c:\Users\rishah\OneDrive\Pictures\Desktop\MicrosoftML.NET\AzureAITalk--main\AzureAITalk--main\DashboardApiServer.cs"
$lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
for ($i = 344; $i -le 360; $i++) {
    Write-Host ("{0}: [{1}]" -f ($i + 1), $lines[$i])
}
