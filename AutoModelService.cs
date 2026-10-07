using System.Diagnostics;
using System.IO;
using System.Text;

namespace Sky1stCharacterStudio;

public static class AutoModelService
{
    private static readonly SemaphoreSlim Gate = new(1);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(4);

    public static async Task Run(string game, string model, string mode, string output,
        int? strength = null, bool isBaseGameCharacter = false, CancellationToken cancellationToken = default,
        string? modelSource = null, string? imageArchive = null, string? ageDefinitionLabel = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(OperationTimeout);
        try { await Gate.WaitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException(UiText.T("error.model.queue.timeout")); }
        try
        {
            var helper = PreviewService.FindFileUpwards("tools", "auto_model.py")
                ?? throw new FileNotFoundException(UiText.T("error.no.model"));
            Directory.CreateDirectory(output);
            var logPath = Path.Combine(output, "model-process.log");
            var start = new ProcessStartInfo(PreviewService.ResolvePython())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in new[] { helper, "--game", game, "--model", model, "--mode", mode, "--out", output })
                start.ArgumentList.Add(argument);
            start.ArgumentList.Add("--edition");
            start.ArgumentList.Add(GameEditionInfo.Detect(game) == GameEdition.Second ? "second" : "first");
            if (!string.IsNullOrWhiteSpace(modelSource))
            {
                start.ArgumentList.Add("--model-source");
                start.ArgumentList.Add(modelSource);
            }
            if (!string.IsNullOrWhiteSpace(imageArchive))
            {
                start.ArgumentList.Add("--image-pac");
                start.ArgumentList.Add(imageArchive);
            }
            if (isBaseGameCharacter) start.ArgumentList.Add("--base-game-character");
            if (!string.IsNullOrWhiteSpace(ageDefinitionLabel))
            {
                start.ArgumentList.Add("--age-definition-label");
                start.ArgumentList.Add(ageDefinitionLabel);
            }
            if (strength.HasValue)
            {
                start.ArgumentList.Add("--export");
                start.ArgumentList.Add("--strength");
                start.ArgumentList.Add(strength.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            using var process = Process.Start(start) ?? throw new IOException(UiText.T("error.processor.start"));
            var clock = Stopwatch.StartNew();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync();
                var cancelledOutput = await stdout;
                var cancelledError = await stderr;
                await File.WriteAllTextAsync(logPath,
                    $"stage=cancelled elapsed={clock.Elapsed}\nstdout:\n{cancelledOutput}\nstderr:\n{cancelledError}",
                    Encoding.UTF8, CancellationToken.None);
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TimeoutException(UiText.F("error.model.timeout", logPath));
            }
            var outputText = await stdout;
            var errorText = await stderr;
            await File.WriteAllTextAsync(logPath,
                $"stage=finished elapsed={clock.Elapsed} exit={process.ExitCode}\nstdout:\n{outputText}\nstderr:\n{errorText}",
                Encoding.UTF8, CancellationToken.None);
            if (process.ExitCode != 0)
                throw new InvalidDataException(UiText.F("error.model.process", errorText.Trim(), logPath));
        }
        finally { Gate.Release(); }
    }
}
