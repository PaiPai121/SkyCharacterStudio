using System.IO;
using System.Text;
namespace Sky1stCharacterStudio;
public static class CharacterNames {
 public static Dictionary<string,string> Load(string archiveDirectory) {
  foreach(var language in new[]{"sc","tc","kr"}) {
   var path=Path.Combine(archiveDirectory,$"table_{language}.pac");
   if(!File.Exists(path))continue;
   var pac=PacArchive.Load(path);
   if(!pac.TryGet($"table_{language}/t_name.tbl",out var entry))continue;
   return Parse(pac.ReadEntry(entry));
  }
  return new(StringComparer.OrdinalIgnoreCase);
 }
 public static Dictionary<string,string> Parse(byte[] data) {
  if(data.Length<8 || Encoding.ASCII.GetString(data,0,4)!="#TBL")throw new InvalidDataException(UiText.T("error.names.format"));
  uint headers=BitConverter.ToUInt32(data,4);
  if(8L+headers*80L>data.Length)throw new InvalidDataException(UiText.T("error.names.bounds"));
  string Text(ulong offset) {
   if(offset==0)return "";
   if(offset>=(ulong)data.Length)throw new InvalidDataException("名称表字符串越界");
   int start=(int)offset,end=start;
   while(end<data.Length && data[end]!=0 && end-start<4096)end++;
   if(end==data.Length || end-start>=4096)throw new InvalidDataException(UiText.T("error.names.terminated"));
   return Encoding.UTF8.GetString(data,start,end-start).Trim();
  }
  var names=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
  for(int i=0;i<headers;i++) {
   int h=8+i*80;
   if(Encoding.ASCII.GetString(data,h,64).TrimEnd('\0')!="NameTableData")continue;
   uint start=BitConverter.ToUInt32(data,h+68),stride=BitConverter.ToUInt32(data,h+72),count=BitConverter.ToUInt32(data,h+76);
   if(stride!=104 || start+(ulong)stride*count>(ulong)data.Length)throw new InvalidDataException(UiText.T("error.names.layout"));
   for(uint r=0;r<count;r++) {
    int row=checked((int)(start+r*stride));
    string name=Text(BitConverter.ToUInt64(data,row+8)),model=Text(BitConverter.ToUInt64(data,row+16));
    if(name.Length==0 || !model.StartsWith("chr",StringComparison.OrdinalIgnoreCase))continue;
    if(!names.TryGetValue(model,out var list))names[model]=list=[];
    if(!list.Contains(name))list.Add(name);
   }
  }
  return names.ToDictionary(x=>x.Key,x=>string.Join(" / ",x.Value.Take(3))+(x.Value.Count>3 ? " 等" : ""),StringComparer.OrdinalIgnoreCase);
 }
}
