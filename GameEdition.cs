using System.IO;

namespace Sky1stCharacterStudio;

public enum GameEdition
{
    First,
    Second
}

public static class GameEditionInfo
{
    public static string ExecutableName(GameEdition edition) =>
        edition == GameEdition.Second ? "sora_2nd.exe" : "sora_1st.exe";

    public static GameEdition Detect(string root)
    {
        var first = File.Exists(Path.Combine(root, ExecutableName(GameEdition.First)));
        var second = File.Exists(Path.Combine(root, ExecutableName(GameEdition.Second)));
        if (first == second)
            throw new InvalidOperationException(UiText.T("error.invalid.game.root"));
        return second ? GameEdition.Second : GameEdition.First;
    }

    public static string RootFromModelArchive(string archivePath)
    {
        var steam = Path.GetDirectoryName(Path.GetFullPath(archivePath))
            ?? throw new InvalidDataException("The model archive has no parent directory.");
        return Path.GetFullPath(Path.Combine(steam, "..", ".."));
    }
}
