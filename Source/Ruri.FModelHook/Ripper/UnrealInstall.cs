using CUE4Parse.UE4.Versions;
using System.Diagnostics;

namespace Ruri.FModelHook.Ripper;

/// <summary>
/// What a packaged Unreal install looks like on disk, read off the install and nothing else:
/// the archive folders (every <c>Paks</c> directory holding .pak/.utoc containers), the project
/// folder those sit under, and the engine version the game executable's own version resource
/// states. No name of any game appears here.
/// </summary>
public static class UnrealInstall
{
    private const string PaksFolderName = "Paks";
    private const string ContentFolderName = "Content";
    private const string BinariesFolderName = "Binaries";
    private const int SearchDepth = 4;

    public static readonly string[] ArchiveExtensions = [".pak", ".utoc", ".ucas"];

    public static bool IsArchive(string path)
    {
        foreach (string extension in ArchiveExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Every archive folder under the install, shallowest first.</summary>
    public static string[] PakFolders(string gameRoot)
    {
        List<string> found = new();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return [];
        }
        Walk(gameRoot, 0, found);
        found.Sort(static (left, right) => left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right));
        return found.ToArray();
    }

    private static void Walk(string directory, int depth, List<string> found)
    {
        if (Path.GetFileName(directory).Equals(PaksFolderName, StringComparison.OrdinalIgnoreCase) && HoldsArchives(directory))
        {
            found.Add(directory);
            return;
        }
        if (depth >= SearchDepth)
        {
            return;
        }
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        foreach (string child in children)
        {
            Walk(child, depth + 1, found);
        }
    }

    private static bool HoldsArchives(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (IsArchive(file))
            {
                return true;
            }
        }
        return false;
    }

    public static string[] ContentRoots(string gameRoot) => PakFolders(gameRoot);

    /// <summary>The project folder an archive folder belongs to (<c>Project/Content/Paks</c>), or null.</summary>
    public static string? ProjectFolder(string pakFolder)
    {
        string? content = Path.GetDirectoryName(pakFolder);
        if (content is null || !Path.GetFileName(content).Equals(ContentFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return Path.GetDirectoryName(content);
    }

    /// <summary>
    /// The engine version the project's largest executable carries in its version resource:
    /// Unreal stamps every packaged executable with the engine's own major.minor.patch, which is
    /// the build's own statement of what serialized it.
    /// </summary>
    public static string EngineVersion(string pakFolder)
    {
        string? project = ProjectFolder(pakFolder);
        if (project is null)
        {
            return string.Empty;
        }
        string binaries = Path.Combine(project, BinariesFolderName);
        if (!Directory.Exists(binaries))
        {
            return string.Empty;
        }
        string? best = null;
        long bestSize = -1;
        foreach (string executable in Directory.EnumerateFiles(binaries, "*.exe", SearchOption.AllDirectories))
        {
            long size = new FileInfo(executable).Length;
            if (size > bestSize)
            {
                bestSize = size;
                best = executable;
            }
        }
        if (best is null)
        {
            return string.Empty;
        }
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(best);
        string? version = info.ProductVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = info.FileVersion;
        }
        return version?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// The CUE4Parse game enum a stock engine version maps to: the <c>GAME_UE{major}_{minor}</c>
    /// member when one exists, else the latest member of that major.
    /// </summary>
    public static EGame? EngineFromVersion(string engineVersion)
    {
        if (string.IsNullOrWhiteSpace(engineVersion))
        {
            return null;
        }
        string[] parts = engineVersion.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
        {
            return null;
        }
        if (Enum.TryParse($"GAME_UE{major}_{minor}", out EGame exact))
        {
            return exact;
        }
        return Enum.TryParse($"GAME_UE{major}_LATEST", out EGame latest) ? latest : null;
    }
}
