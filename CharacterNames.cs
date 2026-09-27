using System.IO;
using System.Text;

namespace Sky1stCharacterStudio;

public sealed record CharacterNameInfo(string Label, bool IsDefinition, int DistinctLabels);

public static class CharacterNames
{
    private static byte[]? ReadTable(string archiveDirectory)
    {
        var gameRoot = Path.GetFullPath(Path.Combine(archiveDirectory, "..", ".."));
        foreach (var language in new[] { "sc", "tc", "kr" })
        {
            var loose = Path.Combine(gameRoot, $"table_{language}", "t_name.tbl");
            if (File.Exists(loose)) return File.ReadAllBytes(loose);
            var path = Path.Combine(archiveDirectory, $"table_{language}.pac");
            if (!File.Exists(path)) continue;
            var pac = PacArchive.Load(path);
            if (pac.TryGet($"table_{language}/t_name.tbl", out var entry))
                return pac.ReadEntry(entry);
        }
        return null;
    }

    public static Dictionary<string, string> Load(string archiveDirectory)
    {
        var data = ReadTable(archiveDirectory);
        return data is null ? new(StringComparer.OrdinalIgnoreCase) : Parse(data);
    }

    public static Dictionary<string, CharacterNameInfo> LoadDetails(string archiveDirectory)
    {
        var data = ReadTable(archiveDirectory);
        return data is null ? new(StringComparer.OrdinalIgnoreCase) : ParseDetails(data);
    }

    // Keep the 1st Chapter's established multi-name display for compatibility.
    public static Dictionary<string, string> Parse(byte[] data)
        => ReadRows(data).ToDictionary(
            pair => pair.Key,
            pair => string.Join(" / ", pair.Value.Select(row => row.Name).Distinct().Take(3))
                + (pair.Value.Select(row => row.Name).Distinct().Skip(3).Any() ? " 等" : ""),
            StringComparer.OrdinalIgnoreCase);

    // A model ID can carry many scene aliases. Prefer its definition row;
    // an in-scene name is evidence of use, not proof of model identity.
    public static Dictionary<string, CharacterNameInfo> ParseDetails(byte[] data)
        => ReadRows(data).ToDictionary(
            pair => pair.Key,
            pair => new CharacterNameInfo(
                pair.Value.FirstOrDefault(row => row.IsDefinition).Name
                    ?? pair.Value[0].Name,
                pair.Value.Any(row => row.IsDefinition),
                pair.Value.Select(row => row.Name).Distinct().Count()),
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, List<(string Name, bool IsDefinition)>> ReadRows(byte[] data)
    {
        if (data.Length < 8 || Encoding.ASCII.GetString(data, 0, 4) != "#TBL")
            throw new InvalidDataException(UiText.T("error.names.format"));
        var headers = BitConverter.ToUInt32(data, 4);
        if (8L + headers * 80L > data.Length)
            throw new InvalidDataException(UiText.T("error.names.bounds"));

        string Text(ulong offset)
        {
            if (offset == 0) return "";
            if (offset >= (ulong)data.Length)
                throw new InvalidDataException("名称表字符串越界");
            var start = (int)offset;
            var end = start;
            while (end < data.Length && data[end] != 0 && end - start < 4096) end++;
            if (end == data.Length || end - start >= 4096)
                throw new InvalidDataException(UiText.T("error.names.terminated"));
            return Encoding.UTF8.GetString(data, start, end - start).Trim();
        }

        var names = new Dictionary<string, List<(string Name, bool IsDefinition)>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers; i++)
        {
            var h = 8 + i * 80;
            if (Encoding.ASCII.GetString(data, h, 64).TrimEnd('\0') != "NameTableData") continue;
            var start = BitConverter.ToUInt32(data, h + 68);
            var stride = BitConverter.ToUInt32(data, h + 72);
            var count = BitConverter.ToUInt32(data, h + 76);
            if (stride != 104 || start + (ulong)stride * count > (ulong)data.Length)
                throw new InvalidDataException(UiText.T("error.names.layout"));
            for (uint r = 0; r < count; r++)
            {
                var row = checked((int)(start + r * stride));
                var name = Text(BitConverter.ToUInt64(data, row + 8));
                var model = Text(BitConverter.ToUInt64(data, row + 16));
                if (name.Length == 0 || !model.StartsWith("chr", StringComparison.OrdinalIgnoreCase)) continue;
                if (!names.TryGetValue(model, out var list)) names[model] = list = [];
                list.Add((name, BitConverter.ToUInt16(data, row) == ushort.MaxValue));
            }
        }
        return names;
    }
}
