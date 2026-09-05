using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Shaders;
using Newtonsoft.Json;
using Ruri.ShaderTools;
using Ruri.ShaderTools.Pipeline.Frontend;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Ruri.FModelHook.ShaderDecompiler.Semantics;

/// <summary>
/// The parts each texture slot of a material plays, read off the material's own compiled base
/// pass: the inline shader map names the pass's pixel shader, the shipped shader library holds
/// its bytecode, the decompiler's front end turns that into SPIR-V, and a per-channel taint
/// from the pass's GBuffer outputs back to the sampled textures says which slot channel feeds
/// base colour, normal, metallic, specular, roughness, occlusion, emissive (scene colour and
/// nothing the GBuffer holds) or the opacity mask (what decides a discard). The material's
/// uniform-buffer layout names each binding as the slot it is; a slot that is no parameter
/// carries the null name and is known by the referenced texture it samples. The
/// GBuffer targets are the deferred base pass's: scene colour, then normal (octahedral in the
/// first two channels), then metallic, specular, roughness, then base colour with occlusion in
/// alpha -- BasePassPixelShader.usf and DeferredShadingCommon.ush state that order. Results
/// are kept per shader map, which an instance without a static permutation of its own shares
/// with its parent.
/// </summary>
public sealed class MaterialSemanticsResolver : IDisposable
{
    private const string BasePassPixelShaderPrefix = "TBasePassPS";
    private const string MaterialPrefix = "Material_";
    private const int SceneColorTarget = 0;
    private const int NormalTarget = 1;
    private const int SurfaceTarget = 2;
    private const int BaseColorTarget = 3;
    private const int MetallicChannel = 0;
    private const int SpecularChannel = 1;
    private const int RoughnessChannel = 2;
    private const int OcclusionChannel = 3;
    private const int ColorChannels = 3;
    private const int NormalChannels = 2;

    private readonly AbstractFileProvider provider;
    private readonly Action<string> log;
    private readonly Action<string> trace;
    private readonly Lazy<MaterialShaderLibraryIndex> libraries;
    private readonly ConcurrentDictionary<string, Lazy<MaterialSemantics>> byShaderMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> reportedStatuses = new(StringComparer.Ordinal);

