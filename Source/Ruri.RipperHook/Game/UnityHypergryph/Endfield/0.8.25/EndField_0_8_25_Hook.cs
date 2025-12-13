using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectAssetBundles;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;

namespace Ruri.RipperHook.EndField_0_8_25;

public partial class EndField_0_8_25_Hook : RipperHook
{
    public static UnityVersion EndFieldClassVersion = UnityVersion.Parse($"2021.3.825x{(int)CustomEngineType.EndField}");
    public const string ClassHookVersion = "2021.3.34f1";
    protected static LZ4_EndField_0_8_25 customLZ4;

    private static readonly List<ClassIDType> ClassesHook = new()
    {
        ClassIDType.AnimationClip,
        ClassIDType.Animator,
        ClassIDType.AnimatorController,
        ClassIDType.AnimatorOverrideController,
        ClassIDType.AssetBundle,
        ClassIDType.AudioManager,
        ClassIDType.BillboardRenderer,
        ClassIDType.BoxCollider,
        ClassIDType.CanvasGroup,
        ClassIDType.CapsuleCollider,
        ClassIDType.CharacterController,
        ClassIDType.Cubemap,
        ClassIDType.CubemapArray,
        ClassIDType.CustomRenderTexture,
        ClassIDType.GraphicsSettings,
        ClassIDType.Light,
        ClassIDType.LineRenderer,
        ClassIDType.Material,
        // 需要自定义加载
        // ClassIDType.Mesh,
        ClassIDType.MeshCollider,
        ClassIDType.MeshRenderer,
        ClassIDType.NavMeshProjectSettings,
        ClassIDType.ParticleSystem,
        ClassIDType.ParticleSystemRenderer,
        ClassIDType.ProceduralMaterial,
        ClassIDType.QualitySettings,
        ClassIDType.ReflectionProbe,
        ClassIDType.RenderSettings,
        ClassIDType.RenderTexture,
        ClassIDType.Shader,
        ClassIDType.SkinnedMeshRenderer,
        ClassIDType.SparseTexture,
        ClassIDType.SphereCollider,
        ClassIDType.SpriteMask,
        ClassIDType.SpriteRenderer,
        ClassIDType.SpriteShapeRenderer,
        ClassIDType.TagManager,
        ClassIDType.Terrain,
        ClassIDType.TerrainCollider,
        ClassIDType.TerrainData,
        ClassIDType.TerrainLayer,
        ClassIDType.Texture2D,
        ClassIDType.Texture2DArray,
        ClassIDType.Texture3D,
        ClassIDType.TilemapRenderer,
        ClassIDType.TrailRenderer,
        ClassIDType.VFXRenderer
    };

    protected EndField_0_8_25_Hook()
    {
        customLZ4 = new LZ4_EndField_0_8_25();
        RuriRuntimeHook.CurrentDecryptor = new VFSDecryptor();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);

        // 1. Hook 文件预加载 (支持多包切分)
        AddExtraHook(typeof(GameBundleHook).Namespace, () => { GameBundleHook.CustomFilePreInitialize = CustomFilePreInitialize; });
        // 2. Hook 文件遍历 (识别后缀)
        AddExtraHook(typeof(PlatformGameStructureHook_CollectAssetBundles).Namespace, () => { PlatformGameStructureHook_CollectAssetBundles.CustomAssetBundlesCheck = CustomAssetBundlesCheck; });
        // 3. Hook 魔数检查 (识别 Header)
        AddExtraHook(typeof(PlatformGameStructureHook_IsBundleHeader).Namespace, () => { PlatformGameStructureHook_IsBundleHeader.CustomAssetBundlesCheckMagicNum = CustomAssetBundlesCheckMagicNum; });

        // 4. Hook Block 解密
        AddExtraHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = CustomBlockCompression; });

        base.InitAttributeHook();

        HookClasses(ClassesHook, ClassHookVersion, EndFieldClassVersion, "Ruri.SourceGenerated");
    }
}