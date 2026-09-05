using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Material.Parameters;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Ruri.RipperHook.Conversion;
using System.Numerics;

namespace Ruri.FModelHook.UnityConverter.Converters;

/// <summary>
/// The parameter set a material interface resolves to, read the way the engine resolves it:
/// the base material's cached parameter tables state every parameter and its default, each
/// instance down the parent chain overrides by name, and an instance's base-property
/// overrides apply only where it flags them. Textures the graph samples without a parameter
/// are constants of the shader: a material with no texture parameter at all exposes its one
/// colour constant and its one normal-map constant under Unity's base-colour and normal-map
/// property names -- the kind is the texture's own declaration, colour space and compression
/// -- and every other constant under the texture's own name. Parameter kinds follow the
/// engine's declared order for its version; connected material inputs are named by the
/// game's own reflection enum.
/// </summary>
internal sealed class UnrealMaterialParameters
{
    private const string ParametersName = "Parameters";
    private const string RuntimeEntriesName = "RuntimeEntries";
    private const string ParameterInfoSetName = "ParameterInfoSet";
    private const string ParameterInfoName = "ParameterInfo";
    private const string ParameterValueName = "ParameterValue";
    private const string ConnectedMaskName = "PropertyConnectedMask";
    private const string ReferencedTexturesName = "ReferencedTextures";
    private const string MainTextureName = "_MainTex";
    private const string NormalMapName = "_BumpMap";
    private const string MaterialPropertyEnumName = "EMaterialProperty";
    private const string OverridePrefix = "bOverride_";
    private const string EnumScope = "::";
    private const string ValuesSuffix = "Values";

    /// <summary>
    /// The runtime parameter kinds in the order the engine indexes its cached entries by.
    /// EMaterialParameterType is a plain C++ enum with no reflection, so the order is stated per
    /// engine version from MaterialTypes.h and verified against every material's own tables --
    /// the entry count and each kind's value count -- before a value is read.
    /// </summary>
    private static readonly IReadOnlyDictionary<EGame, string[]> KindLayouts = new Dictionary<EGame, string[]>
    {
        [EGame.GAME_UE5_5] = ["Scalar", "Vector", "DoubleVector", "Texture", "TextureCollection", "Font", "RuntimeVirtualTexture", "SparseVolumeTexture", "StaticSwitch"],
    };

    private readonly UnrealConversion conversion;
    private readonly TypeMappings? mappings;
    private readonly OrderedDictionary<string, ITexture2D?> textures = new(StringComparer.Ordinal);
    private readonly OrderedDictionary<string, float> floats = new(StringComparer.Ordinal);
    private readonly OrderedDictionary<string, Vector4> colors = new(StringComparer.Ordinal);
    private readonly List<string> keywords = new();
    private readonly HashSet<string> parameterDefaults = new(StringComparer.Ordinal);

    public UnrealMaterialParameters(UnrealConversion conversion)
    {
        this.conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
        mappings = conversion.Shared.Provider.MappingsForGame;
    }

    public IEnumerable<string> TextureNames => textures.Keys;

    public IEnumerable<string> FloatNames => floats.Keys;

    public IEnumerable<string> ColorNames => colors.Keys;

    public void Float(string name, float value) => floats[name] = value;

    /// <summary>The base material: its own surface settings, every cached parameter with its default, and the inputs its graph connects.</summary>
    public void ReadRoot(UMaterial root)
    {
        floats[MaterialConverter.BlendModeName] = (float)root.BlendMode;
        floats[MaterialConverter.ShadingModelName] = (float)root.ShadingModel;
        floats[MaterialConverter.TwoSidedName] = root.TwoSided ? 1f : 0f;
        floats[MaterialConverter.OpacityMaskClipValueName] = root.OpacityMaskClipValue;
        if (root.CachedExpressionData is not { } cached)
        {
            return;
        }
        ReadCached(cached.GetOrDefault<FStructFallback>(ParametersName) ?? cached, root.GetPathName());
        Constants(cached.GetOrDefault<FPackageIndex[]>(ReferencedTexturesName, []));
        ulong connected = cached.GetOrDefault<ulong>(ConnectedMaskName);
        if (connected != 0 && mappings?.Enums.GetValueOrDefault(MaterialPropertyEnumName) is { } properties)
        {
            for (int bit = 0; bit < 64; bit++)
            {
                if ((connected & (1UL << bit)) != 0 && properties.TryGetValue(bit, out string? property))
                {
                    keywords.Add(property);
                }
            }
        }
    }

