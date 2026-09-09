using System.IO;
using Sky1stCharacterStudio;
class NamesCheck {
 static void Main() {
 var root=@"D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st\pac\steam";
 var names=CharacterNames.Load(root);
 foreach(var p in new[]{("chr0003","奥利维尔"),("chr0005","阿加特"),("chr0007","阵"),("chr5002","雪拉扎德")})if(!names[p.Item1].Contains(p.Item2))throw new Exception("Wrong identity");
 var records=CharacterScanner.Build(PacArchive.Load(Path.Combine(root,"asset_common_model.pac")),null,null);
 Console.WriteLine($"Mapped {records.Count(r=>names.ContainsKey(r.ModelId))}/{records.Count} scanned models from game name table");
 Console.WriteLine("Unregistered: "+string.Join(",",records.Where(r=>!names.ContainsKey(r.ModelId)).Select(r=>r.ModelId)));
 try{CharacterNames.Parse(new byte[8]);throw new Exception("Invalid data accepted");}catch(InvalidDataException){}
 Console.WriteLine("PASS known model identities and malformed table rejection");
 }
}
