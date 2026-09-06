using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using AssetRipper.Import.Logging;
using CUE4Parse.UE4.Assets.Exports.Material;
using Ruri.FModelHook.ShaderDecompiler.Semantics;
using Ruri.RipperHook.Conversion;

namespace Ruri.FModelHook.Unreal.Converters;

/// <summary>
/// A material interface: the base material of its parent chain becomes the Unity Shader (named
/// by the material's own path, declaring every parameter the chain states), and every interface
/// -- base or instance -- becomes a Material carrying the parameter set the engine resolves for
/// it: the base material's cached parameters and defaults, overridden by name down the parent
/// chain, with the surface settings the base states and an instance overrides only where it
/// flags them.
/// </summary>
public sealed class MaterialConverter : IUnrealConverter
{
    public const string ShaderSlot = "shader";
    private const string RootClassName = "Material";
    public const string BlendModeName = "BlendMode";
    public const string ShadingModelName = "ShadingModel";
    public const string TwoSidedName = "TwoSided";
    public const string OpacityMaskClipValueName = "OpacityMaskClipValue";

    public IReadOnlyList<string> ClassNames { get; } =
        ["MaterialInterface", "Material", "MaterialInstance", "MaterialInstanceConstant", "MaterialInstanceDynamic", "LandscapeMaterialInstanceConstant"];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.Material, ClassIDType.Shader];

    public void Allocate(UnrealConversion conversion, ResolvedObject header)
    {
        if (conversion.IsA(header, RootClassName))
        {
            IShader shader = conversion.Package.Create<IShader>(ClassIDType.Shader, header.Name.Text, conversion.UnityPath(header));
            conversion.Register(header, shader, ShaderSlot);
        }
        IMaterial material = conversion.Package.Create<IMaterial>(ClassIDType.Material, header.Name.Text, conversion.UnityPath(header));
        conversion.Register(header, material);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UMaterialInterface source || conversion.Table.Find<IMaterial>(export) is not { } material)
        {
            return;
        }
        List<UMaterialInterface> chain = Chain(source);
        UnrealMaterialParameters parameters = Resolve(conversion.Shared.Provider, conversion.Shared.Semantics, source, chain);
        UMaterial? root = chain[0] as UMaterial;
        IShader? shader = root is null ? null : conversion.Table.Find<IShader>(root, ShaderSlot);
        if (shader is null)
        {
            string shaderName = root?.GetPathName() ?? source.GetPathName();
            shader = conversion.Package.Create<IShader>(ClassIDType.Shader, shaderName, null);
            MaterialBuilder.FillShader(shader, shaderName, Declarations(parameters));
        }
        else if (ReferenceEquals(root, source))
        {
            MaterialBuilder.FillShader(shader, source.GetPathName(), Declarations(parameters));
        }
        MaterialBuilder.FillMaterial(material, parameters.Inputs(export.Name, shader, conversion.Table));
    }

    /// <summary>
    /// The parameter set a material interface resolves to, the way the engine resolves it: the
    /// base material's cached parameters and defaults, each instance down the chain overriding by
    /// name, then what the compiled base pass proves about the slots. This is the ONE reading of
    /// a material -- the Unity conversion runs it, and so does a host asking for the parameters
    /// alone -- so neither can drift from the other.
    /// </summary>
    internal static UnrealMaterialParameters Resolve(UnrealFileProvider provider, MaterialSemanticsResolver? resolver,
        UMaterialInterface source, List<UMaterialInterface>? chain = null)
    {
        UnrealMaterialParameters parameters = new(provider);
        foreach (UMaterialInterface layer in chain ?? Chain(source))
        {
            switch (layer)
            {
                case UMaterial baseMaterial:
                    parameters.ReadRoot(baseMaterial);
                    break;
                case UMaterialInstance instance:
                    parameters.ReadInstance(instance);
                    break;
            }
        }
        if (resolver is not null && resolver.Resolve(source) is { } semantics)
        {
            if (semantics.IsResolved)
            {
                parameters.Apply(semantics);
            }
            else
            {
                Logger.Verbose(LogCategory.Import, $"[Unreal] {source.GetPathName()}: material semantics not read: {semantics.Status}");
            }
        }
        return parameters;
    }

    /// <summary>The parent chain from the base material down to <paramref name="source"/>.</summary>
    internal static List<UMaterialInterface> Chain(UMaterialInterface source)
    {
        List<UMaterialInterface> chain = new();
        HashSet<UUnrealMaterial> seen = new(ReferenceEqualityComparer.Instance);
        UUnrealMaterial? cursor = source;
        while (cursor is UMaterialInterface layer && seen.Add(layer))
        {
            chain.Add(layer);
            cursor = layer is UMaterialInstance instance ? instance.Parent : null;
        }
        chain.Reverse();
        return chain;
    }

    private static List<ShaderProperty> Declarations(UnrealMaterialParameters parameters)
    {
        List<ShaderProperty> declarations = new();
        foreach (string name in parameters.TextureNames)
        {
            declarations.Add(new ShaderProperty(name, ShaderPropertyKind.Texture));
        }
        foreach (string name in parameters.FloatNames)
        {
            declarations.Add(new ShaderProperty(name, ShaderPropertyKind.Float));
        }
        foreach (string name in parameters.ColorNames)
        {
            declarations.Add(new ShaderProperty(name, ShaderPropertyKind.Color));
        }
        return declarations;
    }
}
