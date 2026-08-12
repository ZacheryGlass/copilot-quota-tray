$ErrorActionPreference = "Stop"

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

# Stop the old copy so its files can be replaced.
Get-Process "CopilotQuotaTray" -ErrorAction SilentlyContinue |
    Stop-Process -Force

Start-Sleep -Milliseconds 300

Remove-Item ".\publish" `
    -Recurse `
    -Force `
    -ErrorAction SilentlyContinue

dotnet publish ".\CopilotQuotaTray.csproj" `
    -c Release `
    --self-contained false `
    -p:PublishSingleFile=false `
    -o ".\publish"

Write-Host ""
Write-Host "Built successfully:"
Write-Host "$PWD\publish\CopilotQuotaTray.exe"
