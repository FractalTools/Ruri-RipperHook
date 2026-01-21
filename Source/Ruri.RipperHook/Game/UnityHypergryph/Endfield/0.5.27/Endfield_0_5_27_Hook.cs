using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectAssetBundles;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;

namespace Ruri.RipperHook.EndField_0_5_27;

public partial class EndField_0_5_27_Hook : RipperHook
{
    public static UnityVersion EndFieldClassVersion = UnityVersion.Parse($"2021.3.527x{(int)CustomEngineType.EndField}");
    public const string ClassHookVersion = "2021.3.34f1";

    private static EndField_0_5_27_FairGuardDecryptor fairGuardDecryptor;

    private static readonly List<ClassIDType> ClassesHook = new()
    {
        ClassIDType.AnimationClip,
        ClassIDType.Animator,
        ClassIDType.AnimatorController,
        ClassIDType.AssetBundle,
        ClassIDType.AudioManager,
        ClassIDType.BillboardRenderer,
        ClassIDType.BoxCollider,
        ClassIDType.CanvasGroup,
        ClassIDType.CapsuleCollider,
        ClassIDType.CharacterController,
        ClassIDType.GraphicsSettings,
        ClassIDType.Light,
        ClassIDType.LineRenderer,
        ClassIDType.Material,
        ClassIDType.MeshCollider,
        ClassIDType.MeshRenderer,
        ClassIDType.MonoScript,
        ClassIDType.NavMeshData_238,
        ClassIDType.ParticleSystem,
        ClassIDType.ParticleSystemRenderer,
        ClassIDType.ProceduralMaterial,
        ClassIDType.QualitySettings,
        ClassIDType.ReflectionProbe,
        ClassIDType.RenderSettings,
        ClassIDType.Shader,
        ClassIDType.SkinnedMeshRenderer,
        ClassIDType.SphereCollider,
        ClassIDType.SpriteMask,
        ClassIDType.SpriteRenderer,
        ClassIDType.SpriteShapeRenderer,
        ClassIDType.Terrain,
        ClassIDType.TerrainCollider,
        ClassIDType.TerrainData,
        ClassIDType.TerrainLayer,
        ClassIDType.TilemapRenderer,
        ClassIDType.TrailRenderer,
        ClassIDType.VFXRenderer
    };

    protected EndField_0_5_27_Hook()
    {
        fairGuardDecryptor = new EndField_0_5_27_FairGuardDecryptor();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);

        AddNameSpaceHook(typeof(GameBundleHook).Namespace, () => { GameBundleHook.CustomFilePreInitialize = CustomFilePreInitialize; });
        AddNameSpaceHook(typeof(PlatformGameStructureHook_CollectAssetBundles).Namespace, () => { PlatformGameStructureHook_CollectAssetBundles.CustomAssetBundlesCheck = CustomAssetBundlesCheck; });
        AddNameSpaceHook(typeof(PlatformGameStructureHook_IsBundleHeader).Namespace, () => { PlatformGameStructureHook_IsBundleHeader.CustomAssetBundlesCheckMagicNum = CustomAssetBundlesCheckMagicNum; });

        AddNameSpaceHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = CustomBlockCompression; });

        base.InitAttributeHook();

        HookClasses(ClassesHook, ClassHookVersion, EndFieldClassVersion, "Ruri.SourceGenerated");
    }
}