    /// <param name="log">Where library loads and each distinct failure go.</param>
    /// <param name="trace">Where every shader map read is described binding by binding.</param>
    public MaterialSemanticsResolver(AbstractFileProvider provider, Action<string> log, Action<string> trace)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.trace = trace ?? throw new ArgumentNullException(nameof(trace));
        libraries = new Lazy<MaterialShaderLibraryIndex>(() => MaterialShaderLibraryIndex.Load(provider, log), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The semantics of the shader map this material renders with, or the reason there are none.</summary>
    public MaterialSemantics? Resolve(UMaterialInterface material)
    {
        (FMaterialShaderMap map, string materialPath)? owner = ShaderMapOf(material);
        if (owner is null)
        {
            return null;
        }
        (FMaterialShaderMap shaderMap, string materialPath) = owner.Value;
        trace(Resources(material));
        string hash = shaderMap.ResourceHash?.ToString() ?? string.Empty;
        if (hash.Length == 0)
        {
            return MaterialSemantics.Unresolved(hash, "the shader map carries neither inline code nor a library hash");
        }
        return byShaderMap.GetOrAdd(hash, _ => new Lazy<MaterialSemantics>(
            () => Report(Analyze(hash, shaderMap, materialPath), materialPath),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>Each distinct reason a map could not be read is said once, with the first material it was met on.</summary>
    private MaterialSemantics Report(MaterialSemantics semantics, string materialPath)
    {
        if (!semantics.IsResolved && reportedStatuses.TryAdd(semantics.Status, materialPath))
        {
            log($"[Unreal] Material semantics not read for '{materialPath}': {semantics.Status}.");
        }
        return semantics;
    }

    /// <summary>Every inline shader map of the material itself: feature level, quality level and library hash, in the order the asset carries them.</summary>
    private static string Resources(UMaterialInterface material)
    {
        StringBuilder text = new();
        text.Append("[Unreal] Material '").Append(material.GetPathName()).Append("' resources:");
        foreach (FMaterialResource resource in material.LoadedMaterialResources)
        {
            text.Append(' ');
            if (resource.LoadedShaderMap is { } map)
            {
                text.Append(map.ShaderMapId.FeatureLevel).Append('/').Append(map.ShaderMapId.QualityLevel).Append(':').Append(map.ResourceHash?.ToString() ?? "inline");
            }
            else
            {
                text.Append("no-map");
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// The shader map the material renders with at its fullest: of the first layer of the parent
    /// chain that carries inline maps, the one for the highest feature level, and of that the
    /// quality-independent map when there is one, else the map of the richest quality level.
    /// A material with quality switches ships one map per quality it distinguishes plus the
    /// quality-independent one serving the rest; the low ones drop textures for constants.
    /// </summary>
    private static (FMaterialShaderMap, string)? ShaderMapOf(UMaterialInterface material)
    {
        UUnrealMaterial? cursor = material;
        HashSet<UUnrealMaterial> seen = new(ReferenceEqualityComparer.Instance);
        while (cursor is UMaterialInterface layer && seen.Add(layer))
        {
            FMaterialShaderMap? best = null;
            foreach (FMaterialResource resource in layer.LoadedMaterialResources)
            {
                if (resource.LoadedShaderMap is { } map && (best is null || Outranks(map.ShaderMapId, best.ShaderMapId)))
                {
                    best = map;
                }
            }
            if (best is not null)
            {
                return (best, layer.GetPathName());
            }
            cursor = layer is UMaterialInstance instance ? instance.Parent : null;
        }
        return null;
    }

    private static bool Outranks(FMaterialShaderMapId candidate, FMaterialShaderMapId incumbent) =>
        candidate.FeatureLevel != incumbent.FeatureLevel
            ? candidate.FeatureLevel > incumbent.FeatureLevel
            : QualityRank(candidate.QualityLevel) > QualityRank(incumbent.QualityLevel);

    /// <summary>The engine's quality levels from poorest to richest, with the quality-independent map above them all; the enum itself is not in that order.</summary>
    private static int QualityRank(EMaterialQualityLevel quality) => quality switch
    {
        EMaterialQualityLevel.Num => 4,
        EMaterialQualityLevel.Epic => 3,
        EMaterialQualityLevel.High => 2,
        EMaterialQualityLevel.Medium => 1,
        _ => 0,
    };

    private MaterialSemantics Analyze(string hash, FMaterialShaderMap shaderMap, string materialPath)
    {
        if (!libraries.Value.TryFind(hash, out MaterialShaderLibraryIndex.Entry entry))
        {
            return MaterialSemantics.Unresolved(hash, "no shipped shader library holds this shader map");
        }
        if (shaderMap.Content is not FMaterialShaderMapContent content)
        {
            return MaterialSemantics.Unresolved(hash, "the shader map carries no material content");
        }
        FShader? pixelShader = BasePassPixelShader(content);
        if (pixelShader is null)
        {
            return MaterialSemantics.Unresolved(hash, "the shader map lists no base pass pixel shader");
        }
        int shaderIndex = MaterialShaderLibraryIndex.ShaderIndex(entry, pixelShader.ResourceIndex);
        if (shaderIndex < 0 || entry.Library.ShaderEntries[shaderIndex].Frequency != (byte)EShaderFrequency.SF_Pixel)
        {
            return MaterialSemantics.Unresolved(hash, $"resource {pixelShader.ResourceIndex} of the map is not a pixel shader in the library");
        }
        byte[]? raw = entry.Library.GetShaderCode(shaderIndex);
        if (raw is null || raw.Length == 0)
        {
            return MaterialSemantics.Unresolved(hash, $"shader {shaderIndex} has no code in the library");
        }
        byte[] stripped = UnrealShaderParser.Parse(raw, out ShaderBinaryFormat format, out UnrealShaderParser.UnrealMetadata? runtimeMetadata);
        (SerializedProgramData symbols, MaterialUniformBufferLayout? layout) = Symbols(materialPath, entry.Platform, content, runtimeMetadata);
        byte[]? spirv = SpirvFrontend.TryConvert(stripped, out string? error);
        if (spirv is null)
        {
            return MaterialSemantics.Unresolved(hash, $"the front end could not read the base pass: {error}");
        }
        SpirvTaint taint = SpirvTaint.Analyze(spirv);
        if (!taint.Outputs.ContainsKey(BaseColorTarget))
        {
            return MaterialSemantics.Unresolved(hash, "the base pass writes no base colour target (a Substrate or unlit layout)");
        }
        List<MaterialSlotSemantics> slots = Slots(content, symbols, layout, taint);
        string origin = $"{entry.Platform} {format} metadata {(runtimeMetadata is null ? "none" : "read")} {shaderMap.ShaderMapId.FeatureLevel}/{shaderMap.ShaderMapId.QualityLevel} shader {shaderIndex}";
        trace(Describe(materialPath, hash, origin, symbols, taint, slots));
        return new MaterialSemantics(hash, MaterialSemantics.Resolved, slots);
    }

    /// <summary>One line per shader map read: where the shader came from, the targets the base pass writes with how many bindings feed each, every binding the pass samples, every texture binding as the layout names it, and each slot's parts.</summary>
    private static string Describe(string materialPath, string hash, string origin, SerializedProgramData symbols, SpirvTaint taint, List<MaterialSlotSemantics> slots)
    {
        StringBuilder text = new();
        text.Append("[Unreal] Material semantics of '").Append(materialPath).Append("' (map ").Append(hash).Append(", ").Append(origin).Append("): sampled ");
        text.AppendJoin(',', taint.Outputs.Values.SelectMany(static components => components).SelectMany(static sources => sources).Select(static source => source.Binding)
            .Concat(taint.Discard.Select(static source => source.Binding)).Distinct().OrderBy(static binding => binding));
        text.Append("; targets");
        foreach (int target in taint.Outputs.Keys.OrderBy(static key => key))
        {
            text.Append(' ').Append(target).Append('(').Append(taint.Outputs[target].SelectMany(static sources => sources).Select(static source => source.Binding).Distinct().Count()).Append(')');
        }
        text.Append("; discard ").Append(taint.Discard.Count).Append("; bindings");
        foreach (TextureParameter texture in symbols.TextureParameters)
        {
            text.Append(' ').Append(texture.Name).Append('#').Append(texture.Index).Append("@set").Append(symbols.GetSetIdFor(texture.Index, ShaderResourceType.Texture));
        }
        text.Append("; slots");
        foreach (MaterialSlotSemantics slot in slots)
        {
            text.Append(" [").Append(slot.Group).Append('/').Append(slot.Index).Append(' ')
                .Append(slot.ParameterName.Length > 0 ? slot.ParameterName : "texture " + slot.TextureIndex)
                .Append(" base:").AppendJoin(',', slot.BaseColorChannels)
                .Append(" normal:").AppendJoin(',', slot.NormalChannels)
                .Append(" metallic:").AppendJoin(',', slot.MetallicChannels)
                .Append(" specular:").AppendJoin(',', slot.SpecularChannels)
                .Append(" roughness:").AppendJoin(',', slot.RoughnessChannels)
                .Append(" occlusion:").AppendJoin(',', slot.OcclusionChannels)
                .Append(" emissive:").AppendJoin(',', slot.EmissiveChannels)
                .Append(" mask:").AppendJoin(',', slot.OpacityMaskChannels)
                .Append(']');
        }
        text.Append("; reach");
        foreach (TextureParameter texture in symbols.TextureParameters)
        {
            text.Append(' ').Append(texture.Name).Append(':');
            foreach (int target in taint.Outputs.Keys.OrderBy(static key => key))
            {
                HashSet<SpirvTaint.Source>[] components = taint.Outputs[target];
                for (int component = 0; component < components.Length; component++)
                {
                    HashSet<int> channels = Reaching(taint, texture.Index, target, component, 1);
                    if (channels.Count > 0)
                    {
                        text.Append('t').Append(target).Append('.').Append(component).Append('{').AppendJoin(',', channels.OrderBy(static channel => channel)).Append('}');
                    }
                }
            }
        }
        return text.ToString();
    }

    /// <summary>The first base pass pixel shader of any vertex factory: the material's surface is the same whichever mesh carries it.</summary>
    private static FShader? BasePassPixelShader(FMaterialShaderMapContent content)
    {
        foreach (FMeshMaterialShaderMap meshMap in content.OrderedMeshShaderMaps)
        {
            foreach (FShader shader in meshMap.Shaders)
            {
                if (shader.Target.Frequency != EShaderFrequency.SF_Pixel)
                {
                    continue;
                }
                string typeName = HashedNamesResolver.ResolveShaderTypeName(shader.Type.Hash.ToString("X16"));
                if (typeName.StartsWith(BasePassPixelShaderPrefix, StringComparison.Ordinal))
                {
                    return shader;
                }
            }
        }
        return null;
    }

    /// <summary>The shader's bindings named by the material's own uniform-buffer layout, the way the decompiler names them, with that layout for reading the names back.</summary>
    private (SerializedProgramData Symbols, MaterialUniformBufferLayout? Layout) Symbols(string materialPath, string platform, FMaterialShaderMapContent content, UnrealShaderParser.UnrealMetadata? runtimeMetadata)
    {
        MaterialSymbolSource? source = null;
        MaterialUniformBufferLayout? layout = null;
        FUniformExpressionSet? uniformExpressions = content.MaterialCompilationOutput?.UniformExpressionSet;
        if (uniformExpressions is not null)
        {
            using JsonDocument document = JsonDocument.Parse(JsonConvert.SerializeObject(uniformExpressions));
            SymbolInputs? inputs = SymbolInputsReader.ReadFromUniformExpressionSet(materialPath, platform, document.RootElement);
            if (inputs is not null)
            {
                layout = inputs.MaterialResourceCounts is { } counts ? new MaterialUniformBufferLayout(counts) : null;
                source = new MaterialSymbolSource(materialPath, MaterialSymbolMetadataBuilder.Build(inputs), 0, true, layout);
            }
        }
        return (SubProgramMetadataReader.Read(runtimeMetadata, source, null, log), layout);
    }

    /// <summary>A member's typed name: a binding the layout named after its parameter reads back through the layout, the rest are typed already.</summary>
    private static string TypedMember(MaterialUniformBufferLayout? layout, string member) =>
        layout is not null && layout.TryResolveAuthorName(member, out string typed) ? typed : member;

    private static List<MaterialSlotSemantics> Slots(FMaterialShaderMapContent content, SerializedProgramData symbols, MaterialUniformBufferLayout? layout, SpirvTaint taint)
    {
        FMaterialTextureParameterInfo[][] groups = content.MaterialCompilationOutput?.UniformExpressionSet?.UniformTextureParameters ?? [];
        List<MaterialSlotSemantics> slots = new();
        foreach (TextureParameter texture in symbols.TextureParameters)
        {
            if (symbols.GetSetIdFor(texture.Index, ShaderResourceType.Texture) != 0
                || texture.Name is not { } name
                || !name.StartsWith(MaterialPrefix, StringComparison.Ordinal)
                || !MaterialUniformBufferLayout.TryParseTextureSlot(TypedMember(layout, name[MaterialPrefix.Length..]), out int group, out int index)
                || group >= groups.Length
                || index >= groups[group].Length)
            {
                continue;
            }
            FMaterialTextureParameterInfo parameter = groups[group][index];
            slots.Add(Slot(group, index, parameter.TextureIndex, ParameterName(parameter), texture.Index, taint));
        }
        return slots;
    }

    /// <summary>A slot's parameter name; a texture the graph samples as a constant has the null name and is known by its referenced texture instead.</summary>
    private static string ParameterName(FMaterialTextureParameterInfo parameter)
    {
        if (parameter.ParameterInfo is { } info)
        {
            return info.Name.IsNone ? string.Empty : info.Name.Text ?? string.Empty;
        }
        return parameter.ParameterName ?? string.Empty;
    }

    /// <summary>
    /// Emissive is what reaches scene colour and no other target: everything a GBuffer target
    /// holds lights scene colour too. Occlusion is what reaches the base colour target's alpha
    /// and nothing else the GBuffer holds: that alpha also takes the specular colour's bounce,
    /// so base colour, metallic and roughness reach it as well, but each reaches its own
    /// component too, and the occlusion channel reaches nothing but that alpha and the lighting.
    /// </summary>
    private static MaterialSlotSemantics Slot(int group, int index, int textureIndex, string parameterName, int binding, SpirvTaint taint)
    {
        HashSet<int> emissive = Reaching(taint, binding, SceneColorTarget, 0, ColorChannels);
        emissive.ExceptWith(ReachingElsewhere(taint, binding, -1, -1));
        HashSet<int> occlusion = Reaching(taint, binding, BaseColorTarget, OcclusionChannel, 1);
        occlusion.ExceptWith(ReachingElsewhere(taint, binding, BaseColorTarget, OcclusionChannel));
        HashSet<int> opacity = new();
        foreach (SpirvTaint.Source source in taint.Discard)
        {
            if (source.Binding == binding)
            {
                opacity.Add(source.Channel);
            }
        }
        return new MaterialSlotSemantics(group, index, textureIndex, parameterName,
            Reaching(taint, binding, BaseColorTarget, 0, ColorChannels),
            Reaching(taint, binding, NormalTarget, 0, NormalChannels),
            Reaching(taint, binding, SurfaceTarget, MetallicChannel, 1),
            Reaching(taint, binding, SurfaceTarget, SpecularChannel, 1),
            Reaching(taint, binding, SurfaceTarget, RoughnessChannel, 1),
            occlusion, emissive, opacity);
    }

    /// <summary>The channels of a binding that reach any component of any target but scene colour, one component left out.</summary>
    private static HashSet<int> ReachingElsewhere(SpirvTaint taint, int binding, int exceptTarget, int exceptComponent)
    {
        HashSet<int> channels = new();
        foreach ((int target, HashSet<SpirvTaint.Source>[] components) in taint.Outputs)
        {
            if (target == SceneColorTarget)
            {
                continue;
            }
            for (int component = 0; component < components.Length; component++)
            {
                if (target != exceptTarget || component != exceptComponent)
                {
                    channels.UnionWith(Reaching(taint, binding, target, component, 1));
                }
            }
        }
        return channels;
    }

    /// <summary>The channels of one binding that reach any of the given components of one target.</summary>
    private static HashSet<int> Reaching(SpirvTaint taint, int binding, int target, int firstComponent, int componentCount)
    {
        HashSet<int> channels = new();
        if (!taint.Outputs.TryGetValue(target, out HashSet<SpirvTaint.Source>[]? components))
        {
            return channels;
        }
        for (int component = firstComponent; component < firstComponent + componentCount && component < components.Length; component++)
        {
            foreach (SpirvTaint.Source source in components[component])
            {
                if (source.Binding == binding)
                {
                    channels.Add(source.Channel);
                }
            }
        }
        return channels;
    }

    public void Dispose()
    {
        if (libraries.IsValueCreated)
        {
            libraries.Value.Dispose();
        }
    }
}
