using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;
using Ruri.RipperHook.Conversion;
using System.Numerics;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// A material interface: the base material of its parent chain becomes the Unity Shader (named
/// by the material's own path, declaring every parameter the chain states), and every interface
/// -- base or instance -- becomes a Material carrying the resolved parameter set CUE4Parse
/// flattens across the chain: textures by parameter name, scalars, colours, static switches as
/// scalars, and the blend and shading modes the material declares.
/// </summary>
public sealed class MaterialConverter : IUnrealConverter
{
    public const string ShaderSlot = "shader";
    private const string RootClassName = "Material";
    public const string BlendModeName = "BlendMode";
    public const string ShadingModelName = "ShadingModel";
    public const string TwoSidedName = "TwoSided";

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
        CMaterialParams2 parameters = new();
        source.GetParams(parameters, EMaterialDepth.AllLayers);

        UMaterial? root = RootMaterial(source);
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

        MaterialInputs inputs = new() { Name = export.Name, Shader = shader };
        foreach ((string name, UUnrealMaterial texture) in parameters.Textures)
        {
            ITexture2D? unityTexture = texture is UTexture2D ? conversion.Table.Find<ITexture2D>(texture) : null;
            inputs.Textures.Add((name, unityTexture, Vector2.One, Vector2.Zero));
        }
        foreach ((string name, float value) in parameters.Scalars)
        {
            inputs.Floats.Add((name, value));
        }
        foreach ((string name, bool value) in parameters.Switches)
        {
            inputs.Floats.Add((name, value ? 1f : 0f));
        }
        foreach ((string name, FLinearColor color) in parameters.Colors)
        {
            inputs.Colors.Add((name, new Vector4(color.R, color.G, color.B, color.A)));
        }
        inputs.Floats.Add((BlendModeName, (float)parameters.BlendMode));
        inputs.Floats.Add((ShadingModelName, (float)parameters.ShadingModel));
        if (root is not null)
        {
            inputs.Floats.Add((TwoSidedName, root.TwoSided ? 1f : 0f));
        }
        MaterialBuilder.FillMaterial(material, inputs);
    }

    private static UMaterial? RootMaterial(UMaterialInterface source)
    {
        UUnrealMaterial? cursor = source;
        HashSet<UUnrealMaterial> seen = new(ReferenceEqualityComparer.Instance);
        while (cursor is not null && seen.Add(cursor))
        {
            if (cursor is UMaterial material)
            {
                return material;
            }
            cursor = cursor is UMaterialInstance instance ? instance.Parent : null;
        }
        return null;
    }

    private static List<ShaderProperty> Declarations(CMaterialParams2 parameters)
    {
        List<ShaderProperty> declarations = new();
        foreach (string name in parameters.Textures.Keys)
        {
            declarations.Add(new ShaderProperty(name, ShaderPropertyKind.Texture));
        }
        foreach (string name in parameters.Scalars.Keys)
        {
            declarations.Add(new ShaderProperty(name, ShaderPropertyKind.Float));
        }
        foreach (string name in parameters.Switches.Keys)
        {
            declarations.Add(new ShaderProperty(name, ShaderPropertyKind.Float));
        }
        foreach (string name in parameters.Colors.Keys)
        {
            declarations.Add(new ShaderProperty(name, ShaderPropertyKind.Color));
        }
        declarations.Add(new ShaderProperty(BlendModeName, ShaderPropertyKind.Float));
        declarations.Add(new ShaderProperty(ShadingModelName, ShaderPropertyKind.Float));
        declarations.Add(new ShaderProperty(TwoSidedName, ShaderPropertyKind.Float));
        return declarations;
    }
}
