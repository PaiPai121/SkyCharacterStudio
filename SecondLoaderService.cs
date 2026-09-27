using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sky1stCharacterStudio;

/// <summary>Identifies the 2nd Chapter loose-file XInput proxy without executing it.</summary>
public static class SecondLoaderService
{
    private const string FileName = "xinput1_4.dll";

    public static string? FindAvailable(string gameRoot, string projectRoot)
    {
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "runtime", "second-loader", FileName),
            Path.Combine(projectRoot, "cache", "second-loader", FileName),
            Path.Combine(gameRoot, FileName)
        })
        {
            if (!File.Exists(candidate)) continue;
            try { Validate(candidate); return candidate; }
            catch (InvalidDataException) { }
        }
        return null;
    }

    public static string Import(string selectedPath, string projectRoot)
    {
        Validate(selectedPath);
        var cache = Path.Combine(projectRoot, "cache", "second-loader");
        Directory.CreateDirectory(cache);
        var destination = Path.Combine(cache, FileName);
        if (!Path.GetFullPath(selectedPath).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(selectedPath, destination, true);
        Validate(destination);
        return destination;
    }

    public static string Validate(string path)
    {
        if (!LooksLikeSecondLoader(path))
            throw new InvalidDataException(UiText.F("error.second.loader.invalid", path));
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        var manifest = Path.Combine(AppContext.BaseDirectory, "assets", "supported-game.json");
        if (!File.Exists(manifest))
            throw new FileNotFoundException(UiText.T("error.compatibility.missing"), manifest);
        using var supported = JsonDocument.Parse(File.ReadAllText(manifest));
        var accepted = supported.RootElement.GetProperty("loaders").EnumerateArray()
            .Any(loader => loader.GetProperty("game").GetString() == "second"
                && loader.GetProperty("size").GetInt64() == stream.Length
                && string.Equals(loader.GetProperty("sha256").GetString(), hash, StringComparison.OrdinalIgnoreCase));
        if (!accepted)
            throw new InvalidDataException(UiText.F("error.second.loader.invalid", path));
        return hash;
    }

    private static bool LooksLikeSecondLoader(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is < 16384 or > 5242880) return false;
            var data = File.ReadAllBytes(path);
            if (data[0] != 'M' || data[1] != 'Z') return false;
            var pe = (int)U32(data, 0x3c);
            if (pe < 0x40 || pe > data.Length - 0x108 || U32(data, pe) != 0x00004550) return false;
            if (U16(data, pe + 4) != 0x8664) return false; // AMD64
            var sections = U16(data, pe + 6);
            var optionalSize = U16(data, pe + 20);
            var optional = pe + 24;
            if (U16(data, optional) != 0x20b || optionalSize < 0x80) return false; // PE32+
            var sectionTable = optional + optionalSize;
            if (sections == 0 || sectionTable + sections * 40 > data.Length) return false;
            var exportRva = U32(data, optional + 0x70);
            if (exportRva == 0) return false;
            int Offset(uint rva)
            {
                for (var i = 0; i < sections; i++)
                {
                    var section = sectionTable + i * 40;
                    var size = Math.Max(U32(data, section + 8), U32(data, section + 16));
                    var start = U32(data, section + 12);
                    if (rva < start || rva - start >= size) continue;
                    var offset = (long)U32(data, section + 20) + rva - start;
                    if (offset >= 0 && offset < data.Length) return (int)offset;
                }
                return -1;
            }
            var export = Offset(exportRva);
            if (export < 0 || export + 40 > data.Length) return false;
            var ordinalBase = U32(data, export + 16);
            var functionCount = U32(data, export + 20);
            var functionTable = Offset(U32(data, export + 28));
            if (functionTable < 0 || functionCount < 3 || functionCount > 128
                || functionTable + functionCount * 4 > data.Length) return false;
            foreach (uint ordinal in new uint[] { 2, 3 })
            {
                if (ordinal < ordinalBase || ordinal - ordinalBase >= functionCount
                    || U32(data, functionTable + (int)(ordinal - ordinalBase) * 4) == 0) return false;
            }
            return Contains(data, "SORA2LOOSELOAD_LOG")
                && Contains(data, "sora2looseload.ini")
                && Contains(data, "sora2looseload.log");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private static bool Contains(byte[] data, string text) => data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;
}
