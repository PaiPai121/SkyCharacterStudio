using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sky1stCharacterStudio;

public static class GameInstaller
{
    public static string ValidateGameRoot(string path)
    {
        var root = Path.GetFullPath(path.Trim());
        if (!File.Exists(Path.Combine(root, "sora_1st.exe")) ||
            !File.Exists(Path.Combine(root, "pac", "steam", "asset_common_model.pac")))
            throw new InvalidOperationException("请选择同时包含 sora_1st.exe 和 pac 文件夹的游戏根目录。");
        return root;
    }

    public static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("sora_1st");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static string SafePath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("安装路径超出目标目录。");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("安装目标或来源包含链接目录，请选择实际目录：" + current);
        }
        return path;
    }

    public static string Install(string package, string gameRoot, string backupRoot, Func<bool>? runningCheck = null)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        runningCheck ??= IsGameRunning;
        if (runningCheck()) throw new InvalidOperationException("游戏正在运行，请先退出游戏再安装。");
        var compatibility=PreviewService.FindFileUpwards("assets","supported-game.json");
        if(compatibility is not null) {
            using var supported=JsonDocument.Parse(File.ReadAllText(compatibility));
            var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(gameRoot,"sora_1st.exe"))));
            if(hash!=supported.RootElement.GetProperty("sha256").GetString())throw new InvalidOperationException("当前游戏程序不在已验证版本中。此测试版仅支持云豹 Steam 1.0.5.0，未安装任何文件。");
        }
        package = Path.GetFullPath(package);
        foreach (var required in new[] { "xinput1_4.dll", "ED9Loader", "Mod/ScherazardSummon/asset/common/model" })
            if (!File.Exists(Path.Combine(package,required)) && !Directory.Exists(Path.Combine(package,required)))
                throw new InvalidDataException("测试包不完整：" + required);
        var files = new List<string> { "xinput1_4.dll" };
        foreach (var folder in new[] { "ED9Loader", "Mod" })
        {
            var options = new EnumerationOptions { RecurseSubdirectories=true, AttributesToSkip=0 };
            // Check directories before recursive enumeration to reject junctions.
            var pending = new Stack<string>(); pending.Push(Path.Combine(package,folder));
            while (pending.Count>0) {
                var directory=pending.Pop(); SafePath(package,Path.GetRelativePath(package,directory));
                foreach(var child in Directory.GetDirectories(directory)) pending.Push(child);
            }
            files.AddRange(Directory.GetFiles(Path.Combine(package,folder),"*",options).Select(p=>Path.GetRelativePath(package,p)));
        }
        var backup = Path.GetFullPath(Path.Combine(backupRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")));
        var existed = new Dictionary<string,bool>();
        foreach(var relative in files) {
            SafePath(package,relative); var target=SafePath(gameRoot,relative);
            if(Directory.Exists(target)) throw new IOException("文件目标被同名目录占用："+target);
            existed[relative]=File.Exists(target);
        }
        Directory.CreateDirectory(backup);
        foreach(var relative in files.Where(r=>existed[r])) {
            var saved=SafePath(backup,relative); Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            File.Copy(Path.Combine(gameRoot,relative),saved);
        }
        var manifest=Path.Combine(backup,"installation.json");
        var installedHashes=files.ToDictionary(r=>r,r=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SafePath(package,r)))));
        void Record(string state) => File.WriteAllText(manifest,JsonSerializer.Serialize(new { gameRoot, package, state, files=existed,installedHashes },new JsonSerializerOptions { WriteIndented=true }));
        Record("backed-up");
        var touched=new List<string>();
        try {
            foreach(var relative in files) {
                if(runningCheck()) throw new IOException("检测到游戏启动，停止安装并还原。");
                var target=SafePath(gameRoot,relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                touched.Add(relative); File.Copy(Path.Combine(package,relative),target,true);
                using var a=File.OpenRead(Path.Combine(package,relative)); using var b=File.OpenRead(target);
                if(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new IOException("安装校验失败："+relative);
            }
            Record("installed");
            return backup;
        } catch(Exception failure) {
            var errors=new List<Exception>();
            foreach(var relative in touched.AsEnumerable().Reverse()) try {
                var target=SafePath(gameRoot,relative);
                if(existed[relative]) File.Copy(Path.Combine(backup,relative),target,true);
                else File.Delete(target);
            } catch(Exception error) { errors.Add(error); }
            Record(errors.Count==0 ? "rolled-back" : "rollback-incomplete");
            if(errors.Count>0) throw new AggregateException("安装失败，部分文件还原失败。备份："+backup,errors.Prepend(failure));
            throw new IOException("安装失败，已还原本次修改。备份："+backup,failure);
        }
    }

    public static string RestoreLatest(string gameRoot,string backupRoot,Func<bool>? runningCheck=null)
    {
        gameRoot=ValidateGameRoot(gameRoot);runningCheck??=IsGameRunning;
        if(runningCheck())throw new InvalidOperationException("请先退出游戏再恢复。");
        if(!Directory.Exists(backupRoot))throw new IOException("没有可恢复的安装备份。");
        foreach(var file in Directory.GetFiles(backupRoot,"installation.json",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc)) {
            var doc=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            if(doc["state"]?.GetValue<string>()!="installed" || !string.Equals(doc["gameRoot"]?.GetValue<string>(),gameRoot,StringComparison.OrdinalIgnoreCase))continue;
            var backup=Path.GetDirectoryName(file)!;
            var originals=new Dictionary<string,byte[]?>();var current=new Dictionary<string,byte[]?>();
            foreach(var pair in doc["files"]!.AsObject()) {
                var target=SafePath(gameRoot,pair.Key);var saved=SafePath(backup,pair.Key);
                current[pair.Key]=File.Exists(target)?File.ReadAllBytes(target):null;
                var expected=doc["installedHashes"]?[pair.Key]?.GetValue<string>();
                if(expected is not null && (current[pair.Key] is null || Convert.ToHexString(SHA256.HashData(current[pair.Key]!))!=expected))
                    throw new IOException("安装后文件已被其他程序修改，为避免覆盖，停止恢复："+pair.Key);
                originals[pair.Key]=pair.Value!.GetValue<bool>()?File.ReadAllBytes(saved):null;
            }
            var touched=new List<string>();
            try {
                foreach(var pair in originals) {
                    if(runningCheck())throw new IOException("游戏启动，停止恢复。");
                    var target=SafePath(gameRoot,pair.Key);touched.Add(pair.Key);
                    if(pair.Value is null)File.Delete(target);else File.WriteAllBytes(target,pair.Value);
                }
                doc["state"]="restored";File.WriteAllText(file,doc.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
                return backup;
            } catch {
                foreach(var relative in touched) {
                    var target=SafePath(gameRoot,relative);
                    if(current[relative] is null)File.Delete(target);else File.WriteAllBytes(target,current[relative]!);
                }
                throw;
            }
        }
        throw new IOException("没有此游戏目录尚未撤销的安装记录。");
    }
}
