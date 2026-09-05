namespace Ruri.FModelHook.ShaderDecompiler.Semantics;

/// <summary>
/// What one texture slot of a material feeds, channel by channel: which of its channels reach
/// the base colour, the normal, the metallic, specular, roughness and occlusion components of
/// the GBuffer, which reach scene colour alone (emissive), and which decide the opacity mask.
/// Each part lists every channel the shader lets reach it; a consumer that needs one channel
/// takes the part only when exactly one does. The slot is named the way the material names it:
/// its parameter, or the referenced texture it samples as a constant.
/// </summary>
public sealed record MaterialSlotSemantics(
    int Group,
    int Index,
    int TextureIndex,
    string ParameterName,
    IReadOnlySet<int> BaseColorChannels,
    IReadOnlySet<int> NormalChannels,
    IReadOnlySet<int> MetallicChannels,
    IReadOnlySet<int> SpecularChannels,
    IReadOnlySet<int> RoughnessChannels,
    IReadOnlySet<int> OcclusionChannels,
    IReadOnlySet<int> EmissiveChannels,
    IReadOnlySet<int> OpacityMaskChannels)
{
    public bool IsBaseColor => BaseColorChannels.Count > 0;

    public bool IsNormal => NormalChannels.Count > 0;

    public bool IsPacked => MetallicChannels.Count > 0 || RoughnessChannels.Count > 0 || OcclusionChannels.Count > 0 || SpecularChannels.Count > 0;

    public bool IsEmissive => EmissiveChannels.Count > 0;

    public bool PlaysAPart => IsBaseColor || IsNormal || IsPacked || IsEmissive || OpacityMaskChannels.Count > 0;

    /// <summary>The one channel a part is read from, or null when no channel or several reach it.</summary>
    public static int? Single(IReadOnlySet<int> channels) => channels.Count == 1 ? channels.First() : null;
}

/// <summary>The resolved parts of every texture slot a material's compiled base pass samples, or the reason none could be read.</summary>
public sealed record MaterialSemantics(string ShaderMapHash, string Status, IReadOnlyList<MaterialSlotSemantics> Slots)
{
    public const string Resolved = "resolved";

    public bool IsResolved => string.Equals(Status, Resolved, StringComparison.Ordinal);

    public static MaterialSemantics Unresolved(string shaderMapHash, string status) => new(shaderMapHash, status, Array.Empty<MaterialSlotSemantics>());
}
