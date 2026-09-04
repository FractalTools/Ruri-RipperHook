using AssetRipper.Assets;
using AssetRipper.SourceGenerated;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using Ruri.RipperHook.Conversion;
using System.Collections.Concurrent;

namespace Ruri.FModelHook.Ripper;

/// <summary>
/// One Unreal class family turned into Unity assets. A converter runs in two phases over one
/// package on one thread: <see cref="Allocate"/> creates every Unity asset the export becomes
/// and registers each under the export so other packages can point at it; <see cref="Fill"/>
/// writes the data once every package's assets exist, so a reference to any export anywhere in
/// the closure resolves. The class names it handles are matched through the reflection schema's
/// super chain, so a subclass the converter never heard of still lands here.
/// </summary>
public interface IUnrealConverter
{
    /// <summary>The Unreal class names this converter reads, matched by name or by ancestry.</summary>
    IReadOnlyList<string> ClassNames { get; }

    /// <summary>The Unity classes an export of this family becomes -- what a cabmap lists for it.</summary>
    IReadOnlyList<ClassIDType> Produces { get; }

    bool Handles(UObject export);

    void Allocate(UnrealConversion conversion, UObject export);

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

    public int Count => assets.Count;

    public static string Key(UObject export) => export.GetPathName();

    public static string Key(ResolvedObject resolved) => resolved.GetPathName();
}

/// <summary>The per-package conversion state a converter works against.</summary>
public sealed class UnrealConversion
{
    public UnrealConversion(ConvertedSpace space, ConvertedPackage package, IPackage source, string packagePath, UnrealAssetTable table, SourceBasis basis, UnrealLoadShared shared)
    {
        Space = space;
        Package = package;
        Source = source;
        PackagePath = packagePath;
        Table = table;
        Basis = basis;
        Shared = shared;
        Hierarchy = new HierarchyBuilder(package);
    }

    public UnrealLoadShared Shared { get; }

    public ConvertedSpace Space { get; }

    public ConvertedPackage Package { get; }

    public IPackage Source { get; }

    /// <summary>The package's provider path with its extension ("Project/Content/A/B.uasset").</summary>
    public string PackagePath { get; }

    public UnrealAssetTable Table { get; }

    public SourceBasis Basis { get; }

    public HierarchyBuilder Hierarchy { get; }

    /// <summary>
    /// The Unity export path of an asset the package yields: the package's own path under the
    /// Assets root, with the export's name when the package holds more than its main asset.
    /// </summary>
    public string UnityPath(UObject export, string? suffix = null)
    {
        string stem = UnrealPaths.UnityStem(PackagePath);
        string leaf = Path.GetFileName(stem);
        bool isMain = string.Equals(export.Name, leaf, StringComparison.OrdinalIgnoreCase);
        string path = isMain ? stem : stem + "/" + export.Name;
        return suffix is null ? path : path + suffix;
    }

    public void Register(UObject export, IUnityObjectBase asset, string slot = UnrealAssetTable.PrimarySlot) =>
        Table.Register(export, slot, asset);
}

/// <summary>Unreal package paths and the Unity paths they export under.</summary>
public static class UnrealPaths
{
    public const string AssetsRoot = "Assets";

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
        new Converters.PropertyBagConverter(),
    ];

    private static readonly IUnrealConverter Fallback = All[^1];

    private static readonly ConcurrentDictionary<string, IUnrealConverter> ByClassName = new(StringComparer.Ordinal);

    public static IReadOnlyList<IUnrealConverter> Converters => All;

    public static IUnrealConverter For(UObject export)
    {
        foreach (IUnrealConverter converter in All)
        {
            if (converter.Handles(export))
            {
                return converter;
            }
        }
        return Fallback;
    }

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

    private static IUnrealConverter Resolve(string className, TypeMappings? mappings)
    {
        string? cursor = className;
        HashSet<string> seen = new(StringComparer.Ordinal);
        while (cursor is not null && seen.Add(cursor))
        {
            foreach (IUnrealConverter converter in All)
            {
                foreach (string handled in converter.ClassNames)
                {
                    if (string.Equals(handled, cursor, StringComparison.Ordinal))
                    {
                        return converter;
                    }
                }
            }
            cursor = mappings is not null && mappings.Types.TryGetValue(cursor, out Struct? schema) ? schema.SuperType : null;
        }
        return Fallback;
    }
}
