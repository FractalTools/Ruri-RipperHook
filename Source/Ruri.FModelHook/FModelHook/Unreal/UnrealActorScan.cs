using AssetRipper.Import.Logging;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.UObject;
using System.Collections.Concurrent;

namespace Ruri.FModelHook.Unreal;

/// <summary>
/// The actors a build ships as Blueprint classes: every package whose generated class descends
/// from the engine's actor class, read off the package headers alone -- the class export's
/// super chain, followed across packages to the first class the property schema names, then
/// that schema's own parent chain. Nothing is deserialized beyond the export maps, so the whole
/// install is classified in the time its headers take to read.
/// </summary>
public static class UnrealActorScan
{
    private const string BlueprintClassName = "BlueprintGeneratedClass";
    private const string ActorClassName = "Actor";
    private const string PawnClassName = "Pawn";
    private const string CharacterClassName = "Character";
    private const int Parallelism = 8;

    /// <summary>One shipped actor class: its package, its name, its kind by the engine's own ancestry (Character, Pawn or Actor), the class it extends, and the first engine class above it.</summary>
    public readonly record struct Actor(string Package, string Name, string Kind, string Parent, string Native);

    public static List<Actor> Scan(UnrealFileProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        List<Actor> actors = new();
        if (provider.MappingsContainer is SchemalessMappingsProvider || provider.MappingsForGame is not { } mappings)
        {
            throw new InvalidOperationException($"No reflection schema is loaded, so no class can be told an actor: state the .usmap with the '{UnrealSourceOptions.Mappings}' option (Load Options Form).");
        }
        ConcurrentBag<Actor> found = new();
        int unreadable = 0;
        Parallel.ForEach(provider.Files.Values, new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, file =>
        {
            if (!file.IsUePackage)
            {
                return;
            }
            try
            {
                if (Classify(provider, mappings, file) is { } actor)
                {
                    found.Add(actor);
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref unreadable);
            }
        });
        if (unreadable > 0)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {unreadable} package header(s) could not be read while listing actors.");
        }
        actors.AddRange(found.OrderBy(static actor => actor.Package, StringComparer.OrdinalIgnoreCase));
        return actors;
    }

    /// <summary>The package's actor class, when the package holds a generated class descending from the engine's actor class.</summary>
    private static Actor? Classify(UnrealFileProvider provider, TypeMappings mappings, GameFile file)
    {
        if (provider.LoadUncached(file) is not AbstractUePackage package)
        {
            return null;
        }
        for (int index = 0; index < package.ExportMapLength; index++)
        {
            ResolvedObject? export = package.ResolvePackageIndex(new FPackageIndex(package, index + 1));
            if (export is null || !string.Equals(export.Class?.Name.Text, BlueprintClassName, StringComparison.Ordinal))
            {
                continue;
            }
            ResolvedObject? parent = export.Super;
            string? native = NativeAncestor(parent, mappings);
            if (native is null || !UnrealClasses.IsA(native, ActorClassName, mappings))
            {
                return null;
            }
            return new Actor(file.Path, file.NameWithoutExtension, Kind(native, mappings), parent?.Name.Text ?? string.Empty, native);
        }
        return null;
    }

    /// <summary>Character when the engine class descends from Character, Pawn when from Pawn, else Actor.</summary>
    private static string Kind(string native, TypeMappings mappings) =>
        UnrealClasses.IsA(native, CharacterClassName, mappings) ? CharacterClassName
        : UnrealClasses.IsA(native, PawnClassName, mappings) ? PawnClassName
        : ActorClassName;

    /// <summary>The first class up the super chain that the property schema names -- the engine class a Blueprint chain bottoms out in -- or null when the chain ends before one.</summary>
    internal static string? NativeAncestor(ResolvedObject? start, TypeMappings mappings)
    {
        ResolvedObject? cursor = start;
        HashSet<string> seen = new(StringComparer.Ordinal);
        while (cursor is not null && seen.Add(cursor.GetPathName()))
        {
            string name = cursor.Name.Text;
            if (mappings.Types.ContainsKey(name))
            {
                return name;
            }
            cursor = cursor.Super;
        }
        return null;
    }
}
