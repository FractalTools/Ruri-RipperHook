using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;

namespace Ruri.RipperHook.EndField_0_8_25;

public partial class EndField_0_8_25_Hook : RipperHook
{
    public static UnityVersion EndFieldClassVersion = UnityVersion.Parse("2021.3.825x5" + (int)CustomEngineType.EndField);
    public const string ClassHookVersion = "2021.3.34f5";

    protected static LZ4_EndField_0_5 customLZ4;

    // 需要 Hook 的类列表
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
        ClassIDType.Mesh,
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
        customLZ4 = new LZ4_EndField_0_5();
        RuriRuntimeHook.CurrentDecryptor = new FairGuardDecryptor_EndField_0_5();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);
        AddExtraHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = Ruri.RipperHook.EndField_0_5.EndField_0_5_Hook.CustomBlockCompression; });

        base.InitAttributeHook(); // 处理常规 Attribute

        // 注册通用 Class Hook
        // 参数: ID列表, 原始Unity版本, 目标Unity版本, Ruri生成库的命名空间
        HookClasses(ClassesHook, ClassHookVersion, EndFieldClassVersion, "Ruri.SourceGenerated");
    }
}