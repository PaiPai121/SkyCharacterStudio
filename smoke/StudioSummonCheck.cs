using System.IO;
using System.Security.Cryptography;
using Sky1stCharacterStudio;
class StudioSummonCheck {
 static async Task Main() {
  var root=@"D:\work_console\Sky1stCharacterStudio";var game=@"D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st";
  var source=@"D:\work_console\Sky1st-Scherazard-Mod\dist\Scherazard_Runtime";
  var package=Path.Combine(root,"cache","julia-summon-update");
  Directory.CreateDirectory(package);
  foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) {
   var rel=Path.GetRelativePath(source,file);
   if(rel.EndsWith("chr5002.mdl",StringComparison.OrdinalIgnoreCase))continue;
   var to=Path.Combine(package,rel);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(file,to,true);
  }
  var selected=Path.Combine(game,"Mod/ScherazardSummon/asset/common/model/chr5107.mdl");
  var target=Path.Combine(package,"Mod/ScherazardSummon/asset/common/model/chr5107.mdl");File.Copy(selected,target,true);
  await StudioSummonService.ConfigureAsync(package,game,"chr5107","尤莉亚中尉");
  if(!File.ReadAllBytes(selected).SequenceEqual(File.ReadAllBytes(target)))throw new Exception("Installed edit changed");
  var ini=File.ReadAllText(Path.Combine(package,"ED9Loader/config/EventStarter.ini"));
  if(!ini.Contains("character=3") || !ini.Contains("model_mode=1"))throw new Exception("Wrong default selection");
  var dat=File.ReadAllBytes(Path.Combine(package,"Mod/ScherazardSummon/ScherazardSummon.dat"));
  var text=System.Text.Encoding.ASCII.GetString(dat);
  foreach(var name in new[]{"StudioSummon","StudioSummonOriginal","chr5107","chr_studio_original"})if(!text.Contains(name))throw new Exception("Missing script entry "+name);
  Console.WriteLine("PASS package generated from already-installed Julia, model bytes preserved, selected events/config present");
  File.WriteAllText(Path.Combine(root,"cache/julia-installed-sha256.txt"),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(selected))));
 }
}
