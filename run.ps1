$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run --project .\SkyCharacterStudio.csproj --configuration Debug
