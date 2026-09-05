using AssetRipper.Assets;
using AssetRipper.SourceGenerated;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using Ruri.RipperHook.Conversion;
using System.Collections.Concurrent;

namespace Ruri.FModelHook.UnityConverter;

/// <summary>
/// One Unreal class family turned into Unity assets. A converter runs in two phases over one
/// package on one thread: <see cref="Allocate"/> creates every Unity asset another package may
/// point at from the export's header alone -- name, class, outer, nothing deserialized -- and
/// registers each under the export; <see cref="Fill"/> reads the export once every package's
/// assets exist, so a reference to any export anywhere in the closure resolves, and creates
/// the assets only the export itself refers to. The class names it handles are matched
/// through the reflection schema's super chain, so a subclass the converter never heard of
/// still lands here.
/// </summary>
public interface IUnrealConverter
{
    /// <summary>The Unreal class names this converter reads, matched by name or by ancestry.</summary>
    IReadOnlyList<string> ClassNames { get; }

    /// <summary>The Unity classes an export of this family becomes -- what a cabmap lists for it.</summary>
    IReadOnlyList<ClassIDType> Produces { get; }

    void Allocate(UnrealConversion conversion, ResolvedObject header);

    void Fill(UnrealConversion conversion, UObject export);
}

/// <summary>
/// Every export's Unity assets, keyed by the export's full path and a slot the converter names
/// (a skeletal mesh yields a Mesh under one slot and a rig prefab under another). Written during
/// allocation by the package's own thread, read by every thread during fill.
/// </summary>
public sealed class UnrealAssetTable
{
    public const string PrimarySlot = "";

    private readonly ConcurrentDictionary<(string Path, string Slot), IUnityObjectBase> assets = new();

    public void Register(UObject export, string slot, IUnityObjectBase asset) =>
        assets[(Key(export), slot)] = asset;

    public void Register(ResolvedObject header, string slot, IUnityObjectBase asset) =>
        assets[(Key(header), slot)] = asset;

    public IUnityObjectBase? Find(UObject export, string slot = PrimarySlot) =>
        assets.TryGetValue((Key(export), slot), out IUnityObjectBase? asset) ? asset : null;

    public IUnityObjectBase? Find(ResolvedObject resolved, string slot = PrimarySlot) =>
        assets.TryGetValue((Key(resolved), slot), out IUnityObjectBase? asset) ? asset : null;

    public T? Find<T>(UObject? export, string slot = PrimarySlot) where T : class, IUnityObjectBase =>
        export is null ? null : Find(export, slot) as T;

    public T? Find<T>(ResolvedObject? resolved, string slot = PrimarySlot) where T : class, IUnityObjectBase =>
        resolved is null ? null : Find(resolved, slot) as T;

    public T? Find<T>(FPackageIndex? index, string slot = PrimarySlot) where T : class, IUnityObjectBase =>
        index is null || index.IsNull ? null : Find<T>(index.ResolvedObject, slot);

    /// <summary>The asset an object path names -- the form a soft reference carries.</summary>
    public T? Find<T>(string pathName, string slot = PrimarySlot) where T : class, IUnityObjectBase =>
        assets.TryGetValue((pathName, slot), out IUnityObjectBase? asset) ? asset as T : null;

    public int Count => assets.Count;

    public static string Key(UObject export) => export.GetPathName();

    public static string Key(ResolvedObject resolved) => resolved.GetPathName();
}

/// <summary>The per-package conversion state a converter works against.</summary>
public sealed class UnrealConversion
{
    public UnrealConversion(ConvertedSpace space, ConvertedPackage package, string packagePath, UnrealAssetTable table, SourceBasis basis, UnrealLoadShared shared, bool isSeed)
    {
        IsSeed = isSeed;
        Space = space;
        Package = package;
        PackagePath = packagePath;
        Table = table;
        Basis = basis;
        Shared = shared;
        Hierarchy = new HierarchyBuilder(package);
    }

    public UnrealLoadShared Shared { get; }

    /// <summary>
    /// Whether the load was asked for this package by name. Only such a package stands as a
    /// prefab root of its own; one reached as a dependency contributes assets alone.
    /// </summary>
    public bool IsSeed { get; }

    public ConvertedSpace Space { get; }

    public ConvertedPackage Package { get; }

    /// <summary>The package's provider path with its extension ("Project/Content/A/B.uasset").</summary>
    public string PackagePath { get; }

    public UnrealAssetTable Table { get; }

    public SourceBasis Basis { get; }

    public HierarchyBuilder Hierarchy { get; }

