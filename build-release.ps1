[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$SkipSmoke,
    [switch]$KeepStaging,
    [string]$ArtifactDirectory
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
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label 失败（退出码 $LASTEXITCODE）。"
    }
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
        'Sky1stCharacterStudio.exe',
        'assets/character-ages.json',
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

$root = (Get-Location).Path
$projectFile = Join-Path $root 'Sky1stCharacterStudio.csproj'
$packageScript = Join-Path $root 'tools\package_release.py'
$iconScript = Join-Path $root 'tools\make_app_icon.py'
$iconSource = Join-Path $root 'assets\Sky1stCharacterStudio.png'
$iconOutput = Join-Path $root 'assets\Sky1stCharacterStudio.ico'
$nativeSource = Join-Path $root 'runtime-source\tools'
$datSource = Join-Path $root 'runtime-source\vendor\ed9_dat\ed9_dat.cpp'
$nativeInclude = Join-Path $root 'runtime-source\vendor\ed9modmanager'
$sibling = [IO.Path]::GetFullPath((Join-Path $root '..\Sky1st-Scherazard-Mod'))
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
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $root 'release-artifacts'
}
$zipPath = Join-Path $ArtifactDirectory "Sky1stCharacterStudio-$version-$RuntimeIdentifier.zip"
$completed = $false

try {
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

    $gameRoot = ''
    $gameDirectoryFile = Join-Path $root 'game-directory.txt'
    if (Test-Path -LiteralPath $gameDirectoryFile) {
        $gameRoot = (Get-Content -LiteralPath $gameDirectoryFile -Raw -Encoding UTF8).Trim()
    }
    if (-not $SkipSmoke) {
        if (-not [string]::IsNullOrWhiteSpace($gameRoot) -and (Test-Path -LiteralPath (Join-Path $gameRoot 'sora_1st.exe'))) {
            $smokeProject = Join-Path $root 'smoke\PortableReleaseCheck.csproj'
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
                ('-p:StudioReference=' + (Join-Path $buildDirectory 'Sky1stCharacterStudio.dll')),
                ('-p:OutputPath=' + $smokeDirectory)
            )
            Invoke-Checked $dotnet $smokeBuild '运行前构建便携发布离线检查'
            foreach ($directoryName in @('assets', 'runtime', 'tools')) {
                Copy-Item -LiteralPath (Join-Path $portableDirectory $directoryName) -Destination (Join-Path $smokeDirectory $directoryName) -Recurse -Force
            }
            $smokeExe = Join-Path $smokeDirectory 'PortableReleaseCheck.exe'
            if (-not (Test-Path -LiteralPath $smokeExe)) {
                throw "离线检查程序未生成：$smokeExe"
            }
            $previousGameEnv = [Environment]::GetEnvironmentVariable('SKY1ST_GAME_ROOT', 'Process')
            $previousSmokeEnv = [Environment]::GetEnvironmentVariable('SKY1ST_SMOKE_ROOT', 'Process')
            try {
                $env:SKY1ST_GAME_ROOT = $gameRoot
                $env:SKY1ST_SMOKE_ROOT = $smokeDirectory
                Invoke-Checked $smokeExe @() '执行 WPF、导出、安装回滚离线检查'
            } finally {
                if ($null -eq $previousGameEnv) {
                    Remove-Item Env:SKY1ST_GAME_ROOT -ErrorAction SilentlyContinue
                } else {
                    $env:SKY1ST_GAME_ROOT = $previousGameEnv
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

    Invoke-Checked $python @(
        '-X', 'utf8', (Join-Path $root 'tools\create_release_zip.py'),
        '--stage', $portableDirectory,
        '--zip', $zipPath
    ) '生成并逐文件校验 ZIP 发布包'
    Require-Path $zipPath 'ZIP 发布包'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
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
    if ($completed -and -not $KeepStaging -and (Test-Path -LiteralPath $runRoot)) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
