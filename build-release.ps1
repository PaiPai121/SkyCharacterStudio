[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$SkipSmoke,
    [switch]$KeepStaging,
    [string]$ArtifactDirectory,
    [string]$ToolchainRoot,
    [string]$GameRoot,
    [string]$SecondLoaderPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location -LiteralPath $PSScriptRoot

function Require-Path {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "缺少$Description：$Path"
    }
}

function Resolve-Tool {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$Candidates = @()
    )
    foreach ($candidate in $Candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        if (-not [string]::IsNullOrWhiteSpace($command.Path)) { return $command.Path }
        if (-not [string]::IsNullOrWhiteSpace($command.Source)) { return $command.Source }
    }
    throw "找不到 $Name。请安装对应工具后再运行此脚本。"
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [AllowEmptyCollection()][string[]]$Arguments = @(),
        [Parameter(Mandatory = $true)][string]$Label
    )
    Write-Host "`n==> $Label"
    $stageClock = [Diagnostics.Stopwatch]::StartNew()
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label 失败（退出码 $LASTEXITCODE，用时 $([int]$stageClock.Elapsed.TotalSeconds) 秒）。"
    }
    Write-Host "$Label 完成（$([int]$stageClock.Elapsed.TotalSeconds) 秒）"
}

function Get-ProjectVersion {
    param([Parameter(Mandatory = $true)][string]$ProjectFile)
    $project = [xml](Get-Content -LiteralPath $ProjectFile -Raw -Encoding UTF8)
    $version = $project.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$version)) {
        throw "无法从项目文件读取 Version：$ProjectFile"
    }
    return [string]$version
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToUpperInvariant()
    } finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function Test-PortableDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Version
    )
    $manifestPath = Join-Path $Directory 'release-manifest.json'
    Require-Path $manifestPath '发布清单'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$manifest.version -ne $Version) {
        throw "发布清单版本不匹配：$($manifest.version) / $Version"
    }

    foreach ($relative in @(
        'SkyCharacterStudio.exe',
        'assets/character-ages.json',
        'assets/character-ages-2nd.json',
        'assets/supported-game.json',
        'runtime/python/python.exe',
        'runtime/mod-template/xinput1_4.dll',
        'runtime/mod-template/ED9Loader/plugins/EventStarter.dll',
        'runtime/mod-template/ED9Loader/plugins/SceneRedirect.dll',
        'tools/build_summon_dat.exe'
    )) {
        Require-Path (Join-Path $Directory ($relative -replace '/', '\')) "发布文件 $relative"
    }

    $forbidden = @(Get-ChildItem -LiteralPath $Directory -Recurse -File |
        Where-Object { $_.Extension.ToLowerInvariant() -in @('.mdl', '.dat', '.dds', '.pac', '.blend') })
    if ($forbidden.Count -gt 0) {
        throw "发布目录带入游戏资源：$($forbidden[0].FullName)"
    }

    foreach ($property in @($manifest.files.PSObject.Properties)) {
        $file = Join-Path $Directory ($property.Name -replace '/', '\')
        Require-Path $file "清单中的文件 $($property.Name)"
    }
    if ($manifest.source_dirty -eq $true) {
        Write-Warning '当前工作树存在未提交修改；release-manifest.json 已标记 source_dirty=true。'
    }
    return $manifest
}

function Invoke-SecondInstallCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Reference,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$PortableDirectory,
        [Parameter(Mandatory = $true)][string]$GameDirectory,
        [Parameter(Mandatory = $true)][string]$LoaderPath
    )
    $project = Join-Path $root 'smoke\SecondInstallCheck.csproj'
    Require-Path $project '2nd 隔离安装检查项目'
    Require-Path $LoaderPath '2nd 专用加载器'
    $restore = @('restore', $project, '--runtime', $RuntimeIdentifier, '--ignore-failed-sources', '-p:NuGetAudit=false')
    if (Test-Path -LiteralPath $nugetPackages) { $restore += "-p:RestorePackagesPath=$nugetPackages" }
    Invoke-Checked $dotnet $restore '还原 2nd 安装检查'
    Invoke-Checked $dotnet @(
        'build', $project, '--configuration', $Configuration, '--no-restore',
        ('-p:StudioReference=' + $Reference),
        ('-p:OutputPath=' + $OutputDirectory)
    ) '构建 2nd 安装检查'
    foreach ($directoryName in @('assets', 'runtime', 'tools')) {
        Copy-Item -LiteralPath (Join-Path $PortableDirectory $directoryName) -Destination (Join-Path $OutputDirectory $directoryName) -Recurse -Force
    }
    $oldGame = [Environment]::GetEnvironmentVariable('SKY2ND_GAME_ROOT', 'Process')
    $oldLoader = [Environment]::GetEnvironmentVariable('SKY2ND_LOADER_PATH', 'Process')
    $oldSmoke = [Environment]::GetEnvironmentVariable('SKY1ST_SMOKE_ROOT', 'Process')
    try {
        $env:SKY2ND_GAME_ROOT = $GameDirectory
        $env:SKY2ND_LOADER_PATH = $LoaderPath
        $env:SKY1ST_SMOKE_ROOT = $OutputDirectory
        Invoke-Checked (Join-Path $OutputDirectory 'SecondInstallCheck.exe') @() '执行 2nd 隔离安装、冲突与撤销检查'
    } finally {
        if ($null -eq $oldGame) { Remove-Item Env:SKY2ND_GAME_ROOT -ErrorAction SilentlyContinue }
        else { $env:SKY2ND_GAME_ROOT = $oldGame }
        if ($null -eq $oldLoader) { Remove-Item Env:SKY2ND_LOADER_PATH -ErrorAction SilentlyContinue }
        else { $env:SKY2ND_LOADER_PATH = $oldLoader }
        if ($null -eq $oldSmoke) { Remove-Item Env:SKY1ST_SMOKE_ROOT -ErrorAction SilentlyContinue }
        else { $env:SKY1ST_SMOKE_ROOT = $oldSmoke }
    }
}

