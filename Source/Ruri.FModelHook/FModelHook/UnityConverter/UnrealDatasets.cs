using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.VirtualFileSystem;
using Ruri.RipperHook.CabMapping;
using Ruri.RipperHook.Data;
using Ruri.RipperHook.Tables;

namespace Ruri.FModelHook.UnityConverter;

/// <summary>
/// What the Unreal decoder publishes for a host to draw: the source options it reads (so the
/// form is the schema, never a hand-kept copy), the mounted session, and its archives.
/// </summary>
public static class UnrealDatasets
{
    public const string IdPrefix = "unreal.";
    public const string SettingsSchemaId = "unreal.settings.schema";
    public const string SessionId = "unreal.session";
    public const string ArchivesId = "unreal.archives";
    public const string WorldsId = "unreal.worlds";
    public const string WorldCellsId = "unreal.world.cells";
    public const string ActorsId = "unreal.actors";
    public const string WorldParam = "world";
    public const string MinXParam = "minX";
    public const string MinYParam = "minY";
    public const string MaxXParam = "maxX";
    public const string MaxYParam = "maxY";
    public const string LevelParam = "level";
    private const char ListSeparator = ';';

    public static void Register()
    {
        Datasets.Publish(SettingsSchemaId, DataRole.Introspection, [],
            "Every source option this decoder reads: name, kind (text|flag|choice|path|entries), default, "
            + "choices ('|'-separated for a choice), what it means, and whether the mounted build cannot be read without it "
            + "(the reflection schema, for a build that stores its properties unversioned). A host draws its form from this.",
            SettingsSchema);

        Datasets.Publish(SessionId, DataRole.Session, [],
            "The mounted Unreal session: project, engine, archive and file counts, whether a property "
            + "schema (.usmap) is loaded, how many archives still wait for a key, and how many metres "
            + "one of the engine's own units is, so a host can state a world's size without keeping "
            + "its own copy of that scale.",
            SessionState);

        Datasets.Publish(ArchivesId, DataRole.Diagnostic, [],
            "Every archive the install ships: path, encryption, whether it mounted, its key guid and file count.",
            Archives);
        Datasets.Publish(ActorsId, DataRole.CharacterRoster, [],
            "Every actor the install ships as a Blueprint class: its package, its name, its kind by the engine's own "
            + "ancestry (Character, Pawn or Actor), the class it extends, the first "
            + "engine class in its ancestry, and -- with a cabmap loaded -- how many skeletal and static mesh packages it "
            + "imports directly. Importing the package places the actor with its components, the way a level would.",
            Actors);
        Datasets.Publish(WorldsId, DataRole.SceneList, [],
            "Every world the install ships outside a World Partition's generated folder: its package, whether its "
            + "persistent level is partitioned, how many streaming cells a partitioned one lists, and the ground "
            + "those cells cover in Unreal units -- the union of their bounds, zero for a world with none.",
            Worlds);
        Datasets.Publish(WorldCellsId, DataRole.PlaceList,
            [DataParam.Text(WorldParam), DataParam.Real(MinXParam, required: false), DataParam.Real(MinYParam, required: false),
                DataParam.Real(MaxXParam, required: false), DataParam.Real(MaxYParam, required: false), DataParam.Integer(LevelParam, required: false)],
            "The streaming cells of one partitioned world: the generated level package each cell's actors live in, "
            + "its runtime grid and hierarchical level, the world bounds of its content in Unreal units, its loading "
            + "range and priority, whether it is always loaded, an HLOD or client-only, its data layers, and whether "
            + "the install carries its package. Stating a window (minX, minY, maxX, maxY in Unreal units) keeps only the cells "
            + "whose bounds cross it, an always-loaded cell belonging to every window; stating a level keeps one hierarchical level.",
            WorldCells);
    }

    private static ColumnTable Actors(DataRequest request)
    {
        TableBuilder table = new(ActorsId, "package", "name", "kind", "parent", "native", "skeletal#", "static#");
        UnrealFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        Dictionary<string, int> cabIds = request.HasMap ? CabIds(request.Map) : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (UnrealActorScan.Actor actor in UnrealActorScan.Scan(provider))
        {
            (int skeletal, int statics) = request.HasMap && cabIds.TryGetValue(actor.Package, out int id) ? MeshDependencies(request.Map, id) : (0, 0);
            table.Row(actor.Package, actor.Name, actor.Kind, actor.Parent, actor.Native, skeletal, statics);
        }
        return table.Build();
    }