    /// <summary>
    /// The Unity export path of an asset the package yields: the package's own path under the
    /// Assets root, with the export's name when the package holds more than its main asset.
    /// </summary>
    public string UnityPath(UObject export, string? suffix = null) => UnityPath(export.Name, suffix);

    public string UnityPath(ResolvedObject header, string? suffix = null) => UnityPath(header.Name.Text, suffix);

    private string UnityPath(string exportName, string? suffix)
    {
        string stem = UnrealPaths.UnityStem(PackagePath);
        string leaf = Path.GetFileName(stem);
        bool isMain = string.Equals(exportName, leaf, StringComparison.OrdinalIgnoreCase);
        string path = isMain ? stem : stem + "/" + exportName;
        return suffix is null ? path : path + suffix;
    }

    /// <summary>The name of an object's class, empty when the class import did not resolve.</summary>
    public static string ClassName(ResolvedObject? @class) => @class?.Name.Text ?? string.Empty;

    /// <summary>The class name an export was allocated under: its class import's name.</summary>
    public static string ClassOf(ResolvedObject header) => ClassName(header.Class);

    /// <summary>Whether the export's class is <paramref name="ancestor"/> or descends from it in the reflection schema.</summary>
    public bool IsA(ResolvedObject header, string ancestor) => UnrealConverters.IsA(ClassOf(header), ancestor, Shared.Provider.MappingsForGame);

    public void Register(UObject export, IUnityObjectBase asset, string slot = UnrealAssetTable.PrimarySlot) =>
        Table.Register(export, slot, asset);

    public void Register(ResolvedObject header, IUnityObjectBase asset, string slot = UnrealAssetTable.PrimarySlot) =>
        Table.Register(header, slot, asset);
}

/// <summary>Unreal package paths and the Unity paths they export under.</summary>
public static class UnrealPaths
{
    public const string AssetsRoot = "Assets";

    /// <summary>The file a package's prefab root lands on, in the layout its Unity stem states.</summary>
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

/// <summary>
/// The converters this decoder ships, in the order they are asked. Adding a family is adding
/// a converter here; the scan, the loader and the cabmap's class list all read this one table.
/// </summary>
public static class UnrealConverters
{
    private static readonly IReadOnlyList<IUnrealConverter> All =
    [
        new Converters.StaticMeshConverter(),
        new Converters.SkeletalMeshConverter(),
        new Converters.SkeletonConverter(),
        new Converters.TextureConverter(),
        new Converters.MaterialConverter(),
        new Converters.AnimSequenceConverter(),
        new Converters.WorldConverter(),
        new Converters.DataTableConverter(),
        new Converters.BlueprintConverter(),
        new Converters.PropertyBagConverter(),
    ];

    private static readonly IUnrealConverter Fallback = All[^1];

    private static readonly ConcurrentDictionary<string, IUnrealConverter> ByClassName = new(StringComparer.Ordinal);

    public static IReadOnlyList<IUnrealConverter> Converters => All;

    /// <summary>
    /// The converter for a class known only by name -- what the scan has before any object is
    /// loaded. The reflection schema's super chain decides ancestry; without a schema an exact
    /// name is all that can be matched.
    /// </summary>
    public static IUnrealConverter ForClassName(string className, TypeMappings? mappings)
    {
        return ByClassName.GetOrAdd(className, name => Resolve(name, mappings));
    }

    public static void ForgetClassNames() => ByClassName.Clear();

    /// <summary>Whether <paramref name="className"/> is <paramref name="ancestor"/> or descends from it in the reflection schema.</summary>
    public static bool IsA(string className, string ancestor, TypeMappings? mappings)
    {
        foreach (string name in Ancestry(className, mappings))
        {
            if (string.Equals(name, ancestor, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static IUnrealConverter Resolve(string className, TypeMappings? mappings)
    {
        foreach (string name in Ancestry(className, mappings))
        {
            foreach (IUnrealConverter converter in All)
            {
                foreach (string handled in converter.ClassNames)
                {
                    if (string.Equals(handled, name, StringComparison.Ordinal))
                    {
                        return converter;
                    }
                }
            }
        }
        return Fallback;
    }

    /// <summary>The class and its ancestors, nearest first, as far as the reflection schema names them.</summary>
    private static IEnumerable<string> Ancestry(string className, TypeMappings? mappings)
    {
        string? cursor = className;
        HashSet<string> seen = new(StringComparer.Ordinal);
        while (cursor is not null && cursor.Length > 0 && seen.Add(cursor))
        {
            yield return cursor;
            cursor = mappings is not null && mappings.Types.TryGetValue(cursor, out Struct? schema) ? schema.SuperType : null;
        }
    }
}