$root = (Get-Location).Path
$projectFile = Join-Path $root 'SkyCharacterStudio.csproj'
$packageScript = Join-Path $root 'tools\package_release.py'
$iconScript = Join-Path $root 'tools\make_app_icon.py'
$iconSource = Join-Path $root 'assets\SkyCharacterStudio.png'
$iconOutput = Join-Path $root 'assets\SkyCharacterStudio.ico'
$nativeSource = Join-Path $root 'runtime-source\tools'
$datSource = Join-Path $root 'runtime-source\vendor\ed9_dat\ed9_dat.cpp'
$nativeInclude = Join-Path $root 'runtime-source\vendor\ed9modmanager'
$sibling = if (-not [string]::IsNullOrWhiteSpace($ToolchainRoot)) {
    [IO.Path]::GetFullPath($ToolchainRoot)
} elseif (-not [string]::IsNullOrWhiteSpace($env:SKY_STUDIO_TOOLCHAIN)) {
    [IO.Path]::GetFullPath($env:SKY_STUDIO_TOOLCHAIN)
} else {
    [IO.Path]::GetFullPath((Join-Path $root '..\Sky1st-Scherazard-Mod'))
}
$env:SKY_STUDIO_TOOLCHAIN = $sibling
$python = Join-Path $sibling '.venv\Scripts\python.exe'
$nugetPackages = if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
    Join-Path $env:USERPROFILE '.nuget\packages'
} else { '' }

