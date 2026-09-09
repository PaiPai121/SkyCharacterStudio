using System.IO;
using Sky1stCharacterStudio;
var root=Path.Combine(@"D:\work_console\Sky1stCharacterStudio\cache","安装 测试-"+Guid.NewGuid().ToString("N"));
var game=Path.Combine(root,"游戏目录");
Directory.CreateDirectory(Path.Combine(game,"pac/steam"));
File.WriteAllText(Path.Combine(game,"sora_1st.exe"),"fixture");
File.WriteAllText(Path.Combine(game,"pac/steam/asset_common_model.pac"),"do not touch");
File.WriteAllText(Path.Combine(game,"xinput1_4.dll"),"old proxy");
var package=@"D:\work_console\Sky1stCharacterStudio\exports\chr5002_shape_097\Scherazard_Runtime";
var backup=GameInstaller.Install(package,game,Path.Combine(root,"backup"),()=>false);
if(File.ReadAllText(Path.Combine(backup,"xinput1_4.dll"))!="old proxy") throw new Exception("Backup mismatch");
foreach(var folder in new[]{"ED9Loader","Mod"}) foreach(var file in Directory.GetFiles(Path.Combine(package,folder),"*",SearchOption.AllDirectories)) {
 if(!File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(game,Path.GetRelativePath(package,file))))) throw new Exception("Installed mismatch");
}
File.WriteAllText(Path.Combine(game,"xinput1_4.dll"),"before interrupted update");
var count=0;
try { GameInstaller.Install(package,game,Path.Combine(root,"backup"),()=>++count>3); throw new Exception("Did not reject running game"); }
catch(IOException) { }
if(File.ReadAllText(Path.Combine(game,"xinput1_4.dll"))!="before interrupted update") throw new Exception("Rollback mismatch");
try { GameInstaller.Install(package,game,Path.Combine(root,"backup"),()=>true); throw new Exception("Running game accepted"); }
catch(InvalidOperationException) { }
try { GameInstaller.ValidateGameRoot(root); throw new Exception("Invalid folder accepted"); }
catch(InvalidOperationException) { }
if(File.ReadAllText(Path.Combine(game,"pac/steam/asset_common_model.pac"))!="do not touch") throw new Exception("PAC changed");
Console.WriteLine("PASS: actual package installed in custom Unicode path; backup verified; byte comparison; running-game rejection; interrupted installation rollback; invalid directory rejection; PAC untouched");
Console.WriteLine(root);
