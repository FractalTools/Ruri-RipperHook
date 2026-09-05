using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.VirtualFileSystem;
using Ruri.RipperHook.Data;
using Ruri.RipperHook.Tables;

namespace Ruri.FModelHook.Ripper;

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
            + "choices ('|'-separated for a choice) and what it means. A host draws its form from this.",
            SettingsSchema);

        Datasets.Publish(SessionId, DataRole.Session, [],
            "The mounted Unreal session: project, engine, archive and file counts, whether a property "
            + "schema (.usmap) is loaded, and how many archives still wait for a key.",
            SessionState);

        Datasets.Publish(ArchivesId, DataRole.Diagnostic, [],
            "Every archive the install ships: path, encryption, whether it mounted, its key guid and file count.",
            Archives);
        Datasets.Publish(WorldsId, DataRole.SceneList, [],
            "Every world the install ships outside a World Partition's generated folder: its package, whether its "
            + "persistent level is partitioned, and how many streaming cells a partitioned one lists.",
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

    private static ColumnTable Worlds(DataRequest request)
    {
        TableBuilder table = new(WorldsId, "world", "name", "partitioned", "cells#");
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
            int cells = partitioned ? UnrealWorldPartition.Cells(provider, file.Path).Count : 0;
            table.Row(file.Path, file.NameWithoutExtension, partitioned ? "1" : "0", cells);
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
        TableBuilder table = new(SettingsSchemaId, "name", "kind", "default", "choices", "description");
        foreach (UnrealSourceOptions.Option option in UnrealSourceOptions.Schema)
        {
            table.Row(option.Name, option.Kind, option.Default, option.Choices, option.Description);
        }
        return table.Build();
    }

    private static ColumnTable SessionState(DataRequest request)
    {
        TableBuilder table = new(SessionId, "project", "displayName", "engine", "engineVersion", "files#", "archives#",
            "mounted#", "missingKeys#", "mappings", "structs#");
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
            provider.MappingsForGame?.Types.Count ?? 0);
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
