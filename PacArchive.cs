using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Sky1stCharacterStudio;

/// <summary>
/// Read-only FPAC index reader. It never writes back to a game archive.
/// </summary>
public sealed class PacArchive
{
    private readonly Dictionary<string, PacEntry> _byName;
    private readonly string? _archivePath;

    private PacArchive(string path, List<PacEntry> entries, string? archivePath)
    {
        Path = path;
        Entries = entries;
        _archivePath = archivePath;
        _byName = entries.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public string Path { get; }
    public string? ArchivePath => _archivePath;
    public IReadOnlyList<PacEntry> Entries { get; }

    public bool TryGet(string name, out PacEntry entry) => _byName.TryGetValue(name, out entry!);

    public static PacArchive Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(UiText.F("error.pac.missing.path", path), path);
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length < 16) throw new InvalidDataException(UiText.T("error.pac.small"));

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[16];
        ReadExactly(stream, header);
        if (Encoding.ASCII.GetString(header[..4]) != "FPAC")
            throw new InvalidDataException(UiText.T("error.pac.format"));

        var count = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        if (count > 2_000_000) throw new InvalidDataException(UiText.T("error.pac.entries"));
        var recordsBytes = checked((long)count * 32L);
        if (16L + recordsBytes > fileInfo.Length) throw new InvalidDataException(UiText.T("error.pac.range"));

        var records = new (ulong Hash, ulong NameOffset, ulong Size, ulong Offset)[count];
        Span<byte> record = stackalloc byte[32];
        for (var i = 0; i < count; i++)
        {
            ReadExactly(stream, record);
            records[i] = (
                BinaryPrimitives.ReadUInt64LittleEndian(record[..8]),
                BinaryPrimitives.ReadUInt64LittleEndian(record[8..16]),
                BinaryPrimitives.ReadUInt64LittleEndian(record[16..24]),
                BinaryPrimitives.ReadUInt64LittleEndian(record[24..32]));
        }