    private static Dictionary<string, int> CabIds(CabTable map)
    {
        Dictionary<string, int> ids = new(map.Count, StringComparer.OrdinalIgnoreCase);
        for (int id = 0; id < map.Count; id++)
        {
            ids[map.CabName(id)] = id;
        }
        return ids;
    }

    /// <summary>How many of a package's direct dependencies carry a skeletal mesh, and how many a static one, by the classes the cabmap lists for them.</summary>
    private static (int Skeletal, int Static) MeshDependencies(CabTable map, int id)
    {
        int skeletal = 0;
        int statics = 0;
        foreach (int dependency in map.Dependencies(id))
        {
            ReadOnlySpan<int> classIds = map.ClassIds(dependency);
            if (classIds.IndexOf((int)ClassIDType.Mesh) < 0)
            {
                continue;
            }
            if (classIds.IndexOf((int)ClassIDType.SkinnedMeshRenderer) >= 0)
            {
                skeletal++;
            }
            else if (classIds.IndexOf((int)ClassIDType.MeshRenderer) >= 0)
            {
                statics++;
            }
        }
        return (skeletal, statics);
    }

    private static ColumnTable Worlds(DataRequest request)
    {
        TableBuilder table = new(WorldsId, "world", "name", "partitioned", "cells#", "minX#", "minY#", "maxX#", "maxY#");
        UnrealFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        string generatedMarker = "/" + UnrealWorldPartition.GeneratedFolder + "/";
        foreach (GameFile file in provider.Files.Values.OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!file.IsUePackage || !file.Path.EndsWith(UnrealWorldPartition.WorldExtension, StringComparison.OrdinalIgnoreCase)
                || file.Path.Contains(generatedMarker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            bool partitioned = UnrealWorldPartition.IsPartitioned(provider, file);
            IReadOnlyList<UnrealWorldCell> cells = partitioned ? UnrealWorldPartition.Cells(provider, file.Path) : [];
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (UnrealWorldCell cell in cells)
            {
                minX = Math.Min(minX, cell.Bounds.Min.X);
                minY = Math.Min(minY, cell.Bounds.Min.Y);
                maxX = Math.Max(maxX, cell.Bounds.Max.X);
                maxY = Math.Max(maxY, cell.Bounds.Max.Y);
            }
            bool bounded = minX <= maxX && minY <= maxY;
            table.Row(file.Path, file.NameWithoutExtension, partitioned ? "1" : "0", cells.Count,
                bounded ? minX : 0, bounded ? minY : 0, bounded ? maxX : 0, bounded ? maxY : 0);
        }
        return table.Build();
    }

    private static ColumnTable WorldCells(DataRequest request)
    {
        TableBuilder table = new(WorldCellsId, "cell", "level", "grid", "hlevel#", "loadingRange#", "priority#",
            "minX#", "minY#", "minZ#", "maxX#", "maxY#", "maxZ#", "alwaysLoaded", "hlod", "clientOnly", "dataLayers", "present");
        UnrealFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        bool windowed = request.Given(MinXParam) || request.Given(MaxXParam) || request.Given(MinYParam) || request.Given(MaxYParam);
        double minX = request.Real(MinXParam);
        double minY = request.Real(MinYParam);
        double maxX = request.Real(MaxXParam);
        double maxY = request.Real(MaxYParam);
        bool leveled = request.Given(LevelParam);
        int level = request.Integer(LevelParam);
        foreach (UnrealWorldCell cell in UnrealWorldPartition.Cells(provider, request.Text(WorldParam)))
        {
            if (leveled && !cell.AlwaysLoaded && cell.Level != level)
            {
                continue;
            }
            if (windowed && !cell.AlwaysLoaded
                && (cell.Bounds.Max.X < minX || cell.Bounds.Min.X > maxX || cell.Bounds.Max.Y < minY || cell.Bounds.Min.Y > maxY))
            {
                continue;
            }
            table.Row(cell.Name, cell.LevelPackage, cell.Grid, cell.Level, cell.LoadingRange, cell.Priority,
                cell.Bounds.Min.X, cell.Bounds.Min.Y, cell.Bounds.Min.Z, cell.Bounds.Max.X, cell.Bounds.Max.Y, cell.Bounds.Max.Z,
                cell.AlwaysLoaded ? "1" : "0", cell.Hlod ? "1" : "0", cell.ClientOnlyVisible ? "1" : "0",
                string.Join(ListSeparator, cell.DataLayers), provider.Files.ContainsKey(cell.LevelPackage) ? "1" : "0");
        }
        return table.Build();
    }

    private static ColumnTable SettingsSchema(DataRequest request)
    {
        TableBuilder table = new(SettingsSchemaId, "name", "kind", "default", "choices", "description", "required");
        bool engineUnstated = EngineUnstated(request.GameRoot);
        bool engineKnown = !engineUnstated || UnrealSourceOptions.EngineChoice() is not null;
        bool unversioned = engineKnown && StoresPropertiesUnversioned(request.GameRoot);
        foreach (UnrealSourceOptions.Option option in UnrealSourceOptions.Schema)
        {
            bool required = string.Equals(option.Name, UnrealSourceOptions.Engine, StringComparison.Ordinal) ? engineUnstated
                : unversioned && string.Equals(option.Name, UnrealSourceOptions.Mappings, StringComparison.Ordinal);
            table.Row(option.Name, option.Kind, option.Default, option.Choices, option.Description, required ? "1" : "0");
        }
        return table.Build();
    }

    /// <summary>
    /// Whether the open install's executable states no engine version -- true only for a root that
    /// holds archive folders and whose executable carries no build version literal; false while
    /// no install is open, when the question cannot be asked.
    /// </summary>
    private static bool EngineUnstated(string gameRoot)
    {
        if (gameRoot.Length == 0)
        {
            return false;
        }
        string[] pakFolders = UnrealInstall.PakFolders(gameRoot);
        return pakFolders.Length > 0 && UnrealInstall.EngineFromVersion(UnrealInstall.EngineVersion(pakFolders[0])) is null;
    }

    /// <summary>
    /// Whether the mounted build stores its objects' properties unversioned -- the layout only
    /// the build's own reflection schema can read -- judged by the first package the mount
    /// holds, every package of one cook sharing the flag. False while nothing mounts (an
    /// archive still waiting for its key), when the question cannot be answered yet.
    /// </summary>
    private static bool StoresPropertiesUnversioned(string gameRoot)
    {
        if (gameRoot.Length == 0)
        {
            return false;
        }
        try
        {
            UnrealFileProvider provider = UnrealProviderSession.Open(gameRoot);
            foreach (GameFile file in provider.Files.Values)
            {
                if (file.IsUePackage)
                {
                    return provider.LoadUncached(file) is AbstractUePackage package && package.HasFlags(EPackageFlags.PKG_UnversionedProperties);
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] Could not tell whether the build stores its properties unversioned: {exception.GetType().Name}: {exception.Message}");
        }
        return false;
    }

    private static ColumnTable SessionState(DataRequest request)
    {
        TableBuilder table = new(SessionId, "project", "displayName", "engine", "engineVersion", "files#", "archives#",
            "mounted#", "missingKeys#", "mappings", "structs#", "unitScale#");
        DefaultFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        string[] pakFolders = UnrealInstall.PakFolders(request.GameRoot);
        table.Row(
            provider.ProjectName,
            provider.GameDisplayName ?? string.Empty,
            provider.Versions.Game.ToString(),
            pakFolders.Length > 0 ? UnrealInstall.EngineVersion(pakFolders[0]) : string.Empty,
            provider.Files.Count,
            provider.MountedVfs.Count + provider.UnloadedVfs.Count,
            provider.MountedVfs.Count,
            provider.RequiredKeys.Count,
            UnrealSourceOptions.Text(UnrealSourceOptions.Mappings),
            provider.MappingsForGame?.Types.Count ?? 0,
            UnrealPackageLoader.Basis.UnitScale);
        return table.Build();
    }

    private static ColumnTable Archives(DataRequest request)
    {
        TableBuilder table = new(ArchivesId, "name", "path", "encrypted", "mounted", "keyGuid", "files#");
        DefaultFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        foreach (IAesVfsReader reader in provider.MountedVfs)
        {
            table.Row(reader.Name, reader.Path, reader.IsEncrypted ? "1" : "0", "1", reader.EncryptionKeyGuid.ToString(), reader.FileCount);
        }
        foreach (IAesVfsReader reader in provider.UnloadedVfs)
        {
            table.Row(reader.Name, reader.Path, reader.IsEncrypted ? "1" : "0", "0", reader.EncryptionKeyGuid.ToString(), 0);
        }
        return table.Build();
    }
}
