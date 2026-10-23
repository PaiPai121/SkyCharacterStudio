Set-StrictMode -Version Latest

# Explorer's "Opening these files might be harmful" decision uses the Windows
# URL security zone. A local file under a Low-integrity checkout can map to the
# Internet zone even when it has no Zone.Identifier stream. Probe a newly created
# file in the actual output directory instead of guessing from its drive letter.
if (-not ('SkyStudioArtifactZone' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("79EAC9EE-BAF9-11CE-8C82-00AA004BA90B"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISkyStudioInternetSecurityManager
{
    [PreserveSig] int SetSecuritySite(IntPtr site);
    [PreserveSig] int GetSecuritySite(out IntPtr site);
    [PreserveSig] int MapUrlToZone([MarshalAs(UnmanagedType.LPWStr)] string url,
        out int zone, int flags);
}

public static class SkyStudioArtifactZone
{
    [DllImport("urlmon.dll")]
    private static extern int CoInternetCreateSecurityManager(IntPtr serviceProvider,
        out ISkyStudioInternetSecurityManager manager, int reserved);

    public static int Map(string fileUri)
    {
        ISkyStudioInternetSecurityManager manager;
        int result = CoInternetCreateSecurityManager(IntPtr.Zero, out manager, 0);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        try
        {
            int zone;
            result = manager.MapUrlToZone(fileUri, out zone, 0);
            if (result != 0) Marshal.ThrowExceptionForHR(result);
            return zone;
        }
        finally { Marshal.ReleaseComObject(manager); }
    }
}
'@
}

function Get-StudioArtifactZone {
    param([Parameter(Mandatory = $true)][string]$FilePath)
    $absolute = [IO.Path]::GetFullPath($FilePath)
    return [SkyStudioArtifactZone]::Map(([Uri]::new($absolute)).AbsoluteUri)
}

function Test-StudioArtifactDirectoryZone {
    param([Parameter(Mandatory = $true)][string]$Directory)
    $resolved = [IO.Path]::GetFullPath($Directory)
    New-Item -ItemType Directory -Force -Path $resolved | Out-Null
    $probe = Join-Path $resolved ('.sky-studio-zone-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($probe, '')
        return Get-StudioArtifactZone -FilePath $probe
    } finally {
        if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force }
    }
}

function Resolve-StudioArtifactDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][bool]$SourceDirty,
        [string]$Override
    )
    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        $resolved = [IO.Path]::GetFullPath($Override)
        $zone = Test-StudioArtifactDirectoryZone -Directory $resolved
        if ($zone -ne 0) {
            throw "指定发布目录被 Windows 归为区域 $zone，资源管理器可能显示网络文件警告：$resolved。请用 -ArtifactDirectory 指定普通本地目录。"
        }
        return $resolved
    }

    $default = if ($SourceDirty) {
        Join-Path $Repository 'release-artifacts\local-previews'
    } else {
        Join-Path $Repository 'release-artifacts'
    }
    $default = [IO.Path]::GetFullPath($default)
    $zone = Test-StudioArtifactDirectoryZone -Directory $default
    if ($zone -eq 0) { return $default }

    $sibling = Join-Path (Split-Path -Parent $Repository) 'SkyCharacterStudio-Release'
    if ($SourceDirty) { $sibling = Join-Path $sibling 'local-previews' }
    $sibling = [IO.Path]::GetFullPath($sibling)
    $siblingZone = Test-StudioArtifactDirectoryZone -Directory $sibling
    if ($siblingZone -ne 0) {
        throw "仓库内发布目录属于区域 $zone，同级目录属于区域 $siblingZone；无法自动找到可安全打开的本地发布位置。请用 -ArtifactDirectory 指定普通本地目录。"
    }
    Write-Host "仓库内发布目录属于 Windows 区域 $zone；改用本地区域 0：$sibling"
    return $sibling
}