Require-Path $projectFile '项目文件'
Require-Path $packageScript '组装脚本'
Require-Path $iconScript '图标生成脚本'
Require-Path $iconSource '图标源图'
Require-Path (Join-Path $nativeSource 'build_summon_dat.cpp') 'DAT 构建源码'
Require-Path (Join-Path $nativeSource 'scherazard_event_starter.cpp') 'EventStarter 构建源码'
Require-Path (Join-Path $nativeSource 'scene_redirect.cpp') 'SceneRedirect 构建源码'
Require-Path $datSource 'DAT 依赖源码'
Require-Path $nativeInclude 'ED9Loader 头文件'
Require-Path $python '项目 Python 环境'

$dotnet = Resolve-Tool -Name 'dotnet' -Candidates @('C:\Program Files\dotnet\dotnet.exe')
$gxx = Resolve-Tool -Name 'g++' -Candidates @(
    'C:\msys64\ucrt64\bin\g++.exe',
    'C:\msys64\mingw64\bin\g++.exe'
)
$version = Get-ProjectVersion $projectFile
$stageRoot = Join-Path $root 'release-stage'
$runId = "$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$runRoot = Join-Path $stageRoot "pack-$runId"
$buildDirectory = Join-Path $runRoot 'app'
$nativeDirectory = Join-Path $runRoot 'native'
$portableDirectory = Join-Path $runRoot 'portable'
$smokeDirectory = Join-Path $runRoot 'smoke'
$candidateZip = Join-Path $runRoot "SkyCharacterStudio-$version-$RuntimeIdentifier.zip"
$workspaceParent = [IO.Path]::GetFullPath((Split-Path -Parent $root))
$cleanDirectory = [IO.Path]::GetFullPath((Join-Path $workspaceParent "Sky1stReleaseQA-$runId"))
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $root 'release-artifacts'
}
$zipPath = Join-Path $ArtifactDirectory "SkyCharacterStudio-$version-$RuntimeIdentifier.zip"
$completed = $false

