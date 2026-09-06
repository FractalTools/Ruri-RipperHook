using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.TextureDecoder.Rgb.Formats;
using CUE4Parse.UE4.Assets.Exports.Material;
using AssetRipper.Numerics;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using Ruri.FModelHook.ShaderDecompiler.Semantics;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Options;
using Ruri.FModelHook.Unreal.Converters;
using Ruri.RipperHook.Conversion;
using System.Numerics;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.VirtualFileSystem;
using Ruri.RipperHook.CabMapping;
using Ruri.RipperHook.Data;
using Ruri.RipperHook.Tables;

namespace Ruri.FModelHook.Unreal;

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
    public const string MeshGeometryId = "unreal.mesh.geometry";
    public const string MeshSkeletonId = "unreal.mesh.skeleton";
    public const string MaterialsId = "unreal.materials";
    public const string TexturesId = "unreal.textures";
    public const string PackageParam = "package";
    public const string MaterialParam = "material";
    public const string TextureParam = "texture";
    public const string WorldParam = "world";
    public const string MinXParam = "minX";
    public const string MinYParam = "minY";
    public const string MaxXParam = "maxX";
    public const string MaxYParam = "maxY";
    public const string LevelParam = "level";
    private const char ListSeparator = ';';
    private const string MaterialRow = "m";
    private const string KeywordRow = "k";
    private const string TextureRow = "t";
    private const string ScalarRow = "f";
    private const string VectorRow = "c";

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
        Datasets.Publish(MeshGeometryId, DataRole.Internal, [DataParam.Text(PackageParam)],
            "Every LOD of every mesh in one package as raw buffers -- positions, normals, tangents, "
            + "every texture coordinate set, colours, triangle indices, the material sections and -- for "
            + "a skeletal mesh -- four influences a vertex beside the bone names they index. Already in "
            + "the host's basis, beside the object path of the material each slot names. "
            + "The geometry without the conversion: no Unity asset, no export, no text.",
            MeshGeometry);
        Datasets.Publish(MeshSkeletonId, DataRole.Internal, [DataParam.Text(PackageParam)],
            "The reference skeleton of every skeletal mesh in one package, bone by bone in the order "
            + "the meshes' weights index them: name, parent, the local transform it rests at in the "
            + "host's basis, and the path a clip addresses it by.",
            MeshSkeleton);
        Datasets.Publish(MaterialsId, DataRole.Internal, [DataParam.List(MaterialParam)],
            "The parameter set each named material interface resolves to, the way the engine resolves it "
            + "(the base material's cached defaults, each instance overriding by name, then what the "
            + "compiled base pass proves about the slots). One row per entry, told apart by 'kind': "
            + "'m' the material itself (name, and the base material of its chain under 'texture'), "
            + "'k' an input its graph connects, 't' a texture parameter (the texture's object path under "
            + "'texture'), 'f' a scalar (x), 'c' a vector (x y z w). No Unity Shader, no Material, no text.",
            Materials);
        Datasets.Publish(TexturesId, DataRole.Internal, [DataParam.List(TextureParam)],
            "Each named texture decoded to pixels and handed over in a container a host loads directly: "
            + "its object path, its own name, its size, and whether the asset itself declares sRGB encoding "
            + "or a normal map -- the two facts a host must not guess from a slot or a file name.",
            Textures);
    }

    /// <summary>
    /// Every LOD of every mesh in one package, as the buffers a host writes into its own mesh:
    /// every vertex stream the engine stores (positions, normals, tangents, colours, every
    /// texture coordinate set), the triangle indices, the material sections as int triples
    /// (first index, index count, material slot), and -- for a skeletal mesh -- four influences
    /// a vertex beside the bone names its indices address. Coordinates are already in the
    /// host's basis.
    ///
    /// This is the geometry WITHOUT the detour: the package is read, the LOD decoded and the
    /// buffers handed over. Nothing here creates a Unity asset, runs an export or writes text.
    /// </summary>
    private static ColumnTable MeshGeometry(DataRequest request)
    {
        TableBuilder table = new(MeshGeometryId, "name", "lod#", "vertices#", "positions@", "normals@",
            "tangents@", "colors@", "uv@", "uvSets", "indices@", "sections@", "skin@", "bones", "materials");
        UnrealFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        string package = request.Text(PackageParam);
        if (!provider.Files.TryGetValue(package, out GameFile? file))
        {
            return table.Build();
        }
        foreach (UObject export in provider.LoadUncached(file).GetExports())
        {
            switch (export)
            {
                case UStaticMesh staticMesh:
                {
                    using StaticMeshDto dto = new(staticMesh, EMeshQuality.All, ENaniteMeshFormat.NoNanite);
                    Rows(table, export.Name, dto, null, null);
                    break;
                }
                case USkeletalMesh skeletalMesh:
                {
                    using SkeletalMeshDto dto = new(skeletalMesh, EMeshQuality.All, ENaniteMeshFormat.NoNanite);
                    Rows(table, export.Name, dto, static vertex => vertex.Influences,
                        UnrealRig.From(skeletalMesh, UnrealPackageLoader.Basis));
                    break;
                }
            }
        }
        return table.Build();
    }

    /// <summary>Every LOD of one mesh, decoded through the one reading both lanes share.</summary>
    private static void Rows<TVertex>(TableBuilder table, string name, MeshDto<TVertex> dto,
        Func<TVertex, MeshBoneInfluenceDto[]>? influences, UnrealRig? rig)
        where TVertex : struct, IMeshVertex
    {
        string[]? boneNames = rig?.Names;
        string? rootBone = boneNames is { Length: > 0 } ? boneNames[0] : null;
        string materials = string.Join(ListSeparator, dto.Materials.Select(static slot =>
            slot.Material is { IsNull: false } pointer ? pointer.ResolvedObject?.GetPathName() ?? string.Empty : string.Empty));
        foreach (MeshLodDto<TVertex> lod in dto.LODs)
        {
            MeshGeometry geometry = UnrealMeshGeometry.FromLod(name, dto, lod, UnrealPackageLoader.Basis,
                influences, rig?.BindPoses, boneNames, rootBone);
            int[] sections = new int[geometry.Sections.Length * 3];
            for (int index = 0; index < geometry.Sections.Length; index++)
            {
                MeshSection section = geometry.Sections[index];
                sections[index * 3] = section.FirstIndex;
                sections[index * 3 + 1] = section.IndexCount;
                sections[index * 3 + 2] = section.MaterialIndex;
            }
            // Texture coordinate sets are sparse -- a set whose length disagreed with the vertex
            // count is left out -- so the sets present are named beside the buffer that holds
            // them, and nothing has to infer a set index from a position in the buffer.
            List<int> uvSets = new();
            List<Vector2> uvValues = new();
            for (int set = 0; set < geometry.TexCoords.Length; set++)
            {
                if (geometry.TexCoords[set] is { } values)
                {
                    uvSets.Add(set);
                    uvValues.AddRange(values);
                }
            }
            table.Row(name, (int)lod.SourceLodIndex, geometry.Positions.Length,
                Bytes<Vector3>(geometry.Positions),
                Bytes<Vector3>(geometry.Normals),
                Bytes<Vector4>(geometry.Tangents),
                Bytes<Vector4>(geometry.Colors),
                Bytes<Vector2>(uvValues.ToArray()),
                string.Join(ListSeparator, uvSets),
                Bytes<uint>(geometry.Indices),
                Bytes<int>(sections),
                Bytes<BoneWeight4>(geometry.Skin),
                boneNames is null ? string.Empty : string.Join(ListSeparator, boneNames),
                materials);
        }
    }

    /// <summary>
    /// The reference skeleton of every skeletal mesh in one package, bone by bone in the order
    /// the meshes' weights index them: the bone's own name, its parent, the local transform it
    /// rests at in the host's basis, and the path a clip addresses it by. An armature is built
    /// from this alone -- no Unity rig prefab is created and none is needed.
    /// </summary>
    private static ColumnTable MeshSkeleton(DataRequest request)
    {
        TableBuilder table = new(MeshSkeletonId, "mesh", "bone", "parent#", "px#", "py#", "pz#",
            "qx#", "qy#", "qz#", "qw#", "sx#", "sy#", "sz#", "path");
        UnrealFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        string package = request.Text(PackageParam);
        if (!provider.Files.TryGetValue(package, out GameFile? file))
        {
            return table.Build();
        }
        foreach (UObject export in provider.LoadUncached(file).GetExports())
        {
            if (export is not USkeletalMesh skeletalMesh)
            {
                continue;
            }
            foreach (UnrealRig.Bone bone in UnrealRig.From(skeletalMesh, UnrealPackageLoader.Basis).Bones)
            {
                table.Row(export.Name, bone.Name, bone.ParentIndex,
                    bone.Position.X, bone.Position.Y, bone.Position.Z,
                    bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W,
                    bone.Scale.X, bone.Scale.Y, bone.Scale.Z, bone.Path);
            }
        }
        return table.Build();
    }

    private static ColumnTable Materials(DataRequest request)
    {
        TableBuilder table = new(MaterialsId, "material", "kind", "name", "texture", "x#", "y#", "z#", "w#");
        UnrealFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        MaterialSemanticsResolver? resolver =
            UnrealSourceOptions.Flag(UnrealSourceOptions.MaterialSemantics) ? provider.Semantics : null;
        foreach (string path in Named(request.List(MaterialParam)))
        {
            if (Load<UMaterialInterface>(provider, path) is not { } source)
            {
                continue;
            }
            List<UMaterialInterface> chain = MaterialConverter.Chain(source);
            UnrealMaterialParameters parameters = MaterialConverter.Resolve(provider, resolver, source, chain);
            parameters.StateSurfaceMode();
            table.Row(path, MaterialRow, source.Name, chain[0].GetPathName(), 0d, 0d, 0d, 0d);
            foreach (string keyword in parameters.Keywords)
            {
                table.Row(path, KeywordRow, keyword, string.Empty, 0d, 0d, 0d, 0d);
            }
            foreach ((string name, string? texture) in parameters.Textures)
            {
                table.Row(path, TextureRow, name, texture ?? string.Empty, 0d, 0d, 0d, 0d);
            }
            foreach ((string name, float value) in parameters.Floats)
            {
                table.Row(path, ScalarRow, name, string.Empty, value, 0d, 0d, 0d);
            }
            foreach ((string name, Vector4 color) in parameters.Colors)
            {
                table.Row(path, VectorRow, name, string.Empty, color.X, color.Y, color.Z, color.W);
            }
        }
        return table.Build();
    }

    /// <summary>
    /// Every named texture as pixels in a container the host loads: the package is read on this
    /// thread (one archive, one stream -- reading it from several gains nothing and interleaves),
    /// and the decode and encode, which are computation over a buffer, run on every core.
    /// </summary>
    private static ColumnTable Textures(DataRequest request)
    {
        TableBuilder table = new(TexturesId, "texture", "name", "width#", "height#", "srgb", "normal", "image@");
        UnrealFileProvider provider = UnrealProviderSession.Open(request.GameRoot);
        ETexturePlatform platform = UnrealSourceOptions.TexturePlatformChoice();
        List<(string Path, UTexture Source)> loaded = new();
        foreach (string path in Named(request.List(TextureParam)))
        {
            if (Load<UTexture>(provider, path) is { } source)
            {
                loaded.Add((path, source));
            }
        }
        (int Width, int Height, byte[] Image)[] images = new (int, int, byte[])[loaded.Count];
        WarmEncoder();
        Parallel.For(0, loaded.Count, index => images[index] = Image(loaded[index].Path, loaded[index].Source, platform));
        for (int index = 0; index < loaded.Count; index++)
        {
            (string path, UTexture source) = loaded[index];
            (int width, int height, byte[] image) = images[index];
            table.Row(path, source.Name, width, height, source.SRGB ? "1" : "0", source.IsNormalMap ? "1" : "0", image);
        }
        return table.Build();
    }

    /// <summary>
    /// One texture's pixels in a PNG, through the same encoder every exported texture goes
    /// through. A layout the decoder answers with that has no matching colour type is reported
    /// and yields no image, never a silently reinterpreted one.
    /// </summary>
    private static (int Width, int Height, byte[] Image) Image(string path, UTexture source, ETexturePlatform platform)
    {
        CTexture? decoded;
        try
        {
            decoded = source.Decode(platform);
        }
        catch (Exception exception)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {path} did not decode: {exception.GetType().Name}: {exception.Message}");
            return (0, 0, []);
        }
        if (decoded is null)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {path} has no decodable mip.");
            return (0, 0, []);
        }
        DirectBitmap? bitmap = decoded.PixelFormat switch
        {
            EPixelFormat.PF_B8G8R8A8 => Bitmap<ColorBGRA<byte>, byte>(decoded),
            EPixelFormat.PF_R8G8B8A8 => Bitmap<ColorRGBA<byte>, byte>(decoded),
            EPixelFormat.PF_A8R8G8B8 => Bitmap<ColorARGB<byte>, byte>(decoded),
            EPixelFormat.PF_G8 => Bitmap<ColorR<byte>, byte>(decoded),
            EPixelFormat.PF_R8G8 => Bitmap<ColorRG<byte>, byte>(decoded),
            EPixelFormat.PF_G16 => Bitmap<ColorR<ushort>, ushort>(decoded),
            EPixelFormat.PF_R16F => Bitmap<ColorR<Half>, Half>(decoded),
            EPixelFormat.PF_G16R16F => Bitmap<ColorRG<Half>, Half>(decoded),
            EPixelFormat.PF_FloatRGBA => Bitmap<ColorRGBA<Half>, Half>(decoded),
            EPixelFormat.PF_R32_FLOAT => Bitmap<ColorR<float>, float>(decoded),
            EPixelFormat.PF_G32R32F => Bitmap<ColorRG<float>, float>(decoded),
            EPixelFormat.PF_A32B32G32R32F => Bitmap<ColorRGBA<float>, float>(decoded),
            _ => null,
        };
        if (bitmap is null)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {path} decodes to {decoded.PixelFormat}, which no colour type here states.");
            return (0, 0, []);
        }
        using MemoryStream stream = new();
        bitmap.SaveAsPng(stream);
        return (decoded.Width, decoded.Height, stream.ToArray());
    }

    /// <summary>
    /// Run the encoder's static initialisers on this thread, once for the process.
    /// They are not safe to enter from several threads at once: fpng registers each of its
    /// globals in a plain dictionary keyed by a plain counter, and two initialisers racing it
    /// throw out of a type initializer -- which the runtime caches and rethrows for every
    /// later encode, so the whole process loses PNG encoding over a first-use race.
    /// </summary>
    private static void WarmEncoder() => Warmed.Value.GetType();

    private static readonly Lazy<object> Warmed = new(static () =>
    {
        using MemoryStream stream = new();
        new DirectBitmap<ColorBGRA<byte>, byte>(1, 1, 1).SaveAsPng(stream);
        return stream;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private static DirectBitmap? Bitmap<TColor, TChannel>(CTexture decoded)
        where TChannel : unmanaged
        where TColor : unmanaged, AssetRipper.TextureDecoder.Rgb.IColor<TChannel>
    {
        int size = decoded.Width * decoded.Height * System.Runtime.CompilerServices.Unsafe.SizeOf<TColor>();
        byte[] data = decoded.Data;
        if (data.Length < size)
        {
            return null;
        }
        return new DirectBitmap<TColor, TChannel>(decoded.Width, decoded.Height, 1, data.Length == size ? data : data[..size]);
    }

    /// <summary>The named objects, each asked for once, in the order they were named.</summary>
    private static IEnumerable<string> Named(string[] paths)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (path.Length > 0 && seen.Add(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>The object one path names, or null with a line saying why -- a path a mount does not carry is data, not a fault.</summary>
    private static T? Load<T>(UnrealFileProvider provider, string path) where T : UObject
    {
        try
        {
            return provider.LoadPackageObject<T>(path);
        }
        catch (Exception exception)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {path} did not load: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private static byte[] Bytes<T>(T[]? values) where T : unmanaged =>
        values is null || values.Length == 0 ? [] : System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

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
