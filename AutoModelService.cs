using System.Diagnostics;
using System.IO;
using System.Text;
namespace Sky1stCharacterStudio;
public static class AutoModelService {
 private static readonly SemaphoreSlim gate=new(1);
 public static async Task Run(string game,string model,string mode,string output,int? strength=null,bool isBaseGameCharacter=false) {
  await gate.WaitAsync();
  try {
   var helper=PreviewService.FindFileUpwards("tools","auto_model.py") ?? throw new FileNotFoundException(UiText.T("error.no.model"));
   var start=new ProcessStartInfo(PreviewService.ResolvePython()) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
   foreach(var arg in new[]{helper,"--game",game,"--model",model,"--mode",mode,"--out",output}) start.ArgumentList.Add(arg);
   if(isBaseGameCharacter)start.ArgumentList.Add("--base-game-character");
   if(strength.HasValue) {start.ArgumentList.Add("--export");start.ArgumentList.Add("--strength");start.ArgumentList.Add(strength.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));}
   using var p=Process.Start(start) ?? throw new IOException(UiText.T("error.processor.start"));
   var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
   await p.WaitForExitAsync();await stdout;var error=await stderr;
   if(p.ExitCode!=0) throw new InvalidDataException(error);
  } finally {gate.Release();}
 }
}
