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
    private static VFSDecryptor vfsDecryptor;

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
        // Mesh 直接复用 0.5.27 的逻辑
        // ClassIDType.Mesh,
        ClassIDType.MeshCollider,
        ClassIDType.MeshRenderer,
        ClassIDType.NavMeshData_238,
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
        vfsDecryptor = new VFSDecryptor();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);

        AddMethodHook(typeof(EndField_0_5_27.EndField_0_5_27_Hook), nameof(EndField_0_5_27.EndField_0_5_27_Hook.Mesh_ReadRelease));

        AddNameSpaceHook(typeof(GameBundleHook).Namespace, () => { GameBundleHook.CustomFilePreInitialize = CustomFilePreInitialize; });
        AddNameSpaceHook(typeof(PlatformGameStructureHook_CollectAssetBundles).Namespace, () => { PlatformGameStructureHook_CollectAssetBundles.CustomAssetBundlesCheck = CustomAssetBundlesCheck; });
        AddNameSpaceHook(typeof(PlatformGameStructureHook_IsBundleHeader).Namespace, () => { PlatformGameStructureHook_IsBundleHeader.CustomAssetBundlesCheckMagicNum = CustomAssetBundlesCheckMagicNum; });

        AddNameSpaceHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = CustomBlockCompression; });

        base.InitAttributeHook();

        HookClasses(ClassesHook, ClassHookVersion, EndFieldClassVersion, "Ruri.SourceGenerated");
    }
}