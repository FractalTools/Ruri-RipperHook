using AssetRipper.Assets.Bundles;
using AssetRipper.Import.Logging;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.VirtualFileSystem;
using Ruri.RipperHook.Conversion;
using Ruri.FModelHook.Ripper.TypeTree;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ruri.FModelHook.Ripper;

/// <summary>
/// What every converter shares across one load: the mounted provider, and the MonoScript each
/// reflected class is bound to (one per class name for the whole load, in a package of its own).
/// </summary>
public sealed class UnrealLoadShared
{
    private readonly ConcurrentDictionary<string, IMonoScript> scripts = new(StringComparer.Ordinal);
    private readonly object scriptGate = new();

    public UnrealLoadShared(UnrealFileProvider provider, ConvertedPackage scriptPackage)
    {
        Provider = provider;
        ScriptPackage = scriptPackage;
    }

    public UnrealFileProvider Provider { get; }

    public ConvertedPackage ScriptPackage { get; }

    public IMonoScript Script(string className)
    {
        if (scripts.TryGetValue(className, out IMonoScript? existing))
        {
            return existing;
        }
        lock (scriptGate)
        {
            return scripts.GetOrAdd(className, name =>
                PropertyBagBuilder.Script(ScriptPackage, name, UsmapTypeTreeBuilder.AssemblyName, UsmapTypeTreeBuilder.AssemblyName));
        }
    }
}

/// <summary>
/// The load path: the packages AssetRipper asked for become Unity assets in two barriers --
/// every package allocates its assets in parallel, then every package fills them in parallel,
/// so a reference from any export to any other resolves without ordering the packages. What
/// counts as "asked for" is the archive files handed over, narrowed by the include predicate
/// the cabmap closure states. A package is let go the moment its assets are filled: the
/// provider forgets it and the loader drops its objects, so what stays in memory is the Unity
/// side plus whatever a package still being filled reaches into.
/// </summary>
public static class UnrealPackageLoader
{
    public const string BundleName = "Unreal";
    public const string ScriptPackageName = "UnrealEngine/Scripts";

    /// <summary>The Unity layout every converted asset is created at.</summary>
    public static readonly UnityVersion ProjectVersion = UsmapTypeTreeBuilder.LayoutVersion;

    /// <summary>
    /// Unreal is Z up, X forward, Y right, in centimeters; Unity is Y up, Z forward, X right, in
    /// meters. Both left-handed, so Unity X reads Unreal Y, Unity Y reads Unreal Z and Unity Z
    /// reads Unreal X -- a proper rotation, no winding change.
    /// </summary>
    public static readonly SourceBasis Basis = new(1, 1f, 2, 1f, 0, 1f, 0.01f);

    public static void Load(GameBundle bundle, IEnumerable<string> archivePaths, Func<string, bool>? include)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        UnrealFileProvider provider = UnrealProviderSession.Current;
        List<GameFile> admitted = Admitted(provider, archivePaths, include);
        if (admitted.Count == 0)
        {
            Logger.Warning(LogCategory.Import, "[Unreal] No package admitted for loading.");
            return;
        }

        long startHeap = GC.GetTotalMemory(true);
        Stopwatch phase = Stopwatch.StartNew();
        UnrealTypeTree.Ensure(provider, ProjectVersion);
        long schemaMs = phase.ElapsedMilliseconds;
        long schemaHeap = GC.GetTotalMemory(true);

        ConvertedSpace space = new(bundle, BundleName, ProjectVersion);
        UnrealAssetTable table = new();
        UnrealLoadShared shared = new(provider, space.Package(ScriptPackageName));

        phase.Restart();
        UnrealConversion?[] conversions = new UnrealConversion?[admitted.Count];
        List<UObject>[] exports = new List<UObject>[admitted.Count];
        int failedPackages = 0;
        Parallel.For(0, admitted.Count, index =>
        {
            GameFile file = admitted[index];
            try
            {
                IPackage package = provider.LoadPackage(file);
                UnrealConversion conversion = new(space, space.Package(file.Path), package, file.Path, table, Basis, shared);
                List<UObject> loaded = new();
                foreach (UObject export in package.GetExports())
                {
                    loaded.Add(export);
                    try
                    {
                        UnrealConverters.For(export).Allocate(conversion, export);
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning(LogCategory.Import, $"[Unreal] Allocate {file.Path}:{export.Name} ({export.ExportType}): {exception.GetType().Name}: {exception.Message}");
                    }
                }
                conversions[index] = conversion;
                exports[index] = loaded;
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failedPackages);
                Logger.Warning(LogCategory.Import, $"[Unreal] Load {file.Path}: {exception.GetType().Name}: {exception.Message}");
            }
        });
        long allocateMs = phase.ElapsedMilliseconds;
        long allocateHeap = GC.GetTotalMemory(true);

        phase.Restart();
        int failedExports = 0;
        Parallel.For(0, admitted.Count, index =>
        {
            UnrealConversion? conversion = conversions[index];
            if (conversion is null)
            {
                return;
            }
            foreach (UObject export in exports[index])
            {
                try
                {
                    UnrealConverters.For(export).Fill(conversion, export);
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref failedExports);
                    Logger.Warning(LogCategory.Import, $"[Unreal] Fill {conversion.PackagePath}:{export.Name} ({export.ExportType}): {exception.GetType().Name}: {exception.Message}");
                }
            }
            conversions[index] = null;
            exports[index] = null!;
            provider.Forget(admitted[index].Path);
        });
        provider.Release();
        Logger.Info(LogCategory.Import,
            $"[Unreal] packages={admitted.Count} failedPackages={failedPackages} assets={table.Count} failedExports={failedExports} "
            + $"schema={schemaMs}ms allocate={allocateMs}ms fill={phase.ElapsedMilliseconds}ms "
            + $"heapAtStart={startHeap >> 20}MB heapAfterSchema={schemaHeap >> 20}MB heapAfterAllocate={allocateHeap >> 20}MB heapAfterFill={GC.GetTotalMemory(true) >> 20}MB peakWorkingSet={Process.GetCurrentProcess().PeakWorkingSet64 >> 20}MB");
        if (Logger.AllowVerbose)
        {
            Logger.Verbose(LogCategory.Import, "[Unreal] " + UnrealLoadProfile.Summarize(space));
        }
    }

    private static List<GameFile> Admitted(DefaultFileProvider provider, IEnumerable<string> archivePaths, Func<string, bool>? include)
    {
        HashSet<string> archives = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in archivePaths)
        {
            archives.Add(Path.GetFullPath(path));
        }
        List<GameFile> admitted = new();
        foreach (IAesVfsReader reader in provider.MountedVfs)
        {
            if (!archives.Contains(Path.GetFullPath(reader.Path)))
            {
                continue;
            }
            foreach (GameFile file in reader.Files.Values)
            {
                if (file.IsUePackage && (include is null || include(file.Path)))
                {
                    admitted.Add(file);
                }
            }
        }
        return admitted;
    }
}