    /// <summary>An instance: the parameter values it overrides by name, its static switches, and the base properties it flags as overridden.</summary>
    public void ReadInstance(UMaterialInstance instance)
    {
        foreach (FTextureParameterValue value in instance.GetOrDefault<FTextureParameterValue[]>("TextureParameterValues", []))
        {
            textures[value.Name] = conversion.Table.Find<ITexture2D>(value.ParameterValue);
        }
        foreach (FScalarParameterValue value in instance.GetOrDefault<FScalarParameterValue[]>("ScalarParameterValues", []))
        {
            floats[value.Name] = value.ParameterValue;
        }
        foreach (FVectorParameterValue value in instance.GetOrDefault<FVectorParameterValue[]>("VectorParameterValues", []))
        {
            if (value.ParameterValue is { } color)
            {
                colors[value.Name] = new Vector4(color.R, color.G, color.B, color.A);
            }
        }
        foreach (FStructFallback value in instance.GetOrDefault<FStructFallback[]>("DoubleVectorParameterValues", []))
        {
            if (value.GetOrDefault<FMaterialParameterInfo>(ParameterInfoName) is { } info)
            {
                colors[info.Name.Text] = Vector(value.GetOrDefault<TIntVector4<double>>(ParameterValueName));
            }
        }
        if (instance.StaticParameters is { } statics)
        {
            foreach (FStaticSwitchParameter parameter in statics.StaticSwitchParameters)
            {
                floats[parameter.Name] = parameter.Value ? 1f : 0f;
            }
        }
        if (instance.GetOrDefault<FStructFallback>("BasePropertyOverrides") is { } overrides)
        {
            ReadOverrides(overrides);
        }
    }

    public MaterialInputs Inputs(string name, IShader shader)
    {
        MaterialInputs inputs = new() { Name = name, Shader = shader };
        foreach ((string textureName, ITexture2D? texture) in textures)
        {
            inputs.Textures.Add((textureName, texture, Vector2.One, Vector2.Zero));
        }
        foreach ((string floatName, float value) in floats)
        {
            inputs.Floats.Add((floatName, value));
        }
        foreach ((string colorName, Vector4 color) in colors)
        {
            inputs.Colors.Add((colorName, color));
        }
        inputs.Keywords.AddRange(keywords);
        return inputs;
    }

