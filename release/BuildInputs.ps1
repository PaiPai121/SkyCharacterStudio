Set-StrictMode -Version Latest

function Get-StudioMainWorktree {
    param([Parameter(Mandatory = $true)][string]$Repository)
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) { return $null }
    $safeDirectory = [IO.Path]::GetFullPath($Repository).Replace('\', '/')
    $common = & $git.Source -c "safe.directory=$safeDirectory" -C $Repository rev-parse --git-common-dir 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace([string]$common)) { return $null }
    $commonPath = if ([IO.Path]::IsPathRooted($common)) { $common }
        else { Join-Path $Repository $common }
    $main = Split-Path -Parent ([IO.Path]::GetFullPath($commonPath))
    if (Test-Path -LiteralPath (Join-Path $main '.git')) { return $main }
    return $null
}

function Resolve-StudioToolchain {
    param([Parameter(Mandatory = $true)][string]$Repository, [string]$Override)
    $requested = if (-not [string]::IsNullOrWhiteSpace($Override)) { $Override }
        else { $env:SKY_STUDIO_TOOLCHAIN }
    if (-not [string]::IsNullOrWhiteSpace($requested)) {
        $resolved = [IO.Path]::GetFullPath($requested)
        if (-not (Test-Path -LiteralPath (Join-Path $resolved '.venv\Scripts\python.exe'))) {
            throw "指定的工具链没有 .venv\Scripts\python.exe：$resolved。请检查 -ToolchainRoot 或 SKY_STUDIO_TOOLCHAIN。"
        }
        return $resolved
    }
    $candidates = [Collections.Generic.List[string]]::new()
    $candidates.Add((Join-Path (Split-Path -Parent $Repository) 'Sky1st-Scherazard-Mod'))
    $main = Get-StudioMainWorktree -Repository $Repository
    if ($null -ne $main) {
        $candidates.Add((Join-Path (Split-Path -Parent $main) 'Sky1st-Scherazard-Mod'))
    }
    foreach ($candidate in $candidates) {
        $resolved = [IO.Path]::GetFullPath($candidate)
        if (Test-Path -LiteralPath (Join-Path $resolved '.venv\Scripts\python.exe')) {
            return $resolved
        }
    }
    throw "找不到打包用 Python 工具链。已检查当前检出及主工作树的同级目录；可用 -ToolchainRoot 指定含 .venv\Scripts\python.exe 的目录。"
}

function Test-StudioCleanSource {
    param([Parameter(Mandatory = $true)][string]$Repository)
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) { throw '找不到 Git；打包清单需要记录源码提交。' }
    $safeDirectory = [IO.Path]::GetFullPath($Repository).Replace('\', '/')
    $status = @(& $git.Source -c "safe.directory=$safeDirectory" -C $Repository status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 工作树状态，已停止打包。' }
    return $status.Count -eq 0
}

function Get-StudioGameEdition {
    param([Parameter(Mandatory = $true)][string]$Directory)
    $first = Test-Path -LiteralPath (Join-Path $Directory 'sora_1st.exe') -PathType Leaf
    $second = Test-Path -LiteralPath (Join-Path $Directory 'sora_2nd.exe') -PathType Leaf
    if ($first -and -not $second) { return 'first' }
    if ($second -and -not $first) { return 'second' }
    return $null
}

function Get-StudioSteamLibraries {
    $roots = [Collections.Generic.List[string]]::new()
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        $settings = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($null -eq $settings) { continue }
        foreach ($property in @('SteamPath', 'InstallPath')) {
            $entry = $settings.PSObject.Properties[$property]
            $value = if ($null -ne $entry) { $entry.Value } else { $null }
            if (-not [string]::IsNullOrWhiteSpace([string]$value) -and
                (Test-Path -LiteralPath $value -PathType Container)) {
                $resolved = [IO.Path]::GetFullPath($value)
                if (-not $roots.Contains($resolved)) { $roots.Add($resolved) }
            }
        }
    }
    foreach ($steam in @($roots.ToArray())) {
        $index = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $index -PathType Leaf)) { continue }
        $content = Get-Content -LiteralPath $index -Raw -Encoding UTF8
        foreach ($match in [regex]::Matches($content, '(?m)^\s*"path"\s*"([^"]+)"')) {
            $library = $match.Groups[1].Value.Replace('\\', '\')
            if (Test-Path -LiteralPath $library -PathType Container) {
                $resolved = [IO.Path]::GetFullPath($library)
                if (-not $roots.Contains($resolved)) { $roots.Add($resolved) }
            }
        }
    }
    return $roots.ToArray()
}

function Get-StudioGameTargets {
    param([Parameter(Mandatory = $true)][string]$Repository, [string]$Override)
    $targets = [Collections.Generic.List[object]]::new()
    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        $resolved = [IO.Path]::GetFullPath($Override)
        $edition = Get-StudioGameEdition -Directory $resolved
        if ($null -eq $edition) {
            throw "-GameRoot 不是含 sora_1st.exe 或 sora_2nd.exe 的游戏目录：$resolved"
        }
        $targets.Add([pscustomobject]@{ Edition = $edition; Root = $resolved })
        return $targets.ToArray()
    }
    $saved = Join-Path $Repository 'game-directory.txt'
    if (Test-Path -LiteralPath $saved -PathType Leaf) {
        $candidate = (Get-Content -LiteralPath $saved -Raw -Encoding UTF8).Trim()
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $resolved = [IO.Path]::GetFullPath($candidate)
            $sourceRoots = @($Repository, (Get-StudioMainWorktree -Repository $Repository)) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            $isBuildFixture = @($sourceRoots | Where-Object {
                $prefix = [IO.Path]::GetFullPath($_).TrimEnd('\') + '\'
                $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
            if ($isBuildFixture) {
                Write-Warning "game-directory.txt 指向项目工作区内的测试副本，已忽略：$resolved"
            } else {
                $edition = Get-StudioGameEdition -Directory $resolved
                if ($null -eq $edition) { Write-Warning "game-directory.txt 指向无效或已删除的目录：$resolved；继续搜索 Steam 安装。" }
                else { $targets.Add([pscustomobject]@{ Edition = $edition; Root = $resolved }) }
            }
        }
    }
    foreach ($library in Get-StudioSteamLibraries) {
        $apps = Join-Path $library 'steamapps'
        $common = [IO.Path]::GetFullPath((Join-Path $apps 'common')).TrimEnd('\') + '\'
        foreach ($manifest in @(Get-ChildItem -LiteralPath $apps -Filter 'appmanifest_*.acf' -File -ErrorAction SilentlyContinue)) {
            $content = Get-Content -LiteralPath $manifest.FullName -Raw -Encoding UTF8
            $match = [regex]::Match($content, '(?m)^\s*"installdir"\s*"([^"]+)"')
            if (-not $match.Success) { continue }
            $name = $match.Groups[1].Value.Replace('\\', '\')
            if ([IO.Path]::IsPathRooted($name)) { continue }
            $resolved = [IO.Path]::GetFullPath((Join-Path $common $name))
            if (-not $resolved.StartsWith($common, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $edition = Get-StudioGameEdition -Directory $resolved
            if ($null -ne $edition -and -not @($targets | Where-Object {
                $_.Edition -eq $edition
            }).Count) {
                $targets.Add([pscustomobject]@{ Edition = $edition; Root = $resolved })
            }
        }
    }
    return $targets.ToArray()
}
