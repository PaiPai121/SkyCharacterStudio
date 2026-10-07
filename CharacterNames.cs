using System.IO;
using System.Text;

namespace Sky1stCharacterStudio;

public sealed record CharacterNameInfo(string Label, bool IsDefinition, int DistinctLabels);
public sealed record CostumeNameInfo(uint ItemId, string Chinese, string English)
{
    public string LocalizedName => UiText.IsEnglish ? English : Chinese;
}

public static class CharacterNames
{
    private static byte[]? ReadOptionalTable(string archiveDirectory, string language, string tableName)
    {
        var gameRoot = Path.GetFullPath(Path.Combine(archiveDirectory, "..", ".."));
        var folder = "table_" + language;
        var loose = Path.Combine(gameRoot, folder, tableName);
        if (File.Exists(loose)) return File.ReadAllBytes(loose);
        var path = Path.Combine(archiveDirectory, folder + ".pac");
        if (!File.Exists(path)) return null;
        var pac = PacArchive.Load(path);
        return pac.TryGet(folder + "/" + tableName, out var entry) ? pac.ReadEntry(entry) : null;
    }

    private static IEnumerable<int> TableRows(byte[] data, string section, uint stride)
    {
        if (data.Length < 8 || Encoding.ASCII.GetString(data, 0, 4) != "#TBL")
            throw new InvalidDataException(UiText.T("error.names.format"));
        var headers = BitConverter.ToUInt32(data, 4);
        if (8L + headers * 80L > data.Length)
            throw new InvalidDataException(UiText.T("error.names.bounds"));
        for (var i = 0; i < headers; i++)
        {
            var header = checked(8 + (int)i * 80);
            if (Encoding.ASCII.GetString(data, header, 64).TrimEnd('\0') != section) continue;
            var start = BitConverter.ToUInt32(data, header + 68);
            var rowSize = BitConverter.ToUInt32(data, header + 72);
            var count = BitConverter.ToUInt32(data, header + 76);
            if (rowSize != stride || start + (ulong)rowSize * count > (ulong)data.Length)
                throw new InvalidDataException(UiText.T("error.names.layout"));
            for (uint row = 0; row < count; row++)
                yield return checked((int)(start + row * rowSize));
            yield break;
        }
    }

    private static string TableText(byte[] data, ulong offset)
    {
        if (offset == 0) return "";
        if (offset >= (ulong)data.Length)
            throw new InvalidDataException(UiText.T("error.names.bounds"));
        var start = checked((int)offset);
        var end = start;
        while (end < data.Length && data[end] != 0 && end - start < 4096) end++;
        if (end == data.Length || end - start >= 4096)
            throw new InvalidDataException(UiText.T("error.names.terminated"));
        return Encoding.UTF8.GetString(data, start, end - start).Trim();
    }

    private static Dictionary<string, uint> ParseCostumeItems(byte[] data)
    {
        var result = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in TableRows(data, "CostumeParam", 56))
        {
            var itemId = BitConverter.ToUInt16(data, row + 4);
            var model = TableText(data, BitConverter.ToUInt64(data, row + 16));
            if (itemId == 0 || !model.StartsWith("chr", StringComparison.OrdinalIgnoreCase)) continue;
            if (result.TryGetValue(model, out var previous) && previous != itemId)
                ambiguous.Add(model);
            else result[model] = itemId;
        }
        foreach (var model in ambiguous) result.Remove(model);
        return result;
    }

    private static Dictionary<uint, string> ParseItemNames(byte[] data)
    {
        var result = new Dictionary<uint, string>();
        foreach (var row in TableRows(data, "ItemTableData", 256))
        {
            var itemId = BitConverter.ToUInt32(data, row);
            var name = TableText(data, BitConverter.ToUInt64(data, row + 224));
            if (!string.IsNullOrWhiteSpace(name)) result[itemId] = name;
        }
        return result;
    }

    public static Dictionary<string, CostumeNameInfo> LoadCostumeDetails(string archiveDirectory)
    {
        try
        {
            var costume = ReadOptionalTable(archiveDirectory, "sc", "t_costume.tbl")
                ?? ReadOptionalTable(archiveDirectory, "en", "t_costume.tbl");
            if (costume is null) return new(StringComparer.OrdinalIgnoreCase);
            var chinese = ReadOptionalTable(archiveDirectory, "sc", "t_item.tbl");
            var english = ReadOptionalTable(archiveDirectory, "en", "t_item.tbl");
            var chineseNames = chinese is null ? new Dictionary<uint, string>() : ParseItemNames(chinese);
            var englishNames = english is null ? new Dictionary<uint, string>() : ParseItemNames(english);
            var result = new Dictionary<string, CostumeNameInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var (model, itemId) in ParseCostumeItems(costume))
            {
                chineseNames.TryGetValue(itemId, out var sc);
                englishNames.TryGetValue(itemId, out var en);
                if (string.IsNullOrWhiteSpace(sc) && string.IsNullOrWhiteSpace(en)) continue;
                result[model] = new CostumeNameInfo(itemId, sc ?? en!, en ?? sc!);
            }
            return result;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Outfit labels are optional. An incomplete modded table must not
            // prevent scanning the model PAC or its loose replacements.
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

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
