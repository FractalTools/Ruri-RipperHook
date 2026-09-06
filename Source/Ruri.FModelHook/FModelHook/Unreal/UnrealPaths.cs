namespace Ruri.FModelHook.Unreal;

/// <summary>
/// The paths a browser lists an Unreal package under. A cabmap row carries container paths so a
/// user can find an asset by where it lives, and those are spelled the way every other title's
/// rows are, which is why an engine path grows an "Assets/" head here and nowhere else.
/// </summary>
public static class UnrealPaths
{
    public const string AssetsRoot = "Assets";

    /// <summary>The path a package's own model is listed under, beside the package itself.</summary>
    public const string PrefabExtension = ".prefab";

    /// <summary>"Project/Content/A/B.uasset" to "Assets/Project/Content/A/B.prefab".</summary>
    public static string PrefabPath(string packagePath) => UnityStem(packagePath) + PrefabExtension;

    /// <summary>"Project/Content/A/B.uasset" to "Assets/Project/Content/A/B".</summary>
    public static string UnityStem(string packagePath)
    {
        string trimmed = packagePath.Replace('\\', '/');
        int dot = trimmed.LastIndexOf('.');
        int slash = trimmed.LastIndexOf('/');
        if (dot > slash)
        {
            trimmed = trimmed[..dot];
        }
        return AssetsRoot + "/" + trimmed;
    }

    /// <summary>The container path a cabmap row states for a package: the same stem, extension kept.</summary>
    public static string ContainerPath(string packagePath) => AssetsRoot + "/" + packagePath.Replace('\\', '/');
}
