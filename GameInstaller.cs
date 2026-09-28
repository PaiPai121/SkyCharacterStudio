using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sky1stCharacterStudio;

public static class GameInstaller
{
    public static string ValidateGameRoot(string path)
    {
        var root = Path.GetFullPath(path.Trim());
        GameEditionInfo.Detect(root);
        return root;
    }

    public static void ValidateInstallTarget(string gameRoot)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        var edition = GameEditionInfo.Detect(gameRoot);
        var executablePath = Path.Combine(gameRoot, GameEditionInfo.ExecutableName(edition));
        try
        {
            using var executable = File.OpenRead(executablePath);
            using var pe = new PEReader(executable);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64
                || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32Plus)
                throw new InvalidOperationException(UiText.T("error.game.executable.format"));
        }
        catch (BadImageFormatException error)
        { throw new InvalidOperationException(UiText.T("error.game.executable.format"), error); }

        var models = PacArchive.LoadGameResources(gameRoot, "asset_common_model.pac", true,
            "asset/common/model")!;
        if (!models.Entries.Any(entry => entry.Name.StartsWith("asset/common/model/", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(entry.Name).StartsWith("chr", StringComparison.OrdinalIgnoreCase)
            && entry.Name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(UiText.T("error.model.none"));

        if (edition == GameEdition.First) ValidateFirstInstallBuild(executablePath);
    }

    private static void ValidateFirstInstallBuild(string executablePath)
    {
        // The 1st Chapter loader uses executable-specific addresses. The 2nd
        // Chapter loose-file loader is validated separately and has no EXE hash gate.
        var manifest = Path.Combine(AppContext.BaseDirectory, "assets", "first-install-builds.json");
        if (!File.Exists(manifest))
            throw new FileNotFoundException(UiText.T("error.first.builds.missing"), manifest);
        using var builds = JsonDocument.Parse(File.ReadAllText(manifest));
        using var executable = File.OpenRead(executablePath);
        var actual = Convert.ToHexString(SHA256.HashData(executable));
        if (!builds.RootElement.GetProperty("builds").EnumerateArray()
            .Any(build => string.Equals(build.GetProperty("sha256").GetString(), actual,
                StringComparison.OrdinalIgnoreCase)))
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath).FileVersion ?? "?";
            throw new InvalidOperationException(UiText.F("error.unsupported.first.version", version));
        }
    }

    public static bool IsGameRunning(string? gameRoot = null)
    {
        GameEdition? edition = gameRoot is null ? null : GameEditionInfo.Detect(gameRoot);
        var names = edition is null ? new[] { "sora_1st", "sora_2nd" }
            : new[] { Path.GetFileNameWithoutExtension(GameEditionInfo.ExecutableName(edition.Value)) };
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    private static string SafePath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(UiText.T("error.path.outside"));
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(UiText.F("error.link.path", current));
        }
        return path;
    }

    public static string Install(string package, string gameRoot, string backupRoot, Func<bool>? runningCheck = null)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        runningCheck ??= () => IsGameRunning(gameRoot);
        if (runningCheck()) throw new InvalidOperationException(UiText.T("error.game.running"));
        ValidateInstallTarget(gameRoot);
        var edition = GameEditionInfo.Detect(gameRoot);
        package = Path.GetFullPath(package);
        var requiredPaths = edition == GameEdition.Second
            ? new[] { "xinput1_4.dll", "asset/common/model" }
            : new[] { "xinput1_4.dll", "ED9Loader", "Mod/ScherazardSummon/asset/common/model" };
        foreach (var required in requiredPaths)
            if (!File.Exists(Path.Combine(package,required)) && !Directory.Exists(Path.Combine(package,required)))
                throw new InvalidDataException(UiText.F("error.package.incomplete", required));
        var files = new List<string> { "xinput1_4.dll" };
        if (edition == GameEdition.Second)
        {
            var packageLoader = SafePath(package, "xinput1_4.dll");
            var packageHash = SecondLoaderService.Validate(packageLoader);
            var existingLoader = SafePath(gameRoot, "xinput1_4.dll");
            if (File.Exists(existingLoader))
            {
                using var existingStream = File.OpenRead(existingLoader);
                var existingHash = Convert.ToHexString(SHA256.HashData(existingStream));
                if (!existingHash.Equals(packageHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(UiText.F("error.second.loader.conflict", existingLoader));
            }
            var modelFolder = Path.Combine(package, "asset", "common", "model");
            SafePath(package, "asset/common/model");
            var modelFiles = Directory.GetFiles(modelFolder, "*.mdl", SearchOption.TopDirectoryOnly);
            if (modelFiles.Length != 1 || Directory.GetFileSystemEntries(modelFolder).Length != 1)
                throw new InvalidDataException(UiText.T("error.second.package.model"));
            files.Add(Path.GetRelativePath(package, modelFiles[0]));
            if (Directory.GetFileSystemEntries(package).Length != 2
                || Directory.GetFileSystemEntries(Path.Combine(package, "asset")).Length != 1
                || Directory.GetFileSystemEntries(Path.Combine(package, "asset", "common")).Length != 1)
                throw new InvalidDataException(UiText.T("error.second.package.model"));
        }
        else foreach (var folder in new[] { "ED9Loader", "Mod" })
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
            if(Directory.Exists(target)) throw new IOException(UiText.F("error.same.directory", target));
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
                if(runningCheck()) throw new IOException(UiText.T("error.install.interrupted"));
                var target=SafePath(gameRoot,relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                touched.Add(relative); File.Copy(Path.Combine(package,relative),target,true);
                using var a=File.OpenRead(Path.Combine(package,relative)); using var b=File.OpenRead(target);
                if(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new IOException(UiText.F("error.install.verify", relative));
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
            if(errors.Count>0) throw new AggregateException(UiText.F("error.install.rollback.incomplete", backup),errors.Prepend(failure));
            throw new IOException(UiText.F("error.install.rollback", backup),failure);
        }
    }

    public static string RestoreLatest(string gameRoot,string backupRoot,Func<bool>? runningCheck=null)
    {
        gameRoot=ValidateGameRoot(gameRoot);runningCheck??=()=>IsGameRunning(gameRoot);
        if(runningCheck())throw new InvalidOperationException(UiText.T("error.restore.running"));
        if(!Directory.Exists(backupRoot))throw new IOException(UiText.T("error.no.backup"));
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
                    throw new IOException(UiText.F("error.restore.changed", pair.Key));
                originals[pair.Key]=pair.Value!.GetValue<bool>()?File.ReadAllBytes(saved):null;
            }
            var touched=new List<string>();
            try {
                foreach(var pair in originals) {
                    if(runningCheck())throw new IOException(UiText.T("error.restore.interrupted"));
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
        throw new IOException(UiText.T("error.restore.none.for.game"));
    }
}
