using AssetRipper.Assets;
using AssetRipper.IO.Endian;
using AssetRipper.SourceGenerated;
using Ruri.SourceGenerated.Classes.ClassID_108;
using Ruri.SourceGenerated.Classes.ClassID_120;
using Ruri.SourceGenerated.Classes.ClassID_136;
using Ruri.SourceGenerated.Classes.ClassID_143;
using Ruri.SourceGenerated.Classes.ClassID_188;
using Ruri.SourceGenerated.Classes.ClassID_227;
using Ruri.SourceGenerated.Classes.ClassID_23;
using Ruri.SourceGenerated.Classes.ClassID_238;
using Ruri.SourceGenerated.Classes.ClassID_30;
using Ruri.SourceGenerated.Classes.ClassID_64;
using Ruri.SourceGenerated.Classes.ClassID_65;
using Ruri.SourceGenerated.Classes.ClassID_86;
using Ruri.SourceGenerated.Classes.ClassID_89;
using Ruri.SourceGenerated.Classes.ClassID_104;
using Ruri.SourceGenerated.Classes.ClassID_11;
using Ruri.SourceGenerated.Classes.ClassID_117;
using Ruri.SourceGenerated.Classes.ClassID_126;
using Ruri.SourceGenerated.Classes.ClassID_142;
using Ruri.SourceGenerated.Classes.ClassID_198;
using Ruri.SourceGenerated.Classes.ClassID_199;
using Ruri.SourceGenerated.Classes.ClassID_21;
using Ruri.SourceGenerated.Classes.ClassID_221;
using Ruri.SourceGenerated.Classes.ClassID_225;
using Ruri.SourceGenerated.Classes.ClassID_28;
using Ruri.SourceGenerated.Classes.ClassID_43;
using Ruri.SourceGenerated.Classes.ClassID_47;
using Ruri.SourceGenerated.Classes.ClassID_48;
using Ruri.SourceGenerated.Classes.ClassID_74;
using Ruri.SourceGenerated.Classes.ClassID_78;
using Ruri.SourceGenerated.Classes.ClassID_91;
using Ruri.SourceGenerated.Classes.ClassID_95;
using Ruri.SourceGenerated.Classes.ClassID_115;
using Ruri.SourceGenerated.Classes.ClassID_185;
using Ruri.SourceGenerated.Classes.ClassID_215;
using Ruri.SourceGenerated.Classes.ClassID_84;
using Ruri.SourceGenerated.Classes.ClassID_137;
using Ruri.SourceGenerated.Classes.ClassID_171;
using Ruri.SourceGenerated.Classes.ClassID_135;
using Ruri.SourceGenerated.Classes.ClassID_331;
using Ruri.SourceGenerated.Classes.ClassID_1971053207;
using Ruri.SourceGenerated.Classes.ClassID_218;
using Ruri.SourceGenerated.Classes.ClassID_154;
using Ruri.SourceGenerated.Classes.ClassID_156;
using Ruri.SourceGenerated.Classes.ClassID_1953259897;
using Ruri.SourceGenerated.Classes.ClassID_187;
using Ruri.SourceGenerated.Classes.ClassID_483693784;
using Ruri.SourceGenerated.Classes.ClassID_96;
using Ruri.SourceGenerated.Classes.ClassID_73398921;
using Ruri.SourceGenerated.Classes.ClassID_212;
namespace Ruri.RipperHook.EndField_0_8_25;

