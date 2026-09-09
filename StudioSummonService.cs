using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sky1stCharacterStudio;

public static class StudioSummonService
{
    public static async Task ConfigureAsync(string package, string game, string modelId, string displayName)
    {
        if (!Regex.IsMatch(modelId, @"\Achr[0-9]{4}\z")) throw new InvalidDataException("角色模型编号格式错误");
        var mod=Path.Combine(package,"Mod","ScherazardSummon");
        var dat=Path.Combine(mod,"ScherazardSummon.dat");
        var models=Path.Combine(mod,"asset","common","model");
        if(!File.Exists(dat)) {
            var scripts=PacArchive.Load(Path.Combine(game,"pac","steam","script_sc.pac"));
            if(!scripts.TryGet("script_sc/scena/mp0000_ev.dat",out var entry))throw new InvalidDataException("找不到召唤脚本所需的游戏函数");
            File.WriteAllBytes(dat,scripts.ReadEntry(entry));
        }
        if (!File.Exists(Path.Combine(models,modelId+".mdl"))) throw new FileNotFoundException("召唤目标模型未安装到测试包");
        var original=Path.Combine(package,"studio-original");
        await AutoModelService.Run(game,modelId,"width",original,0);
        File.Copy(Path.Combine(original,modelId+".mdl"),Path.Combine(models,"chr_studio_original.mdl"),true);
        var builder=PreviewService.FindFileUpwards("tools","build_summon_dat.exe") ?? throw new FileNotFoundException("缺少召唤脚本生成器");
        var start=new ProcessStartInfo(builder) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{dat,dat+".new",modelId})start.ArgumentList.Add(arg);
        using(var process=Process.Start(start) ?? throw new IOException("召唤脚本生成器启动失败")) {
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();await stdout;var error=await stderr;
            if(process.ExitCode!=0)throw new InvalidDataException("召唤脚本生成失败："+error);
        }
        File.Move(dat+".new",dat,true);
        var ini=Path.Combine(package,"ED9Loader","config","EventStarter.ini");
        var settings=File.ReadAllText(ini);
        settings=Regex.Replace(settings,@"(?m)^enabled\s*=.*$","enabled=1");
        foreach(var setting in new[]{("character","3"),("model_mode","1")}) {
            var pattern=@"(?m)^"+setting.Item1+@"\s*=.*$";
            if(!Regex.IsMatch(settings,pattern))throw new InvalidDataException("召唤配置缺少 "+setting.Item1);
            settings=Regex.Replace(settings,pattern,setting.Item1+"="+setting.Item2);
        }
        File.WriteAllText(ini,settings,Encoding.ASCII);
        File.WriteAllText(Path.Combine(mod,"add_dat_ini.json"),"{\"inject\":[{\"map\":\"system\",\"script\":\"ScherazardSummon\",\"file\":\"ScherazardSummon.dat\"}]}");
        await File.WriteAllTextAsync(Path.Combine(mod,"character-studio-selection.json"),JsonSerializer.Serialize(new {
            modelId,displayName,editedEvent="StudioSummon",originalEvent="StudioSummonOriginal",hotkey="F8",toggle="F9",installedSelection=true
        },new JsonSerializerOptions{WriteIndented=true}),Encoding.UTF8);
    }
}
