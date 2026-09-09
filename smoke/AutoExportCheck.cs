using System.IO;
using System.Security.Cryptography;
using Sky1stCharacterStudio;
class AutoExportCheck {
 static async Task Main() {
  var root=@"D:\work_console\Sky1stCharacterStudio";var game=@"D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st";
  var archive=PacArchive.Load(Path.Combine(game,"pac/steam/asset_common_model.pac"));
  var record=CharacterScanner.Build(archive,null,null).Single(r=>r.ModelId=="chr5004");
  var output=await ExportService.ExportAsync(record,archive,root,100,true,CancellationToken.None,"width");
  var package=output.RuntimePackagePath!;
  var target=Path.Combine(package,"Mod/ScherazardSummon/asset/common/model/chr5004.mdl");
  if(!File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(output.ModelPath)))throw new Exception("Package model mismatch");
  if(File.Exists(Path.Combine(package,"Mod/ScherazardSummon/asset/common/model/chr5002.mdl")))throw new Exception("Would reset other character");
  var dll=Path.Combine(package,"ED9Loader/plugins/SceneRedirect.dll");
  if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)))!="C1717046B0BA485394D4951D8F11C283388E7920C735E0E639A4C70D457CC753")throw new Exception("Old loader regression");
  Console.WriteLine("PASS generic export package: selected model matches, other character preserved, fixed loader retained");
 }
}
