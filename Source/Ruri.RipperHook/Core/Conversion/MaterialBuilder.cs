using AssetRipper.Assets.Generics;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Subclasses.ColorRGBAf;
using AssetRipper.SourceGenerated.Subclasses.SerializedProperty;
using AssetRipper.SourceGenerated.Subclasses.UnityTexEnv;
using System.Numerics;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// The ShaderLab property kinds a shader declares -- the numbers Unity serializes in
/// SerializedProperty.m_Type.
/// </summary>
public enum ShaderPropertyKind
{
    Color = 0,
    Vector = 1,
    Float = 2,
    Range = 3,
    Texture = 4,
}

public readonly record struct ShaderProperty(string Name, ShaderPropertyKind Kind);

/// <summary>
/// One material's inputs in Unity's vocabulary: which shader, and the named textures, scalars
/// and colours the shader reads. Names are whatever the source calls them; the consuming host
/// maps vocabularies, not this builder.
/// </summary>
public sealed class MaterialInputs
{
    public required string Name { get; init; }

    public required IShader Shader { get; init; }

    public List<(string Name, ITexture2D? Texture, Vector2 Scale, Vector2 Offset)> Textures { get; } = new();

    public List<(string Name, float Value)> Floats { get; } = new();

    public List<(string Name, Vector4 Color)> Colors { get; } = new();

    public List<string> Keywords { get; } = new();
}

/// <summary>
/// Shaders here carry only what a material needs to name them: the shader's name and its
/// property declarations (the dummy exporter writes exactly that). A host that rebuilds
/// shading from the material's own inputs never reads more.
/// </summary>
public static class MaterialBuilder
{
    public static IShader Shader(ConvertedPackage package, string name, string? originalPath, IReadOnlyList<ShaderProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(package);
        IShader shader = package.Create<IShader>(ClassIDType.Shader, name, originalPath);
        if (shader.Has_ParsedForm())
        {
            shader.ParsedForm!.Name_R = name;
            AccessListBase<ISerializedProperty> declared = shader.ParsedForm.PropInfo.Props;
            foreach (ShaderProperty property in properties)
            {
                ISerializedProperty entry = declared.AddNew();
                entry.Name_R = property.Name;
                entry.Description = property.Name;
                entry.Type = (int)property.Kind;
                if (property.Kind == ShaderPropertyKind.Texture)
                {
                    entry.DefTexture.DefaultName = "white";
                }
                if (property.Kind == ShaderPropertyKind.Color)
                {
                    entry.DefValue_0_ = 1f;
                    entry.DefValue_1_ = 1f;
                    entry.DefValue_2_ = 1f;
                    entry.DefValue_3_ = 1f;
                }
            }
        }
        return shader;
    }

    public static IMaterial Material(ConvertedPackage package, MaterialInputs inputs, string? originalPath)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(inputs);
        IMaterial material = package.Create<IMaterial>(ClassIDType.Material, inputs.Name, originalPath);
        material.Shader_C21P = inputs.Shader;

        var texEnvs = material.SavedProperties_C21.TexEnvs_AssetDictionary_Utf8String_UnityTexEnv_5
            ?? throw new InvalidOperationException($"[MaterialBuilder] Material at {package.Space.Version} keeps no string-keyed texture table.");
        foreach ((string name, ITexture2D? texture, Vector2 scale, Vector2 offset) in inputs.Textures)
        {
            AccessPairBase<Utf8String, UnityTexEnv_5> pair = texEnvs.AddNew();
            pair.Key = name;
            pair.Value.Texture.SetAsset(material.Collection, texture);
            pair.Value.Scale.SetValues(scale.X, scale.Y);
            pair.Value.Offset.SetValues(offset.X, offset.Y);
        }

        var floats = material.SavedProperties_C21.Floats_AssetDictionary_Utf8String_Single
            ?? throw new InvalidOperationException($"[MaterialBuilder] Material at {package.Space.Version} keeps no string-keyed float table.");
        foreach ((string name, float value) in inputs.Floats)
        {
            floats.Add(name, value);
        }

        var colors = material.SavedProperties_C21.Colors_AssetDictionary_Utf8String_ColorRGBAf
            ?? throw new InvalidOperationException($"[MaterialBuilder] Material at {package.Space.Version} keeps no string-keyed colour table.");
        foreach ((string name, Vector4 color) in inputs.Colors)
        {
            AccessPairBase<Utf8String, ColorRGBAf> pair = colors.AddNew();
            pair.Key = name;
            pair.Value.SetValues(color.X, color.Y, color.Z, color.W);
        }

        if (inputs.Keywords.Count > 0 && material.Has_ShaderKeywords_C21_AssetList_Utf8String())
        {
            foreach (string keyword in inputs.Keywords)
            {
                material.ShaderKeywords_C21_AssetList_Utf8String!.Add(keyword);
            }
        }
        return material;
    }
}
