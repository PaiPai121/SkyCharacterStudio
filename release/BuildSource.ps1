$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$taskPackage=Split-Path -Parent $PSScriptRoot
Copy-Item (Join-Path $taskPackage 'tools') . -Recurse -Force
Copy-Item (Join-Path $taskPackage 'assets') . -Recurse -Force
dotnet publish .\SkyCharacterStudio.csproj -c Release -r win-x64 --self-contained true -o .\rebuilt
if($LASTEXITCODE -ne 0){throw 'Requires the .NET SDK and NuGet access.'}
Copy-Item (Join-Path $taskPackage 'runtime') .\rebuilt -Recurse -Force
Copy-Item (Join-Path $taskPackage 'tools') .\rebuilt -Recurse -Force
$taskNative=Join-Path $taskPackage 'runtime-source'
# Install a Windows x64 MinGW g++ toolchain to rebuild native components.
& g++ -std=c++17 -O2 -static (Join-Path $taskNative 'tools\build_summon_dat.cpp') (Join-Path $taskNative 'vendor\ed9_dat\ed9_dat.cpp') -o .\rebuilt\tools\build_summon_dat.exe
if($LASTEXITCODE -ne 0){throw 'DAT builder compilation failed.'}
& g++ -std=c++17 -O2 -shared -static (Join-Path $taskNative 'tools\scherazard_event_starter.cpp') ('-I'+(Join-Path $taskNative 'vendor\ed9modmanager')) -o .\rebuilt\runtime\mod-template\ED9Loader\plugins\EventStarter.dll
if($LASTEXITCODE -ne 0){throw 'EventStarter compilation failed.'}
& g++ -std=c++17 -O2 -shared -static (Join-Path $taskNative 'tools\scene_redirect.cpp') ('-I'+(Join-Path $taskNative 'vendor\ed9modmanager')) -o .\rebuilt\runtime\mod-template\ED9Loader\plugins\SceneRedirect.dll
if($LASTEXITCODE -ne 0){throw 'SceneRedirect compilation failed.'}
