$ErrorActionPreference = 'Stop'
$taskRoot = 'D:\work_console\Sky1stCharacterStudio'
$taskGame = 'D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st'
$taskPackage = Join-Path $taskRoot 'cache\julia-summon-update'
if (-not (Test-Path -LiteralPath (Join-Path $taskGame 'sora_1st.exe'))) { throw 'Game directory not found.' }
if (Get-Process sora_1st -ErrorAction SilentlyContinue) { throw 'Exit the game before installing.' }
$taskModel = Join-Path $taskGame 'Mod\ScherazardSummon\asset\common\model\chr5107.mdl'
$taskExpected = [IO.File]::ReadAllText((Join-Path $taskRoot 'cache\julia-installed-sha256.txt')).Trim()
if ((Get-FileHash -LiteralPath $taskModel -Algorithm SHA256).Hash -ne $taskExpected) { throw 'The installed Julia model changed after staging; installation stopped.' }
$taskFiles = @('ED9Loader\plugins\EventStarter.dll','ED9Loader\config\EventStarter.ini','Mod\ScherazardSummon\ScherazardSummon.dat','Mod\ScherazardSummon\asset\common\model\chr_studio_original.mdl','Mod\ScherazardSummon\character-studio-selection.json')
$taskBackup = Join-Path $taskRoot ('install-backups\summon-repair-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$taskExisted = @{}
foreach ($taskRelative in $taskFiles) {
    $taskSource = Join-Path $taskPackage $taskRelative
    $taskTarget = Join-Path $taskGame $taskRelative
    if (-not (Test-Path -LiteralPath $taskSource -PathType Leaf)) { throw "Missing staged file: $taskRelative" }
    $taskExisted[$taskRelative] = Test-Path -LiteralPath $taskTarget -PathType Leaf
    if ($taskExisted[$taskRelative]) {
        $taskSaved = Join-Path $taskBackup $taskRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskSaved) -Force | Out-Null
        Copy-Item -LiteralPath $taskTarget -Destination $taskSaved
    }
}
$taskTouched = [Collections.Generic.List[string]]::new()
try {
    foreach ($taskRelative in $taskFiles) {
        if (Get-Process sora_1st -ErrorAction SilentlyContinue) { throw 'Game started during installation.' }
        $taskSource = Join-Path $taskPackage $taskRelative
        $taskTarget = Join-Path $taskGame $taskRelative
        $taskTouched.Add($taskRelative)
        Copy-Item -LiteralPath $taskSource -Destination $taskTarget -Force
        if ((Get-FileHash -LiteralPath $taskSource).Hash -ne (Get-FileHash -LiteralPath $taskTarget).Hash) { throw "Copy verification failed: $taskRelative" }
    }
    if ((Get-FileHash -LiteralPath $taskModel).Hash -ne $taskExpected) { throw 'Julia model verification failed.' }
    @{game=$taskGame;files=$taskExisted;state='installed';juliaHash=$taskExpected} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskBackup 'manifest.json') -Encoding UTF8
    Write-Output "Installed summon repair. Existing Julia model unchanged. Backup: $taskBackup"
} catch {
    foreach ($taskRelative in $taskTouched) {
        $taskTarget=Join-Path $taskGame $taskRelative
        if ($taskExisted[$taskRelative]) { Copy-Item -LiteralPath (Join-Path $taskBackup $taskRelative) -Destination $taskTarget -Force }
        elseif (Test-Path -LiteralPath $taskTarget) { Remove-Item -LiteralPath $taskTarget }
    }
    throw
}