public partial class EndField_0_8_25_Hook
{
    // AnimationClip
    [RetargetMethod(ClassIDType.AnimationClip, ClassHookVersion)]
    public void AnimationClip_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = AnimationClip.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Animator
    [RetargetMethod(ClassIDType.Animator, ClassHookVersion)]
    public void Animator_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Animator.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // AnimatorController
    [RetargetMethod(ClassIDType.AnimatorController, ClassHookVersion)]
    public void AnimatorController_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = AnimatorController.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // AnimatorOverrideController
    [RetargetMethod(ClassIDType.AnimatorOverrideController, ClassHookVersion)]
    public void AnimatorOverrideController_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = AnimatorOverrideController.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // AssetBundle
    [RetargetMethod(ClassIDType.AssetBundle, ClassHookVersion)]
    public void AssetBundle_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = AssetBundle.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // AudioManager
    [RetargetMethod(ClassIDType.AudioManager, ClassHookVersion)]
    public void AudioManager_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = AudioManager.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // BillboardRenderer
    [RetargetMethod(ClassIDType.BillboardRenderer, ClassHookVersion)]
    public void BillboardRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = BillboardRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // BoxCollider
    [RetargetMethod(ClassIDType.BoxCollider, ClassHookVersion)]
    public void BoxCollider_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = BoxCollider.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // CanvasGroup
    [RetargetMethod(ClassIDType.CanvasGroup, ClassHookVersion)]
    public void CanvasGroup_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = CanvasGroup.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // CapsuleCollider
    [RetargetMethod(ClassIDType.CapsuleCollider, ClassHookVersion)]
    public void CapsuleCollider_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = CapsuleCollider.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // CharacterController
    [RetargetMethod(ClassIDType.CharacterController, ClassHookVersion)]
    public void CharacterController_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = CharacterController.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Cubemap
    [RetargetMethod(ClassIDType.Cubemap, ClassHookVersion)]
    public void Cubemap_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Cubemap.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // CubemapArray
    [RetargetMethod(ClassIDType.CubemapArray, ClassHookVersion)]
    public void CubemapArray_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = CubemapArray.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // CustomRenderTexture
    [RetargetMethod(ClassIDType.CustomRenderTexture, ClassHookVersion)]
    public void CustomRenderTexture_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = CustomRenderTexture.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // GraphicsSettings
    [RetargetMethod(ClassIDType.GraphicsSettings, ClassHookVersion)]
    public void GraphicsSettings_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = GraphicsSettings.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Light
    [RetargetMethod(ClassIDType.Light, ClassHookVersion)]
    public void Light_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Light.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // LineRenderer
    [RetargetMethod(ClassIDType.LineRenderer, ClassHookVersion)]
    public void LineRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = LineRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Material
    [RetargetMethod(ClassIDType.Material, ClassHookVersion)]
    public void Material_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Material.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Mesh
    [RetargetMethod(ClassIDType.Mesh, ClassHookVersion)]
    public void Mesh_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Mesh.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // MeshCollider
    [RetargetMethod(ClassIDType.MeshCollider, ClassHookVersion)]
    public void MeshCollider_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = MeshCollider.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // MeshRenderer
    [RetargetMethod(ClassIDType.MeshRenderer, ClassHookVersion)]
    public void MeshRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = MeshRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // NavMeshProjectSettings
    [RetargetMethod(ClassIDType.NavMeshProjectSettings, ClassHookVersion)]
    public void NavMeshProjectSettings_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = NavMeshProjectSettings.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // ParticleSystem
    [RetargetMethod(ClassIDType.ParticleSystem, ClassHookVersion)]
    public void ParticleSystem_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = ParticleSystem.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // ParticleSystemRenderer
    [RetargetMethod(ClassIDType.ParticleSystemRenderer, ClassHookVersion)]
    public void ParticleSystemRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = ParticleSystemRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // ProceduralMaterial
    [RetargetMethod(ClassIDType.ProceduralMaterial, ClassHookVersion)]
    public void ProceduralMaterial_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = ProceduralMaterial.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // QualitySettings
    [RetargetMethod(ClassIDType.QualitySettings, ClassHookVersion)]
    public void QualitySettings_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = QualitySettings.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // ReflectionProbe
    [RetargetMethod(ClassIDType.ReflectionProbe, ClassHookVersion)]
    public void ReflectionProbe_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = ReflectionProbe.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // RenderSettings
    [RetargetMethod(ClassIDType.RenderSettings, ClassHookVersion)]
    public void RenderSettings_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = RenderSettings.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // RenderTexture
    [RetargetMethod(ClassIDType.RenderTexture, ClassHookVersion)]
    public void RenderTexture_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = RenderTexture.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Shader
    [RetargetMethod(ClassIDType.Shader, ClassHookVersion)]
    public void Shader_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Shader.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // SkinnedMeshRenderer
    [RetargetMethod(ClassIDType.SkinnedMeshRenderer, ClassHookVersion)]
    public void SkinnedMeshRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = SkinnedMeshRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // SparseTexture
    [RetargetMethod(ClassIDType.SparseTexture, ClassHookVersion)]
    public void SparseTexture_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = SparseTexture.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // SphereCollider
    [RetargetMethod(ClassIDType.SphereCollider, ClassHookVersion)]
    public void SphereCollider_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = SphereCollider.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // SpriteMask
    [RetargetMethod(ClassIDType.SpriteMask, ClassHookVersion)]
    public void SpriteMask_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = SpriteMask.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // SpriteRenderer
    [RetargetMethod(ClassIDType.SpriteRenderer, ClassHookVersion)]
    public void SpriteRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = SpriteRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // SpriteShapeRenderer
    [RetargetMethod(ClassIDType.SpriteShapeRenderer, ClassHookVersion)]
    public void SpriteShapeRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = SpriteShapeRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // TagManager
    [RetargetMethod(ClassIDType.TagManager, ClassHookVersion)]
    public void TagManager_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = TagManager.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Terrain
    [RetargetMethod(ClassIDType.Terrain, ClassHookVersion)]
    public void Terrain_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Terrain.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // TerrainCollider
    [RetargetMethod(ClassIDType.TerrainCollider, ClassHookVersion)]
    public void TerrainCollider_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = TerrainCollider.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // TerrainData
    [RetargetMethod(ClassIDType.TerrainData, ClassHookVersion)]
    public void TerrainData_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = TerrainData.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // TerrainLayer
    [RetargetMethod(ClassIDType.TerrainLayer, ClassHookVersion)]
    public void TerrainLayer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = TerrainLayer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Texture2D
    [RetargetMethod(ClassIDType.Texture2D, ClassHookVersion)]
    public void Texture2D_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Texture2D.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Texture2DArray
    [RetargetMethod(ClassIDType.Texture2DArray, ClassHookVersion)]
    public void Texture2DArray_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Texture2DArray.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // Texture3D
    [RetargetMethod(ClassIDType.Texture3D, ClassHookVersion)]
    public void Texture3D_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = Texture3D.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // TilemapRenderer
    [RetargetMethod(ClassIDType.TilemapRenderer, ClassHookVersion)]
    public void TilemapRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = TilemapRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // TrailRenderer
    [RetargetMethod(ClassIDType.TrailRenderer, ClassHookVersion)]
    public void TrailRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = TrailRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    // VFXRenderer
    [RetargetMethod(ClassIDType.VFXRenderer, ClassHookVersion)]
    public void VFXRenderer_ReadRelease(ref EndianSpanReader reader)
    {
        var realThis = (object)this as IUnityObjectBase;
        var dummyThis = VFXRenderer.Create(realThis.AssetInfo, EndFieldClassVersion);

        dummyThis.ReadRelease(ref reader);
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }
}