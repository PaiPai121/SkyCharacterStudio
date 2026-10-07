using System.IO;
using System.Text.RegularExpressions;

namespace Sky1stCharacterStudio;

public sealed record RecentGameModelUse(DateTime LogTimeLocal,
    IReadOnlyDictionary<string, string> LastRequestedByCharacter);

public static class RecentGameModelLog
{
    private const long MaxReadBytes = 64L * 1024 * 1024;
    private static readonly Regex ModelRequest = new(
        @"[/\\]asset[/\\]common[/\\]model[/\\](?<id>chr[0-9]{4}(?:_c[0-9]{2})?)\.mdl'$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static RecentGameModelUse? Read(string gameRoot)
    {
        var path = Path.Combine(gameRoot, "sora2looseload.log");
        if (!File.Exists(path)) return null;
        try
        {
            var logTime = File.GetLastWriteTime(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var fromTail = stream.Length > MaxReadBytes;
            if (fromTail) stream.Seek(-MaxReadBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            if (fromTail) reader.ReadLine(); // Discard a potentially partial first line.
            var last = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line == "---- Log Started ----")
                {
                    last.Clear();
                    continue;
                }
                if (!line.StartsWith("[MOD] Checking standard loose file: '",
                        StringComparison.Ordinal)) continue;
                var match = ModelRequest.Match(line);
                if (!match.Success) continue;
                var modelId = match.Groups["id"].Value.ToLowerInvariant();
                last[modelId[..7]] = modelId;
            }
            return last.Count == 0 ? null : new RecentGameModelUse(logTime, last);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // This optional hint must never prevent the ordinary resource scan.
            return null;
        }
    }
}
