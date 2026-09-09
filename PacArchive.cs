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

    private PacArchive(string path, List<PacEntry> entries)
    {
        Path = path;
        Entries = entries;
        _byName = entries.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public string Path { get; }
    public IReadOnlyList<PacEntry> Entries { get; }

    public bool TryGet(string name, out PacEntry entry) => _byName.TryGetValue(name, out entry!);

    public static PacArchive Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 PAC 文件", path);
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length < 16) throw new InvalidDataException("PAC 文件过小");

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[16];
        ReadExactly(stream, header);
        if (Encoding.ASCII.GetString(header[..4]) != "FPAC")
            throw new InvalidDataException("只支持 FPAC 资源包");

        var count = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        if (count > 2_000_000) throw new InvalidDataException("PAC 条目数异常");
        var recordsBytes = checked((long)count * 32L);
        if (16L + recordsBytes > fileInfo.Length) throw new InvalidDataException("PAC 条目表超出文件范围");

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
                throw new InvalidDataException("PAC 条目偏移无效");
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
        return new PacArchive(path, entries);
    }

    public void CopyEntryTo(PacEntry entry, string destination)
    {
        var destinationPath = System.IO.Path.GetFullPath(destination);
        var parent = Directory.GetParent(destinationPath)?.FullName
            ?? throw new InvalidOperationException("输出路径无父目录");
        Directory.CreateDirectory(parent);

        using var source = File.OpenRead(Path);
        source.Position = entry.Offset;
        using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        CopyExactly(source, target, entry.Size);
    }

    public byte[] ReadEntry(PacEntry entry)
    {
        if (entry.Size > int.MaxValue) throw new InvalidDataException("预览条目过大");
        using var source = File.OpenRead(Path);
        source.Position = entry.Offset;
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
            if (value < 0) throw new InvalidDataException("PAC 文件名被截断");
            if (value == 0) return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add((byte)value);
        }
        throw new InvalidDataException("PAC 文件名过长");
    }

    private static void CopyExactly(Stream source, Stream target, long length)
    {
        var buffer = new byte[1024 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) throw new EndOfStreamException("PAC 条目数据被截断");
            target.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read <= 0) throw new EndOfStreamException("PAC 文件被截断");
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
        var names=CharacterNames.Load(System.IO.Path.GetDirectoryName(modelArchive.Path)!);
        foreach (var entry in modelArchive.Entries)
        {
            var slash = entry.Name.LastIndexOf('/');
            var fileName = slash >= 0 ? entry.Name[(slash + 1)..] : entry.Name;
            if (!entry.Name.StartsWith("asset/common/model/", StringComparison.OrdinalIgnoreCase)
                || !fileName.StartsWith("chr", StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains('_')) continue;

            var id = fileName[..^4];
            if (id.Length < 4) continue;
            var face = ChoosePreview(imageEntries, id);
            var info = FindModelInfo(modelInfoArchive, id);
            records.Add(new CharacterRecord
            {
                ModelId = id,
                DisplayName = names.TryGetValue(id, out var known) ? known : $"未登记名称 · {id}",
                ModelEntry = entry,
                ModelInfoEntry = info,
                PreviewEntry = face,
                IsSupportedShapeEdit = true
            });
        }
        return records.OrderBy(x => names.ContainsKey(x.ModelId) ? 0 : 1)
            .ThenBy(x => x.ModelId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static PacEntry? FindModelInfo(PacArchive? archive, string modelId)
    {
        if (archive is null) return null;
        return archive.Entries.FirstOrDefault(entry =>
            entry.Name.Contains('/' + modelId, StringComparison.OrdinalIgnoreCase)
            && entry.Name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase));
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