try {
    if (Test-Path -LiteralPath $zipPath) { throw "此版本发布包已经存在，请先更新版本号：$zipPath" }
    if (Test-Path -LiteralPath $cleanDirectory) { throw "独立解压目录已经存在：$cleanDirectory" }
    New-Item -ItemType Directory -Force -Path $buildDirectory, $nativeDirectory, $ArtifactDirectory | Out-Null

    Invoke-Checked $python @(
        '-X', 'utf8', $iconScript,
        '--input', $iconSource,
        '--output', $iconOutput
    ) '生成应用图标'

    $restoreArguments = @('restore', $projectFile, '--runtime', $RuntimeIdentifier, '--ignore-failed-sources', '-p:NuGetAudit=false')
    if (Test-Path -LiteralPath $nugetPackages) {
        $restoreArguments += "-p:RestorePackagesPath=$nugetPackages"
    }
    Invoke-Checked $dotnet $restoreArguments '还原 .NET 依赖'

    $publishArguments = @(
        'publish', $projectFile,
        '--configuration', $Configuration,
        '--runtime', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--no-restore',
        '--output', $buildDirectory,
        '-p:PublishSingleFile=false',
        '-p:NuGetAudit=false'
    )
    if (Test-Path -LiteralPath $nugetPackages) {
        $publishArguments += "-p:RestorePackagesPath=$nugetPackages"
    }
    Invoke-Checked $dotnet $publishArguments '发布自包含 .NET 工作台'

    Invoke-Checked $gxx @(
        '-std=c++17', '-O2', '-static',
        (Join-Path $nativeSource 'build_summon_dat.cpp'), $datSource,
        '-o', (Join-Path $nativeDirectory 'build_summon_dat.exe')
    ) '编译 DAT 构建器'
    Copy-Item -LiteralPath (Join-Path $nativeDirectory 'build_summon_dat.exe') -Destination (Join-Path $root 'tools\build_summon_dat.exe') -Force
    Invoke-Checked $gxx @(
        '-std=c++17', '-O2', '-shared', '-static',
        (Join-Path $nativeSource 'scherazard_event_starter.cpp'),
        "-I$nativeInclude",
        '-o', (Join-Path $nativeDirectory 'EventStarter.dll')
    ) '编译 EventStarter 插件'
    Invoke-Checked $gxx @(
        '-std=c++17', '-O2', '-shared', '-static',
        (Join-Path $nativeSource 'scene_redirect.cpp'),
        "-I$nativeInclude",
        '-o', (Join-Path $nativeDirectory 'SceneRedirect.dll')
    ) '编译 SceneRedirect 插件'

    Invoke-Checked $python @(
        '-X', 'utf8', $packageScript,
        '--build', $buildDirectory,
        '--out', $portableDirectory,
        '--native-dir', $nativeDirectory,
        '--version', $version
    ) '按白名单组装便携发布目录'
    $manifest = Test-PortableDirectory $portableDirectory $version

    $gameRoot = if (-not [string]::IsNullOrWhiteSpace($GameRoot)) { [IO.Path]::GetFullPath($GameRoot) } else { '' }
    $gameDirectoryFile = Join-Path $root 'game-directory.txt'
    if ([string]::IsNullOrWhiteSpace($gameRoot) -and (Test-Path -LiteralPath $gameDirectoryFile)) {
        $gameRoot = (Get-Content -LiteralPath $gameDirectoryFile -Raw -Encoding UTF8).Trim()
    }
    $smokeEdition = if (-not [string]::IsNullOrWhiteSpace($gameRoot) -and
        (Test-Path -LiteralPath (Join-Path $gameRoot 'sora_1st.exe'))) { 'first' }
        elseif (-not [string]::IsNullOrWhiteSpace($gameRoot) -and
        (Test-Path -LiteralPath (Join-Path $gameRoot 'sora_2nd.exe'))) { 'second' }
        else { '' }
    if (-not $SkipSmoke) {
        if (-not [string]::IsNullOrWhiteSpace($smokeEdition)) {
            $smokeName = if ($smokeEdition -eq 'second') { 'SecondChapterCheck' } else { 'PortableReleaseCheck' }
            $smokeProject = Join-Path $root "smoke\$smokeName.csproj"
            Require-Path $smokeProject '便携发布离线检查项目'
            $smokeRestore = @('restore', $smokeProject, '--runtime', $RuntimeIdentifier, '--ignore-failed-sources', '-p:NuGetAudit=false')
            if (Test-Path -LiteralPath $nugetPackages) {
                $smokeRestore += "-p:RestorePackagesPath=$nugetPackages"
            }
            Invoke-Checked $dotnet $smokeRestore '还原便携发布离线检查'
            $smokeBuild = @(
                'build', $smokeProject,
                '--configuration', $Configuration,
                '--no-restore',
                ('-p:StudioReference=' + (Join-Path $buildDirectory 'SkyCharacterStudio.dll')),
                ('-p:OutputPath=' + $smokeDirectory)
            )
            Invoke-Checked $dotnet $smokeBuild '运行前构建便携发布离线检查'
            foreach ($directoryName in @('assets', 'runtime', 'tools')) {
                Copy-Item -LiteralPath (Join-Path $portableDirectory $directoryName) -Destination (Join-Path $smokeDirectory $directoryName) -Recurse -Force
            }
            $smokeExe = Join-Path $smokeDirectory "$smokeName.exe"
            if (-not (Test-Path -LiteralPath $smokeExe)) {
                throw "离线检查程序未生成：$smokeExe"
            }
            $previousGameEnv = [Environment]::GetEnvironmentVariable('SKY1ST_GAME_ROOT', 'Process')
            $previousSecondGameEnv = [Environment]::GetEnvironmentVariable('SKY2ND_GAME_ROOT', 'Process')
            $previousSmokeEnv = [Environment]::GetEnvironmentVariable('SKY1ST_SMOKE_ROOT', 'Process')
            try {
                $env:SKY1ST_GAME_ROOT = $gameRoot
                $env:SKY2ND_GAME_ROOT = $gameRoot
                $env:SKY1ST_SMOKE_ROOT = $smokeDirectory
                Invoke-Checked $smokeExe @() '执行 WPF、导出、安装回滚离线检查'
            } finally {
                if ($null -eq $previousGameEnv) {
                    Remove-Item Env:SKY1ST_GAME_ROOT -ErrorAction SilentlyContinue
                } else {
                    $env:SKY1ST_GAME_ROOT = $previousGameEnv
                }
                if ($null -eq $previousSecondGameEnv) {
                    Remove-Item Env:SKY2ND_GAME_ROOT -ErrorAction SilentlyContinue
                } else {
                    $env:SKY2ND_GAME_ROOT = $previousSecondGameEnv
                }
                if ($null -eq $previousSmokeEnv) {
                    Remove-Item Env:SKY1ST_SMOKE_ROOT -ErrorAction SilentlyContinue
                } else {
                    $env:SKY1ST_SMOKE_ROOT = $previousSmokeEnv
                }
            }
        } else {
            Write-Warning '未找到 game-directory.txt 中的可用游戏目录，跳过 WPF 离线检查；发布目录结构检查仍已执行。'
        }
    } else {
        Write-Host '已按参数跳过 WPF 离线检查。'
    }
    if (-not $SkipSmoke -and $smokeEdition -eq 'second') {
        if ([string]::IsNullOrWhiteSpace($SecondLoaderPath)) {
            Write-Warning '未提供 SecondLoaderPath；最终包仍可在首次安装时选择 DLL，但本次无法运行 2nd 隔离安装检查。'
        } else {
            Invoke-SecondInstallCheck -Reference (Join-Path $buildDirectory 'SkyCharacterStudio.dll') `
                -OutputDirectory (Join-Path $runRoot 'second-install-smoke') `
                -PortableDirectory $portableDirectory -GameDirectory $gameRoot -LoaderPath $SecondLoaderPath
        }
    }

    Invoke-Checked $python @(
        '-X', 'utf8', (Join-Path $root 'tools\create_release_zip.py'),
        '--stage', $portableDirectory,
        '--zip', $candidateZip
    ) '生成并逐文件校验 ZIP 发布包'
    Require-Path $candidateZip 'ZIP 发布包'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($candidateZip)
    try {
        $badEntries = @($archive.Entries | Where-Object {
            $_.FullName -match '(?i)\.(mdl|dat|dds|pac|blend)$'
        })
    } finally {
        $archive.Dispose()
    }
    if ($badEntries.Count -gt 0) {
        throw "ZIP 带入游戏资源：$($badEntries[0].FullName)"
    }

    Write-Host "`n==> 从最终 ZIP 独立解压并验证启动程序"
    Expand-Archive -LiteralPath $candidateZip -DestinationPath $cleanDirectory
    $null = Test-PortableDirectory $cleanDirectory $version
    $launcherPath = Join-Path $cleanDirectory 'SkyCharacterStudio.exe'
    $launcher = Start-Process -FilePath $launcherPath -WorkingDirectory $cleanDirectory -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Seconds 5
        $launcher.Refresh()
        if ($launcher.HasExited) { throw "最终包启动程序过早退出，退出码 $($launcher.ExitCode)" }
    } finally {
        $launcher.Refresh()
        if (-not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force }
    }

    if (-not $SkipSmoke -and -not [string]::IsNullOrWhiteSpace($smokeEdition)) {
        Write-Host "`n==> 从独立解压目录执行完整便携流程"
        $cleanSmokeDirectory = Join-Path $cleanDirectory '_smoke'
        $cleanSmokeBuild = @(
            'build', (Join-Path $root "smoke\$smokeName.csproj"),
            '--configuration', $Configuration, '--no-restore',
            ('-p:StudioReference=' + (Join-Path $cleanDirectory 'SkyCharacterStudio.dll')),
            ('-p:OutputPath=' + $cleanSmokeDirectory)
        )
        Invoke-Checked $dotnet $cleanSmokeBuild '构建最终包离线检查'
        foreach ($directoryName in @('assets', 'runtime', 'tools')) {
            Copy-Item -LiteralPath (Join-Path $cleanDirectory $directoryName) -Destination (Join-Path $cleanSmokeDirectory $directoryName) -Recurse -Force
        }
        $previousGameEnv = [Environment]::GetEnvironmentVariable('SKY1ST_GAME_ROOT', 'Process')
        $previousSecondGameEnv = [Environment]::GetEnvironmentVariable('SKY2ND_GAME_ROOT', 'Process')
        $previousSmokeEnv = [Environment]::GetEnvironmentVariable('SKY1ST_SMOKE_ROOT', 'Process')
        try {
            $env:SKY1ST_GAME_ROOT = $gameRoot
            $env:SKY2ND_GAME_ROOT = $gameRoot
            $env:SKY1ST_SMOKE_ROOT = $cleanSmokeDirectory
            Invoke-Checked (Join-Path $cleanSmokeDirectory "$smokeName.exe") @() '验证最终 ZIP 的模型生成与版本隔离'
        } finally {
            if ($null -eq $previousGameEnv) { Remove-Item Env:SKY1ST_GAME_ROOT -ErrorAction SilentlyContinue }
            else { $env:SKY1ST_GAME_ROOT = $previousGameEnv }
            if ($null -eq $previousSecondGameEnv) { Remove-Item Env:SKY2ND_GAME_ROOT -ErrorAction SilentlyContinue }
            else { $env:SKY2ND_GAME_ROOT = $previousSecondGameEnv }
            if ($null -eq $previousSmokeEnv) { Remove-Item Env:SKY1ST_SMOKE_ROOT -ErrorAction SilentlyContinue }
            else { $env:SKY1ST_SMOKE_ROOT = $previousSmokeEnv }
        }
    }
    if (-not $SkipSmoke -and $smokeEdition -eq 'second' -and -not [string]::IsNullOrWhiteSpace($SecondLoaderPath)) {
        Invoke-SecondInstallCheck -Reference (Join-Path $cleanDirectory 'SkyCharacterStudio.dll') `
            -OutputDirectory (Join-Path $cleanDirectory '_second_install_smoke') `
            -PortableDirectory $cleanDirectory -GameDirectory $gameRoot -LoaderPath $SecondLoaderPath
    }

    Move-Item -LiteralPath $candidateZip -Destination $zipPath
    $hash = Get-Sha256 $zipPath
    Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $(Split-Path -Leaf $zipPath)" -Encoding ASCII
    $completed = $true

    Write-Host "`n打包完成。"
    Write-Host "ZIP：$zipPath"
    Write-Host "SHA256：$hash"
    Write-Host "发布目录文件数：$(@($manifest.files.PSObject.Properties).Count)"
    if ($KeepStaging) {
        Write-Host "临时目录（已保留）：$runRoot"
    }
} finally {
    if ($completed -and -not $KeepStaging) {
        $verifiedStageRoot = [IO.Path]::GetFullPath($stageRoot).TrimEnd('\') + '\'
        $verifiedRunRoot = [IO.Path]::GetFullPath($runRoot)
        $verifiedWorkspace = $workspaceParent.TrimEnd('\') + '\'
        if (-not $verifiedRunRoot.StartsWith($verifiedStageRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not $cleanDirectory.StartsWith($verifiedWorkspace, [StringComparison]::OrdinalIgnoreCase)) {
            throw '打包临时目录超出预期工作区，已停止清理。'
        }
        if (Test-Path -LiteralPath $runRoot) { Remove-Item -LiteralPath $runRoot -Recurse -Force }
        if (Test-Path -LiteralPath $cleanDirectory) { Remove-Item -LiteralPath $cleanDirectory -Recurse -Force }
    }
}
