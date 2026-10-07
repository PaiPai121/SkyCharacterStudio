using System.Diagnostics;
using System.ComponentModel;
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

        if (edition == GameEdition.First) GetSupportedFirstVersion(gameRoot);
    }

    public static string GetSupportedFirstVersion(string gameRoot)
    {
        var executablePath = Path.Combine(gameRoot, GameEditionInfo.ExecutableName(GameEdition.First));
        // The 1st Chapter loader uses executable-specific addresses. The 2nd
        // Chapter loose-file loader is validated separately and has no EXE hash gate.
        var manifest = Path.Combine(AppContext.BaseDirectory, "assets", "first-install-builds.json");
        if (!File.Exists(manifest))
            throw new FileNotFoundException(UiText.T("error.first.builds.missing"), manifest);
        using var builds = JsonDocument.Parse(File.ReadAllText(manifest));
        using var executable = File.OpenRead(executablePath);
        var actual = Convert.ToHexString(SHA256.HashData(executable));
        var match = builds.RootElement.GetProperty("builds").EnumerateArray()
            .FirstOrDefault(build => string.Equals(build.GetProperty("sha256").GetString(), actual,
                StringComparison.OrdinalIgnoreCase));
        if (match.ValueKind == JsonValueKind.Undefined)
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath).FileVersion ?? "?";
            throw new InvalidOperationException(UiText.F("error.unsupported.first.version", version));
        }
        return match.GetProperty("version").GetString()
            ?? throw new InvalidDataException(UiText.T("error.first.builds.missing"));
    }

    public static bool IsGameRunning(string? gameRoot = null)
    {
        GameEdition? edition = gameRoot is null ? null : GameEditionInfo.Detect(gameRoot);
        var names = edition is null ? new[] { "sora_1st", "sora_2nd" }
            : new[] { Path.GetFileNameWithoutExtension(GameEditionInfo.ExecutableName(edition.Value)) };
        var targetExecutable = edition is null ? null : Path.GetFullPath(Path.Combine(gameRoot!,
            GameEditionInfo.ExecutableName(edition.Value)));
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    if (targetExecutable is null) return true;
                    try
                    {
                        var runningExecutable = process.MainModule?.FileName;
                        if (runningExecutable is null || Path.GetFullPath(runningExecutable).Equals(
                            targetExecutable, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch (InvalidOperationException) { /* Process exited during inspection. */ }
                    catch (Win32Exception) { return true; /* Cannot establish a safe mismatch. */ }
                    catch (UnauthorizedAccessException) { return true; /* Cannot establish a safe mismatch. */ }
                }
            }
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

    private static bool SameFileContents(string left, string right)
    {
        if (new FileInfo(left).Length != new FileInfo(right).Length) return false;
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return SHA256.HashData(leftStream).SequenceEqual(SHA256.HashData(rightStream));
    }

    private static List<string> MatchedRetiredFirstPlugins(string gameRoot, string gameVersion,
        IReadOnlyCollection<string> copiedFiles)
    {
        if (gameVersion != "1.0.7.0") return [];
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "assets", "legacy-first-plugins.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        if (root.GetProperty("game").GetString() != "first"
            || root.GetProperty("retireOnVersion").GetString() != gameVersion)
            throw new InvalidDataException("Invalid 1st legacy plugin retirement manifest.");
        var allowedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ED9Loader/plugins/ScriptInject.dll", "ED9Loader/plugins/EventStarter.dll"
        };
        var retired = new List<string>();
        foreach (var entry in root.GetProperty("files").EnumerateArray())
        {
            var relative = entry.GetProperty("path").GetString()
                ?? throw new InvalidDataException("Legacy plugin path is missing.");
            var expectedHash = entry.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException("Legacy plugin hash is missing.");
            if (!allowedPaths.Remove(relative) || expectedHash.Length != 64
                || !expectedHash.All(Uri.IsHexDigit)
                || copiedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid 1st legacy plugin retirement entry.");
            var target = SafePath(gameRoot, relative);
            if (!File.Exists(target)) continue;
            using var file = File.OpenRead(target);
            var actualHash = Convert.ToHexString(SHA256.HashData(file));
            if (actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) retired.Add(relative);
        }
        if (allowedPaths.Count != 0)
            throw new InvalidDataException("Incomplete 1st legacy plugin retirement manifest.");
        return retired;
    }

    public static string Install(string package, string gameRoot, string backupRoot, Func<bool>? runningCheck = null)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        runningCheck ??= () => IsGameRunning(gameRoot);
        if (runningCheck()) throw new InvalidOperationException(UiText.T("error.game.running"));
        ValidateInstallTarget(gameRoot);
        var edition = GameEditionInfo.Detect(gameRoot);
        var firstVersion = edition == GameEdition.First ? GetSupportedFirstVersion(gameRoot) : null;
        var firstRedirect = firstVersion == "1.0.7.0"
            ? "ED9Loader/plugins/StudioModelRedirect.dll"
            : "ED9Loader/plugins/SceneRedirect.dll";
        package = Path.GetFullPath(package);
        var requiredPaths = edition == GameEdition.Second
            ? new[] { "xinput1_4.dll", "asset/common/model" }
            : new[] { "xinput1_4.dll", firstRedirect,
                "Mod/SkyCharacterStudio/asset/common/model" };
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
        var copiedFiles = files.ToList();
        var retiredFiles = firstVersion is null ? new List<string>()
            : MatchedRetiredFirstPlugins(gameRoot, firstVersion, copiedFiles);
        files.AddRange(retiredFiles);
        var backup = Path.GetFullPath(Path.Combine(backupRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")));
        var existed = new Dictionary<string,bool>();
        foreach(var relative in files) {
            if (!retiredFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) SafePath(package,relative);
            var target=SafePath(gameRoot,relative);
            if(Directory.Exists(target)) throw new IOException(UiText.F("error.same.directory", target));
            existed[relative]=File.Exists(target);
        }
        Directory.CreateDirectory(backup);
        foreach(var relative in files.Where(r=>existed[r])) {
            var saved=SafePath(backup,relative); Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            File.Copy(Path.Combine(gameRoot,relative),saved);
        }
        var manifest=Path.Combine(backup,"installation.json");
        var installedHashes=copiedFiles.ToDictionary(r=>r,r=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SafePath(package,r)))));
        void Record(string state,string? note=null) => File.WriteAllText(manifest,JsonSerializer.Serialize(new { gameRoot, package, state, note, files=existed,installedHashes,removed=retiredFiles },new JsonSerializerOptions { WriteIndented=true }));
        Record("backed-up");
        // A file whose bytes already match the package must not be rewritten: on a
        // locked or protected copy the write fails even though nothing has to
        // change, and the rollback then fails on the same file. Verify and skip.
        var identical=copiedFiles.Where(r=>existed[r]
            && string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SafePath(gameRoot,r)))),installedHashes[r],StringComparison.OrdinalIgnoreCase)).ToList();
        var touched=new List<string>();
        var createdDirectories=new List<string>();
        try {
            foreach(var relative in copiedFiles) {
                if(runningCheck()) throw new IOException(UiText.T("error.install.interrupted"));
                var source=SafePath(package,relative); var target=SafePath(gameRoot,relative);
                if(identical.Contains(relative)) continue;
                var directory=Path.GetDirectoryName(target)!;
                if(!Directory.Exists(directory)) { Directory.CreateDirectory(directory); createdDirectories.Add(directory); }
                touched.Add(relative); File.Copy(source,target,true);
                using var a=File.OpenRead(source); using var b=File.OpenRead(target);
                if(!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new IOException(UiText.F("error.install.verify", relative));
            }
            foreach (var relative in retiredFiles) {
                if (runningCheck()) throw new IOException(UiText.T("error.install.interrupted"));
                var target = SafePath(gameRoot, relative);
                touched.Add(relative);
                File.Delete(target);
                if (File.Exists(target)) throw new IOException(UiText.F("error.install.verify", relative));
            }
            var note=identical.Count==0 ? null
                : UiText.F("install.note.identical", identical.Count);
            Record("installed",note);
            return backup;
        } catch(Exception failure) {
            var errors=new List<Exception>();
            foreach(var relative in touched.AsEnumerable().Reverse()) try {
                var target=SafePath(gameRoot,relative);
                if(existed[relative]) {
                    var saved=SafePath(backup,relative);
                    if(!File.Exists(target) || !SameFileContents(saved,target)) File.Copy(saved,target,true);
                }
                else if(File.Exists(target)) File.Delete(target);
            } catch(Exception error) { errors.Add(error); }
            Record(errors.Count==0 ? "rolled-back" : "rollback-incomplete",
                identical.Count==0 ? null : UiText.F("install.note.identical", identical.Count));
            if(errors.Count==0) RemoveEmptyDirectories(createdDirectories);
            if(errors.Count>0) throw new AggregateException(UiText.F("error.install.rollback.incomplete", failure.Message, backup),errors.Prepend(failure));
            throw new IOException(UiText.F("error.install.rollback", failure.Message, backup),failure);
        }
    }

    /// <summary>Removes directories this installation created, deepest first, once they are empty.</summary>
    private static void RemoveEmptyDirectories(IEnumerable<string> directories)
    {
        foreach (var directory in directories
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
            var removed=doc["removed"]?.AsArray().Select(item=>item!.GetValue<string>())
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in doc["files"]!.AsObject()) {
                var target=SafePath(gameRoot,pair.Key);var saved=SafePath(backup,pair.Key);
                current[pair.Key]=File.Exists(target)?File.ReadAllBytes(target):null;
                var expected=doc["installedHashes"]?[pair.Key]?.GetValue<string>();
                if(expected is not null && (current[pair.Key] is null || Convert.ToHexString(SHA256.HashData(current[pair.Key]!))!=expected))
                    throw new IOException(UiText.F("error.restore.changed", pair.Key));
                if (removed.Contains(pair.Key) && current[pair.Key] is not null)
                    throw new IOException(UiText.F("error.restore.changed", pair.Key));
                originals[pair.Key]=pair.Value!.GetValue<bool>()?File.ReadAllBytes(saved):null;
            }
            var touched=new List<string>();
            try {
                foreach(var pair in originals) {
                    if(runningCheck())throw new IOException(UiText.T("error.restore.interrupted"));
                    // A matching original was never changed by this installation.
                    // Skipping it also allows Undo while an unchanged loader is
                    // protected against writes by another process.
                    if((pair.Value is null && current[pair.Key] is null)
                        || (pair.Value is not null && current[pair.Key] is not null
                            && pair.Value.SequenceEqual(current[pair.Key]!))) continue;
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
