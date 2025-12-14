using System.Collections.Generic;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;
using Ruri.RipperHook.HookUtils.GameBundleHook;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectAssetBundles;
using Ruri.RipperHook.HookUtils.PlatformGameStructureHook_IsBundleHeader;

namespace Ruri.RipperHook.EndField_0_5;

public partial class EndField_0_5_Hook : RipperHook
{
    public static UnityVersion EndFieldClassVersion = UnityVersion.Parse($"2021.3.527x{(int)CustomEngineType.EndField}");
    public const string ClassHookVersion = "2021.3.34f1";

    private static LZ4_EndField_0_5 customLZ4;
    private static FairGuardDecryptor_EndField_0_5 fairGuardDecryptor;

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
        ClassIDType.Mesh,
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

    protected EndField_0_5_Hook()
    {
        customLZ4 = new LZ4_EndField_0_5();
        fairGuardDecryptor = new FairGuardDecryptor_EndField_0_5();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);

        AddExtraHook(typeof(GameBundleHook).Namespace, () => { GameBundleHook.CustomFilePreInitialize = CustomFilePreInitialize; });
        AddExtraHook(typeof(PlatformGameStructureHook_CollectAssetBundles).Namespace, () => { PlatformGameStructureHook_CollectAssetBundles.CustomAssetBundlesCheck = CustomAssetBundlesCheck; });
        AddExtraHook(typeof(PlatformGameStructureHook_IsBundleHeader).Namespace, () => { PlatformGameStructureHook_IsBundleHeader.CustomAssetBundlesCheckMagicNum = CustomAssetBundlesCheckMagicNum; });

        AddExtraHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = CustomBlockCompression; });

        base.InitAttributeHook();

        HookClasses(ClassesHook, ClassHookVersion, EndFieldClassVersion, "Ruri.SourceGenerated");
    }
}