    /// <summary>
    /// The cached parameter tables -- on the cached data itself, or under its Parameters member
    /// in the engines that nest them: one entry per runtime parameter kind, in the engine's
    /// declared order, each listing its parameters beside a value table.
    /// </summary>
    private void ReadCached(FStructFallback parameters, string owner)
    {
        EGame game = conversion.Shared.Provider.Versions.Game;
        if (!KindLayouts.TryGetValue(game, out string[]? layout))
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {owner}: no cached-parameter kind layout is declared for {game}; parameter defaults not read.");
            return;
        }
        FMaterialParameterInfo[]?[] entries = new FMaterialParameterInfo[]?[layout.Length];
        int count = 0;
        foreach (FPropertyTag tag in parameters.Properties)
        {
            if (!string.Equals(tag.Name.Text, RuntimeEntriesName, StringComparison.Ordinal)
                || tag.Tag?.GenericValue is not FScriptStruct { StructType: FStructFallback entry })
            {
                continue;
            }
            count++;
            if (tag.ArrayIndex < layout.Length)
            {
                entries[tag.ArrayIndex] = entry.GetOrDefault<FMaterialParameterInfo[]>(ParameterInfoSetName, []);
            }
        }
        if (count != layout.Length)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {owner}: {count} cached parameter entries beside the {layout.Length} kinds declared for {game}; parameter defaults not read.");
            return;
        }
        for (int kindIndex = 0; kindIndex < layout.Length; kindIndex++)
        {
            string kind = layout[kindIndex];
            FMaterialParameterInfo[] infos = entries[kindIndex] ?? [];
            string table = kind + ValuesSuffix;
            switch (kind)
            {
                case "Scalar":
                    Read(owner, kind, infos, parameters.GetOrDefault<float[]>(table, []), (name, value) => floats[name] = value);
                    break;
                case "Vector":
                    Read(owner, kind, infos, parameters.GetOrDefault<FLinearColor[]>(table, []), (name, value) => colors[name] = new Vector4(value.R, value.G, value.B, value.A));
                    break;
                case "DoubleVector":
                    Read(owner, kind, infos, parameters.GetOrDefault<TIntVector4<double>[]>(table, []), (name, value) => colors[name] = Vector(value));
                    break;
                case "Texture":
                    Read(owner, kind, infos, parameters.GetOrDefault<FSoftObjectPath[]>(table, []), (name, value) =>
                    {
                        parameterDefaults.Add(value.AssetPathName.Text);
                        textures[name] = conversion.Table.Find<ITexture2D>(value.AssetPathName.Text);
                    });
                    break;
                case "StaticSwitch":
                    Read(owner, kind, infos, parameters.GetOrDefault<bool[]>(table, []), (name, value) => floats[name] = value ? 1f : 0f);
                    break;
            }
        }
    }

    /// <summary>One kind's parameters beside its value table; a count mismatch means the layout is not this cook's, and nothing is read.</summary>
    private static void Read<T>(string owner, string kind, FMaterialParameterInfo[] infos, T[] values, Action<string, T> assign)
    {
        if (infos.Length != values.Length)
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {owner}: {infos.Length} {kind} parameter(s) beside {values.Length} value(s); this kind's defaults not read.");
            return;
        }
        for (int i = 0; i < infos.Length; i++)
        {
            assign(infos[i].Name.Text, values[i]);
        }
    }

    /// <summary>
    /// The graph's constant textures -- referenced, yet the default of no parameter -- grouped by
    /// the kind each texture declares for itself.
    /// </summary>
    private void Constants(FPackageIndex[] referenced)
    {
        List<UTexture> colors = new();
        List<UTexture> normals = new();
        List<UTexture> others = new();
        foreach (FPackageIndex pointer in referenced)
        {
            if (pointer.Load() is not UTexture texture || parameterDefaults.Contains(texture.GetPathName()))
            {
                continue;
            }
            (texture.IsNormalMap ? normals : texture.SRGB ? colors : others).Add(texture);
        }
        bool parameterized = textures.Count > 0;
        Constant(colors, parameterized ? null : MainTextureName);
        Constant(normals, parameterized ? null : NormalMapName);
        Constant(others, null);
    }

    private void Constant(List<UTexture> group, string? role)
    {
        if (role is not null && group.Count == 1)
        {
            textures[role] = conversion.Table.Find<ITexture2D>(group[0]);
            return;
        }
        foreach (UTexture texture in group)
        {
            textures[texture.Name] = conversion.Table.Find<ITexture2D>(texture);
        }
    }

    /// <summary>Every flagged override, its value read by its own property's kind: a switch, a number, or an enum by the game's enum order.</summary>
    private void ReadOverrides(FStructFallback overrides)
    {
        foreach (FPropertyTag flag in overrides.Properties)
        {
            string flagName = flag.Name.Text;
            if (!flagName.StartsWith(OverridePrefix, StringComparison.Ordinal) || flag.Tag?.GenericValue is not true)
            {
                continue;
            }
            string name = flagName[OverridePrefix.Length..];
            FPropertyTag? value = overrides.Properties.Find(tag => string.Equals(tag.Name.Text, name, StringComparison.Ordinal));
            if (value is not null && Scalar(value) is { } scalar)
            {
                floats[name] = scalar;
            }
        }
    }

    private float? Scalar(FPropertyTag tag) => tag.Tag?.GenericValue switch
    {
        bool value => value ? 1f : 0f,
        float value => value,
        double value => (float)value,
        byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToSingle(tag.Tag.GenericValue),
        FName value => Ordinal(tag.TagData?.EnumName, value.Text),
        _ => null,
    };

    /// <summary>An enum entry's ordinal in the game's reflection, from its qualified or bare name.</summary>
    private float? Ordinal(string? enumName, string entry)
    {
        if (enumName is null || mappings?.Enums.GetValueOrDefault(enumName) is not { } entries)
        {
            return null;
        }
        string bare = Bare(entry);
        foreach ((long ordinal, string name) in entries)
        {
            if (string.Equals(Bare(name), bare, StringComparison.Ordinal))
            {
                return ordinal;
            }
        }
        return null;
    }

    /// <summary>An enum entry without its enum-class scope: the identifier the engine declares.</summary>
    private static string Bare(string entry)
    {
        int scope = entry.IndexOf(EnumScope, StringComparison.Ordinal);
        return scope >= 0 ? entry[(scope + EnumScope.Length)..] : entry;
    }

    private static Vector4 Vector(TIntVector4<double> value) => new((float)value.X, (float)value.Y, (float)value.Z, (float)value.W);
}
