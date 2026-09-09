using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Sky1stCharacterStudio;

var root = @"D:\work_console\Sky1stCharacterStudio";
var pac = PacArchive.Load(@"D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st\pac\steam\asset_common_model.pac");
if (!pac.TryGet("asset/common/model/chr5002.mdl", out var entry)) throw new Exception("Missing model");
var record = new CharacterRecord { ModelId="chr5002", DisplayName="Scherazard", ModelEntry=entry, IsSupportedShapeEdit=true };
var results = new List<object>();
foreach (var strength in new[] { -500, -100, 0, 97, 100, 300, 1000 }) {
    var result = await ExportService.ExportAsync(record, pac, root, strength, strength == 97, CancellationToken.None);
    if (!result.ShapeEditApplied) throw new Exception("Export failed");
    var bytes = File.ReadAllBytes(result.ModelPath);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    if (strength == 0 && hash != "768D93A3327CCE025F53F2EC64E3AFC0E261BED092B0B532134644899B339E0A") throw new Exception("Zero is not original");
    if (strength == 97) {
        var packaged = File.ReadAllBytes(Path.Combine(result.RuntimePackagePath!, "Mod/ScherazardSummon/asset/common/model/chr5002.mdl"));
        if (!bytes.SequenceEqual(packaged)) throw new Exception("Runtime model mismatch");
    }
    results.Add(new { strength, hash, bytes=bytes.Length, packageVerified=strength==97 });
}
File.WriteAllText(Path.Combine(root,"cache/export_verification.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented=true }));
Console.WriteLine(JsonSerializer.Serialize(results));
