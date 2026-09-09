$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$taskNugetPackages = Join-Path $env:USERPROFILE '.nuget\packages'
$restoreOptions = @('--ignore-failed-sources', '-p:NuGetAudit=false')
if (Test-Path -LiteralPath $taskNugetPackages) { $restoreOptions += "-p:RestorePackagesPath=$taskNugetPackages" }
dotnet restore .\Sky1stCharacterStudio.csproj @restoreOptions
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
dotnet publish .\Sky1stCharacterStudio.csproj --configuration Release --self-contained false --no-restore @restoreOptions `
    /p:PublishSingleFile=false `
    --output .\dist-summon
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Copy-Item -LiteralPath .\README.md -Destination .\dist-summon\README.md -Force
Write-Host "Published to $PSScriptRoot\dist-summon"
