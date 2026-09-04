using CUE4Parse.FileProvider;
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
