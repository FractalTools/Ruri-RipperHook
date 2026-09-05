using AssetRipper.Assets.Bundles;
using AssetRipper.Import.Logging;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.VirtualFileSystem;
using Ruri.FModelHook.ShaderDecompiler.Semantics;
using Ruri.FModelHook.UnityConverter.TypeTree;
using Ruri.RipperHook.Conversion;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ruri.FModelHook.UnityConverter;

/// <summary>
/// What every converter shares across one load: the mounted provider, and the MonoScript each
/// reflected class is bound to (one per class name for the whole load, in a package of its own).
/// </summary>
public sealed class UnrealLoadShared
{
    private readonly ConcurrentDictionary<string, IMonoScript> scripts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Converters.UnrealRig> rigs = new(StringComparer.Ordinal);
    private readonly object scriptGate = new();

    public UnrealLoadShared(UnrealFileProvider provider, ConvertedPackage scriptPackage)
    {
        Provider = provider;
        ScriptPackage = scriptPackage;
        Semantics = UnrealSourceOptions.Flag(UnrealSourceOptions.MaterialSemantics) ? provider.Semantics : null;
    }

    /// <summary>The mount's material semantics, when the load asked for them.</summary>
    public MaterialSemanticsResolver? Semantics { get; }

    public UnrealFileProvider Provider { get; }

    public ConvertedPackage ScriptPackage { get; }

    /// <summary>The rig of a skeletal mesh, built once per load from its reference skeleton and shared by every placement of it.</summary>
    public Converters.UnrealRig Rig(USkeletalMesh mesh, SourceBasis basis) =>
        rigs.GetOrAdd(mesh.GetPathName(), _ => Converters.UnrealRig.From(mesh, basis));

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
/// every package allocates its assets in parallel from its export map alone, then every
/// package is deserialized and filled in parallel, so a reference from any export to any other
/// resolves without ordering the packages and no package's data is held before its own fill.
/// What counts as "asked for" is the archive files handed over, narrowed by the include
/// predicate the cabmap closure states; the seed predicate marks the packages the closure was
/// asked for by name, which are the ones that stand as prefab roots of their own -- a
/// package reached as a dependency contributes its assets and nothing at the top level.
/// A package is let go the moment its assets are filled:
/// the provider forgets it and the loader drops its objects, so what stays in memory is the
/// Unity side plus whatever a package still being filled reaches into.
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

    public static void Load(GameBundle bundle, IEnumerable<string> archivePaths, Func<string, bool>? include, Func<string, bool>? seed)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        UnrealFileProvider provider = UnrealProviderSession.Current;
        List<GameFile> admitted = Admitted(provider, archivePaths, include);
        if (admitted.Count == 0)
        {
            Logger.Warning(LogCategory.Import, "[Unreal] No package admitted for loading.");
            return;
        }
        if (provider.MappingsContainer is SchemalessMappingsProvider && admitted.FirstOrDefault(static file => UnrealProviderSession.Current.LoadPackage(file) is AbstractUePackage package
                && package.HasFlags(EPackageFlags.PKG_UnversionedProperties)) is { } unversioned)
        {
            throw new InvalidOperationException(
                $"'{unversioned.Path}' stores its properties unversioned, which only the game's own reflection schema can read: "
                + $"state the .usmap with the '{UnrealSourceOptions.Mappings}' option (Load Options Form). "
                + "Ruri.Tpk --unreal-reflection <game executable> writes one. Without it a load yields empty meshes and bare skeletons.");
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
        int failedPackages = 0;
        TypeMappings? mappings = provider.MappingsForGame;
        Parallel.For(0, admitted.Count, index =>
        {
            GameFile file = admitted[index];
            try
            {
                if (provider.LoadPackage(file) is not AbstractUePackage package)
                {
                    throw new InvalidDataException("not a package with an export map");
                }
                UnrealConversion conversion = new(space, space.Package(file.Path), file.Path, table, Basis, shared, seed is null || seed(file.Path));
                for (int slot = 0; slot < package.ExportMapLength; slot++)
                {
                    if (package.ResolvePackageIndex(new FPackageIndex(package, slot + 1)) is not { } header)
                    {
                        continue;
                    }
                    try
                    {
                        UnrealConverters.ForClassName(UnrealConversion.ClassOf(header), mappings).Allocate(conversion, header);
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning(LogCategory.Import, $"[Unreal] Allocate {file.Path}:{header.Name} ({UnrealConversion.ClassOf(header)}): {exception.GetType().Name}: {exception.Message}");
                        Logger.Verbose(LogCategory.Import, exception.ToString());
                    }
                }
                conversions[index] = conversion;
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failedPackages);
                Logger.Warning(LogCategory.Import, $"[Unreal] Load {file.Path}: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                provider.Forget(file.Path);
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
            GameFile file = admitted[index];
            try
            {
                foreach (UObject export in provider.LoadPackage(file).GetExports())
                {
                    try
                    {
                        UnrealConverters.ForClassName(UnrealConversion.ClassName(export.Class), mappings).Fill(conversion, export);
                    }
                    catch (Exception exception)
                    {
                        Interlocked.Increment(ref failedExports);
                        Logger.Warning(LogCategory.Import, $"[Unreal] Fill {conversion.PackagePath}:{export.Name} ({export.ExportType}): {exception.GetType().Name}: {exception.Message}");
                        Logger.Verbose(LogCategory.Import, exception.ToString());
                    }
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failedPackages);
                Logger.Warning(LogCategory.Import, $"[Unreal] Read {file.Path}: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                conversions[index] = null;
                provider.Forget(file.Path);
            }
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
