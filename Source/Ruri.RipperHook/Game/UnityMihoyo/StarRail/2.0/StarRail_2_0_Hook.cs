using Ruri.RipperHook.Attributes;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;
using Ruri.RipperHook.StarRail;
using Ruri.RipperHook.UnityMihoyo;

namespace Ruri.RipperHook.StarRail;

[GameHook("StarRail", "2.0", "2019.4.34f1")]
public partial class StarRail_2_0_Hook : StarRailCommon_Hook
{
    public static UnityVersion StarRailClassVersion = UnityVersion.Parse($"2019.4.200x{(int)CustomEngineType.StarRail}");
    public const string ClassHookVersion = "2019.4.34f1";

    private static readonly List<ClassIDType> ClassesToHook = new()
    {
        ClassIDType.AnimationClip,
        ClassIDType.Animator,
        ClassIDType.AudioManager,
        ClassIDType.BillboardRenderer,
        ClassIDType.Camera,
        ClassIDType.GraphicsSettings,
        ClassIDType.Light,
        ClassIDType.LineRenderer,
        ClassIDType.MeshRenderer,
        ClassIDType.MonoScript,
        ClassIDType.ParticleSystem,
        ClassIDType.ParticleSystemRenderer,
        ClassIDType.QualitySettings,
        ClassIDType.ReflectionProbe,
        ClassIDType.SkinnedMeshRenderer,
        ClassIDType.SpriteMask,
        ClassIDType.SpriteRenderer,
        ClassIDType.SpriteShapeRenderer,
        ClassIDType.TilemapRenderer,
        ClassIDType.TrailRenderer,
        ClassIDType.VFXRenderer
    };

    protected StarRail_2_0_Hook()
    {
        MihoyoCommon.Mr0kDecryptor = new Mr0kDecryptor(Mr0kKey.Mr0kExpansionKey, initVector: Mr0kKey.Mr0kInitVector, blockKey: Mr0kKey.Mr0kBlockKey);
    }

    protected override void InitAttributeHook()
    {
RegisterModule(new BundleFileBlockReaderHook(MihoyoCommon.CustomBlockCompression));
        RegisterModule(new PlatformGameStructureHook_IsBundleHeader(StarRailCommon_Hook.CustomAssetBundlesCheckMagicNum));
        RegisterModule(new GameBundleHook(StarRailCommon_Hook.CustomFilePreInitialize));
        base.InitAttributeHook();

        HookClasses(ClassesToHook, ClassHookVersion, StarRailClassVersion, "Ruri.SourceGenerated");
    }
}