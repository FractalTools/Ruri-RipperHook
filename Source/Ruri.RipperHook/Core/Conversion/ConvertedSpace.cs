using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Metadata;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using System.Collections.Concurrent;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// Where assets converted from another engine are born: one processed bundle inside the
/// game bundle AssetRipper is loading, one collection per source package, every asset a
/// stock SourceGenerated object created at the project version. Nothing here knows which
/// engine the data came from; a source module names packages and fills assets.
/// </summary>
public sealed class ConvertedSpace
{
    private readonly ConcurrentDictionary<string, ConvertedPackage> packages = new(StringComparer.Ordinal);
    private readonly object bundleLock = new();

    public GameBundle GameBundle { get; }

    public ProcessedBundle Bundle { get; }

    /// <summary>Where an asset's bytes wait to be fetched at export instead of being held (see <see cref="DeferredResource"/>).</summary>
    public DeferredResource Deferred { get; }

    public UnityVersion Version { get; }

    public ConvertedSpace(GameBundle gameBundle, string bundleName, UnityVersion version)
    {
        ArgumentNullException.ThrowIfNull(gameBundle);
        GameBundle = gameBundle;
        Version = version;
        Bundle = gameBundle.AddNewProcessedBundle(bundleName);
        Deferred = new DeferredResource(Bundle);
    }

    public IEnumerable<ConvertedPackage> Packages => packages.Values;

    public ConvertedPackage Package(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return packages.GetOrAdd(name, CreatePackage);
    }

    public bool TryGetPackage(string name, out ConvertedPackage? package) => packages.TryGetValue(name, out package);

    private ConvertedPackage CreatePackage(string name)
    {
        lock (bundleLock)
        {
            return new ConvertedPackage(this, Bundle.AddNewProcessedCollection(name, Version));
        }
    }
}

/// <summary>
/// One source package as a Unity collection. Assets are created and filled by the thread
/// that owns the package; another package's assets are only ever referenced, never mutated,
/// which is what lets a whole closure convert in parallel with no lock on the hot path.
/// </summary>
public sealed class ConvertedPackage
{
    private readonly object linkLock = new();

    public ConvertedSpace Space { get; }

    public ProcessedAssetCollection Collection { get; }

    public string Name => Collection.Name;

    internal ConvertedPackage(ConvertedSpace space, ProcessedAssetCollection collection)
    {
        Space = space;
        Collection = collection;
    }

    /// <summary>
    /// A new asset of <paramref name="classId"/>, named and filed under the export path the
    /// source states for it. The path is the Unity-side address AssetRipper writes the asset
    /// to; it carries no extension, that being the exporter's decision per class.
    /// </summary>
    public T Create<T>(ClassIDType classId, string name, string? originalPath) where T : IUnityObjectBase
    {
        IUnityObjectBase created = Collection.CreateAsset((int)classId, AssetFactory.Create);
        if (created is not T typed)
        {
            throw new InvalidOperationException(
                $"[ConvertedPackage] {classId} at {Space.Version} is {created.GetType().Name}, not {typeof(T).Name}.");
        }
        if (created is INamed named)
        {
            named.Name = name;
        }
        if (originalPath is not null)
        {
            created.OriginalPath = originalPath;
        }
        return typed;
    }

    /// <summary>
    /// A pointer from this package to <paramref name="target"/>, wherever it lives. The
    /// dependency list is the one shared thing two threads could touch (a target in a package
    /// converted elsewhere adds an entry), so its growth is serialized per package.
    /// </summary>
    public PPtr<T> Link<T>(T? target) where T : IUnityObjectBase
    {
        if (target is null)
        {
            return default;
        }
        lock (linkLock)
        {
            return Collection.ForceCreatePPtr(target);
        }
    }

    public void Link<T>(IPPtr<T> pointer, T? target) where T : IUnityObjectBase
    {
        if (target is null)
        {
            pointer.SetAsset(Collection, default);
            return;
        }
        lock (linkLock)
        {
            Collection.AddDependency(target.Collection);
        }
        pointer.SetAsset(Collection, target);
    }
}
