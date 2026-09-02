param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

Push-Location $PSScriptRoot
try {
    dotnet restore
    dotnet publish -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o "publish/$Runtime"
    Write-Host "Published to: $PSScriptRoot/publish/$Runtime"
}
finally {
    Pop-Location
}