        var entries = new List<PacEntry>((int)count);
        foreach (var item in records)
        {
            if (item.NameOffset >= (ulong)fileInfo.Length || item.Size > (ulong)fileInfo.Length
                || item.Offset > (ulong)fileInfo.Length - item.Size)
                throw new InvalidDataException(UiText.T("error.pac.offset"));
            stream.Position = checked((long)item.NameOffset);
            var name = ReadUtf8Name(stream, 4096);
            entries.Add(new PacEntry
            {
                Name = name,
                Offset = checked((long)item.Offset),
                Size = checked((long)item.Size),
                Hash = item.Hash
            });
        }
        return new PacArchive(path, entries, path);
    }

    /// <summary>Combine a game's FPAC entries with loose overrides at their game-relative paths.</summary>
    public static PacArchive? LoadGameResources(string gameRoot, string archiveName, bool required,
        params string[] looseDirectories)
    {
        var root = System.IO.Path.GetFullPath(gameRoot);
        var expected = System.IO.Path.Combine(root, "pac", "steam", archiveName);
        var archive = FindGameArchive(expected, looseDirectories);
        var entries = new Dictionary<string, PacEntry>(StringComparer.OrdinalIgnoreCase);
        if (archive is not null)
            foreach (var entry in archive.Entries) entries.Add(entry.Name, entry);

        foreach (var relativeDirectory in looseDirectories)
        {
            var directory = System.IO.Path.Combine(root, relativeDirectory.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(root, file).Replace('\\', '/');
                var suffix = System.IO.Path.GetExtension(file);
                if (relativeDirectory.EndsWith("/model", StringComparison.OrdinalIgnoreCase) &&
                    !suffix.Equals(".mdl", StringComparison.OrdinalIgnoreCase)) continue;
                if (relativeDirectory.EndsWith("/image", StringComparison.OrdinalIgnoreCase) &&
                    !suffix.Equals(".dds", StringComparison.OrdinalIgnoreCase)) continue;
                if (relativeDirectory.EndsWith("/model_info", StringComparison.OrdinalIgnoreCase) &&
                    !suffix.Equals(".mi", StringComparison.OrdinalIgnoreCase) &&
                    !suffix.Equals(".mdl", StringComparison.OrdinalIgnoreCase)) continue;
                entries[relative] = new PacEntry
                {
                    Name = relative,
                    Size = new FileInfo(file).Length,
                    LoosePath = file
                };
            }
        }

        if (archive is null && entries.Count == 0)
        {
            if (!required) return null;
            throw new FileNotFoundException(UiText.F("error.model.sources.missing", expected,
                System.IO.Path.Combine(root, "asset", "common", "model")));
        }
        return new PacArchive(archive?.Path ?? expected, entries.Values.ToList(), archive?.ArchivePath);
    }

    private static PacArchive? FindGameArchive(string expected, IReadOnlyList<string> looseDirectories)
    {
        if (File.Exists(expected)) return Load(expected);
        var directory = System.IO.Path.GetDirectoryName(expected)!;
        if (!Directory.Exists(directory)) return null;
        var stem = System.IO.Path.GetFileNameWithoutExtension(expected);
        var candidates = new List<PacArchive>();
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            if (!System.IO.Path.GetFileName(file).StartsWith(stem, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var candidate = Load(file);
                if (candidate.Entries.Any(entry => looseDirectories.Any(prefix =>
                    entry.Name.StartsWith(prefix.TrimEnd('/') + '/', StringComparison.OrdinalIgnoreCase))))
                    candidates.Add(candidate);
            }
            catch (InvalidDataException) { /* A similarly named file is not a readable FPAC. */ }
        }
        if (candidates.Count > 1)
            throw new InvalidDataException(UiText.F("error.archive.ambiguous", stem));
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public void CopyEntryTo(PacEntry entry, string destination)
    {
        var destinationPath = System.IO.Path.GetFullPath(destination);
        var parent = Directory.GetParent(destinationPath)?.FullName
            ?? throw new InvalidOperationException(UiText.T("error.archive.parent"));
        Directory.CreateDirectory(parent);

        using var source = File.OpenRead(entry.LoosePath ?? _archivePath
            ?? throw new FileNotFoundException(UiText.T("error.pac.missing")));
        if (entry.LoosePath is null) source.Position = entry.Offset;
        using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        CopyExactly(source, target, entry.Size);
    }

    public byte[] ReadEntry(PacEntry entry)
    {
        if (entry.Size > int.MaxValue) throw new InvalidDataException(UiText.T("error.pac.preview.large"));
        using var source = File.OpenRead(entry.LoosePath ?? _archivePath
            ?? throw new FileNotFoundException(UiText.T("error.pac.missing")));
        if (entry.LoosePath is null) source.Position = entry.Offset;
        var bytes = new byte[checked((int)entry.Size)];
        ReadExactly(source, bytes);
        return bytes;
    }

    private static string ReadUtf8Name(FileStream stream, int maxLength)
    {
        var bytes = new List<byte>(64);
        for (var i = 0; i < maxLength; i++)
        {
            var value = stream.ReadByte();
            if (value < 0) throw new InvalidDataException(UiText.T("error.pac.name.truncated"));
            if (value == 0) return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add((byte)value);
        }
        throw new InvalidDataException(UiText.T("error.pac.name.long"));
    }

    private static void CopyExactly(Stream source, Stream target, long length)
    {
        var buffer = new byte[1024 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) throw new EndOfStreamException(UiText.T("error.pac.data.truncated"));
            target.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read <= 0) throw new EndOfStreamException(UiText.T("error.pac.file.truncated"));
            buffer = buffer[read..];
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
        => ReadExactly(stream, buffer.AsSpan());
}

