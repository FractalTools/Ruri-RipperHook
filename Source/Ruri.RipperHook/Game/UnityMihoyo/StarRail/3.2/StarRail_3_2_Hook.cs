using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;
using Ruri.RipperHook.StarRailCommon;
using Ruri.RipperHook.UnityMihoyo;

namespace Ruri.RipperHook.StarRail_3_2;

public partial class StarRail_3_2_Hook : RipperHook
{
    public static UnityVersion StarRailClassVersion = UnityVersion.Parse($"2019.4.320x{(int)CustomEngineType.StarRail}");
    public const string ClassHookVersion = "2019.4.34f1";

    private static readonly List<ClassIDType> ClassesHook = new()
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

    protected StarRail_3_2_Hook()
    {
        MihoyoCommon.Mr0kDecryptor = new Mr0kDecryptor(Mr0kKey.Mr0kExpansionKey, initVector: Mr0kKey.Mr0kInitVector, blockKey: Mr0kKey.Mr0kBlockKey);
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(StarRailCommon_Hook).Namespace);
        AddNameSpaceHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = MihoyoCommon.CustomBlockCompression; });
        AddNameSpaceHook(typeof(PlatformGameStructureHook_IsBundleHeader).Namespace, () => { PlatformGameStructureHook_IsBundleHeader.CustomAssetBundlesCheckMagicNum = StarRailCommon_Hook.CustomAssetBundlesCheckMagicNum; });
        AddNameSpaceHook(typeof(GameBundleHook).Namespace, () => { GameBundleHook.CustomFilePreInitialize = StarRailCommon_Hook.CustomFilePreInitialize; });
        base.InitAttributeHook();

        HookClasses(ClassesHook, ClassHookVersion, StarRailClassVersion, "Ruri.SourceGenerated");
    }
}