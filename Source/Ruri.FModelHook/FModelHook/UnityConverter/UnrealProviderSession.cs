using AssetRipper.Import.Logging;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures.BC;
using Ruri.RipperHook.Data;

namespace Ruri.FModelHook.UnityConverter;

/// <summary>
/// The ONE mounted CUE4Parse provider of the session: every archive under the install's pak
/// folders (plus the extra folders stated), opened with the stated keys, versioned the way the
/// stated options say, its property schema the stated mappings. Built on first use, kept while
/// the install and the options stay what they were, dropped the moment either changes -- a
/// provider mounted under yesterday's key is not a provider for today's.
/// </summary>
public static class UnrealProviderSession
{
    private static readonly object Gate = new();
    private static UnrealFileProvider? provider;
    private static string fingerprint = string.Empty;

    static UnrealProviderSession()
    {
        Session.OptionsChanged += Close;
    }

    public static bool IsOpen => provider is not null;

    public static UnrealFileProvider Current => Open(Session.GameRoot);

    public static UnrealFileProvider Open(string gameRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        string wanted = gameRoot + "\n" + UnrealSourceOptions.Fingerprint();
        lock (Gate)
        {
            if (provider is not null && fingerprint == wanted)
            {
                return provider;
            }
            provider?.Dispose();
            provider = null;
            provider = Mount(gameRoot);
            fingerprint = wanted;
            return provider;
        }
    }

    public static void Close()
    {
        lock (Gate)
        {
            provider?.Dispose();
            provider = null;
            fingerprint = string.Empty;
        }
    }

    private static bool codecsReady;

    /// <summary>
    /// The native codecs CUE4Parse decompresses and decodes through -- Oodle for archive
    /// blocks, zlib-ng for the older ones, Detex for block-compressed texels -- loaded from the
    /// folder the codecs option names, or the .data folder beside the kernel when it names none.
    /// Natives bind once per process, so this runs once; a codec that is not there is named,
    /// with the option that would point at it, and the rest stay usable. Nothing is fetched.
    /// </summary>
    private static void EnsureCodecs()
    {
        if (codecsReady)
        {
            return;
        }
        codecsReady = true;
        string stated = UnrealSourceOptions.Text(UnrealSourceOptions.Codecs);
        string dataDirectory = stated.Length > 0
            ? Path.GetFullPath(stated)
            : Path.Combine(Path.GetDirectoryName(typeof(Ruri.RipperHook.Bootstrap).Assembly.Location) ?? AppContext.BaseDirectory, ".data");
        string oodlePath = Path.Combine(dataDirectory, OodleHelper.OODLE_NAME_CURRENT);
        if (!File.Exists(oodlePath))
        {
            oodlePath = Path.Combine(dataDirectory, OodleHelper.OODLE_NAME_OLD);
        }
        LoadCodec("Oodle", oodlePath, OodleHelper.Initialize);
        LoadCodec("zlib-ng", Path.Combine(dataDirectory, ZlibHelper.DLL_NAME), ZlibHelper.Initialize);
        LoadCodec("Detex", Path.Combine(dataDirectory, DetexHelper.DLL_NAME), path =>
        {
            DetexHelper.LoadDll(path);
            DetexHelper.Initialize(path);
        });
    }

    private static void LoadCodec(string codec, string path, Action<string> initialize)
    {
        if (!File.Exists(path))
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {codec} codec not found at '{path}'; state its folder with the '{UnrealSourceOptions.Codecs}' option.");
            return;
        }
        try
        {
            initialize(path);
        }
        catch (Exception exception)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {codec} codec failed to load from '{path}': {exception.Message}");
        }
    }

    private static UnrealFileProvider Mount(string gameRoot)
    {
        EnsureCodecs();
        string[] pakFolders = UnrealInstall.PakFolders(gameRoot);
        if (pakFolders.Length == 0)
        {
            throw new DirectoryNotFoundException($"[Unreal] No Paks folder holding .pak/.utoc archives under '{gameRoot}'.");
        }
        string engineVersion = UnrealInstall.EngineVersion(pakFolders[0]);
        EGame game = UnrealSourceOptions.EngineChoice()
            ?? UnrealInstall.EngineFromVersion(engineVersion)
            ?? throw new InvalidOperationException(
                $"[Unreal] The executable under '{pakFolders[0]}' states no engine version; state one with the '{UnrealSourceOptions.Engine}' option.");

        VersionContainer versions = new(
            game: game,
            platform: UnrealSourceOptions.TexturePlatformChoice(),
            customVersions: UnrealSourceOptions.CustomVersionContainer(),
            optionOverrides: UnrealSourceOptions.OptionOverrideTable(),
            mapStructTypesOverrides: UnrealSourceOptions.MapStructTypeTable());

        DirectoryInfo[] extra = UnrealSourceOptions.ExtraDirectoryList()
            .Concat(pakFolders.Skip(1))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => new DirectoryInfo(path))
            .ToArray();
        UnrealFileProvider mounted = new(new DirectoryInfo(pakFolders[0]), extra, SearchOption.AllDirectories, versions, StringComparer.OrdinalIgnoreCase);
        mounted.ReadScriptData = false;
        mounted.ReadShaderMaps = UnrealSourceOptions.Flag(UnrealSourceOptions.ReadShaderMaps);
        mounted.ReadNaniteData = true;
        mounted.Initialize();
        mounted.Mount();
        List<KeyValuePair<CUE4Parse.UE4.Objects.Core.Misc.FGuid, CUE4Parse.Encryption.Aes.FAesKey>> keys = UnrealSourceOptions.AesKeys().ToList();
        if (keys.Count > 0)
        {
            mounted.SubmitKeys(keys);
        }
        mounted.PostMount();

        string mappings = UnrealSourceOptions.Text(UnrealSourceOptions.Mappings);
        if (mappings.Length > 0)
        {
            if (!File.Exists(mappings))
            {
                throw new FileNotFoundException($"[Unreal] The '{UnrealSourceOptions.Mappings}' option names a .usmap that does not exist.", mappings);
            }
            mounted.MappingsContainer = new FileUsmapTypeMappingsProvider(mappings);
        }
        else
        {
            mounted.MappingsContainer = new SchemalessMappingsProvider();
            Logger.Warning(LogCategory.Import,
                $"[Unreal] No reflection schema stated ('{UnrealSourceOptions.Mappings}'): package headers read, but no object with unversioned properties can be converted until a .usmap is given.");
        }
        mounted.LoadVirtualPaths();

        Logger.Info(LogCategory.Import,
            $"[Unreal] Mounted {mounted.MountedVfs.Count}/{mounted.MountedVfs.Count + mounted.UnloadedVfs.Count} archives of '{mounted.ProjectName}' "
            + $"({game}, engine {engineVersion}) files={mounted.Files.Count} mappings={(mounted.MappingsForGame?.Types.Count ?? 0)} structs "
            + $"missingKeys={mounted.RequiredKeys.Count}");
        return mounted;
    }
}
