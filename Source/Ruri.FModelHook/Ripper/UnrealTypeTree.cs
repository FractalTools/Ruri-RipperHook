using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using Ruri.RipperHook.Core;
using Ruri.RipperHook.Core.TypeTree;
using Ruri.FModelHook.Ripper.TypeTree;
using System.Globalization;

namespace Ruri.FModelHook.Ripper;

/// <summary>
/// The install's reflection schema (.usmap) as the type trees of the Unreal custom engine,
/// registered at run time under lineage <see cref="CustomEngineType.UnrealEngine"/>: every
/// struct a class by name, readable by the same interpreter that reads a Unity fork's classes.
/// Rebuilt when the mounted schema changes, never for the same one twice.
/// </summary>
public static class UnrealTypeTree
{
    public const string VersionKey = UsmapTypeTreeBuilder.VersionKey;

    public static readonly string LineageKey = ((int)CustomEngineType.UnrealEngine).ToString(CultureInfo.InvariantCulture);

    public static readonly TypeTreeVersion Version = new(CustomEngineType.UnrealEngine, VersionKey);

    private const string ScriptPackagePrefix = "/Script/";

    /// <summary>
    /// Whether a type lives in a script package -- compiled into the game, and so in the
    /// reflection schema -- or in a content package, where a Blueprint class or a user-defined
    /// struct is data the schema never saw and the object's own tags state its shape.
    /// </summary>
    public static bool IsNative(ResolvedObject? type) =>
        type?.Package.Name is { } package && package.StartsWith(ScriptPackagePrefix, StringComparison.Ordinal);

    private static readonly object Gate = new();
    private static TypeMappings? registeredFor;
    private static IReadOnlyDictionary<string, int> classIds = new Dictionary<string, int>();

    public static bool IsLoaded => registeredFor is not null;

    public static void Ensure(DefaultFileProvider provider, UnityVersion layoutVersion)
    {
        TypeMappings? mappings = provider.MappingsForGame;
        if (mappings is null || mappings.Types.Count == 0 || ReferenceEquals(mappings, registeredFor))
        {
            return;
        }
        lock (Gate)
        {
            if (ReferenceEquals(mappings, registeredFor))
            {
                return;
            }
            UsmapTypeTreeBuilder builder = UsmapTypeTreeBuilder.Build(mappings);
            TypeTreeDatabase.RegisterLineage(LineageKey, builder.Blobs, [(VersionKey, layoutVersion.ToString())]);
            classIds = builder.ClassIds;
            registeredFor = mappings;
        }
    }

    public static TypeTreeNode? RootFor(string className)
    {
        if (registeredFor is null || !classIds.TryGetValue(className, out int id))
        {
            return null;
        }
        return TypeTreeDatabase.GetReleaseRoot((ClassIDType)id, Version);
    }
}