public static class CharacterScanner
{
    public static List<CharacterRecord> Build(PacArchive modelArchive, PacArchive? modelInfoArchive, PacArchive? imageArchive)
    {
        var imageEntries = imageArchive?.Entries ?? Array.Empty<PacEntry>();
        var records = new List<CharacterRecord>();
        var edition = GameEditionInfo.Detect(GameEditionInfo.RootFromModelArchive(modelArchive.Path));
        var archiveDirectory = System.IO.Path.GetDirectoryName(modelArchive.Path)!;
        var names = edition == GameEdition.First
            ? CharacterNames.Load(archiveDirectory) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var detailedNames = CharacterNames.LoadDetails(archiveDirectory);
        foreach (var entry in modelArchive.Entries)
        {
            var slash = entry.Name.LastIndexOf('/');
            var fileName = slash >= 0 ? entry.Name[(slash + 1)..] : entry.Name;
            if (!entry.Name.StartsWith("asset/common/model/", StringComparison.OrdinalIgnoreCase)
                || !IsCharacterModelFile(fileName)) continue;

            var id = fileName[..^4];
            var face = ChoosePreview(imageEntries, id);
            var info = FindModelInfo(modelInfoArchive, id);
            detailedNames.TryGetValue(id, out var nameInfo);
            var catalogName = edition == GameEdition.Second ? CharacterAgeCatalog.Get(id, edition: edition).Name : "";
            // A definition can identify an outfit more precisely than an age-catalog
            // person label (for example, a model retained from the previous game).
            var displayName = edition == GameEdition.Second && nameInfo?.IsDefinition == true
                ? nameInfo.Label
                : !string.IsNullOrWhiteSpace(catalogName) ? catalogName
                : nameInfo?.Label ?? (names.TryGetValue(id, out var known) ? known : $"未登记名称 · {id}");
            records.Add(new CharacterRecord
            {
                ModelId = id,
                DisplayName = displayName,
                NameSourceKey = nameInfo is null ? "second.name.missing"
                    : nameInfo.IsDefinition ? "second.name.definition" : "second.name.scene",
                NameAliasCount = nameInfo?.DistinctLabels ?? 0,
                ModelEntry = entry,
                ModelInfoEntry = info,
                PreviewEntry = face,
                IsSupportedShapeEdit = true,
                IsBaseGameCharacter = true,
                AgeDefinitionLabel = nameInfo?.IsDefinition == true ? nameInfo.Label : null,
                Edition = edition
            });
        }
        return records.OrderBy(x => names.ContainsKey(x.ModelId) || detailedNames.ContainsKey(x.ModelId) ? 0 : 1)
            .ThenBy(x => x.ModelId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsCharacterModelFile(string fileName)
    {
        if (!fileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)) return false;
        var id = fileName.AsSpan(0, fileName.Length - 4);
        if (id.Length is not (7 or 11) || !id[..3].Equals("chr", StringComparison.OrdinalIgnoreCase))
            return false;
        for (var i = 3; i < 7; i++) if (!char.IsAsciiDigit(id[i])) return false;
        // Full costume models use chr####_c##. Other suffixed MDLs are
        // animations, props or mesh parts and cannot be edited as outfits.
        return id.Length == 7 || (id[7] == '_' && (id[8] is 'c' or 'C')
            && char.IsAsciiDigit(id[9]) && char.IsAsciiDigit(id[10]));
    }

    private static PacEntry? FindModelInfo(PacArchive? archive, string modelId)
    {
        if (archive is null) return null;
        return archive.Entries.FirstOrDefault(entry =>
            System.IO.Path.GetFileName(entry.Name).Equals(modelId + ".mdl", StringComparison.OrdinalIgnoreCase));
    }

    private static PacEntry? ChoosePreview(IReadOnlyList<PacEntry> entries, string modelId)
    {
        var prefix = "fc_" + modelId;
        var candidates = entries.Where(entry =>
            entry.Name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
            && entry.Name[(entry.Name.LastIndexOf('/') + 1)..].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) return null;
        return candidates.OrderBy(PreviewRank).ThenBy(entry => entry.Name.Length).First();
    }

    private static int PreviewRank(PacEntry entry)
    {
        var name = entry.Name.ToLowerInvariant();
        var rank = 100;
        if (name.Contains("e50")) rank -= 30;
        if (name.Contains("e00")) rank -= 20;
        if (name.Contains("e51")) rank -= 10;
        if (name.EndsWith("_a.dds")) rank -= 5;
        if (name.Contains("h")) rank += 5;
        return rank;
    }
}
