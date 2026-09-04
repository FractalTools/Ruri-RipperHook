using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_137;
using AssetRipper.SourceGenerated.Classes.ClassID_2;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_23;
using AssetRipper.SourceGenerated.Classes.ClassID_33;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Extensions;
using System.Numerics;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// GameObject hierarchies in one package: a node is a GameObject with its Transform, parented
/// by handing it the parent's Transform, and renderers hang off a node the same way. Every
/// transform value arrives already in Unity's basis.
/// </summary>
public sealed class HierarchyBuilder
{
    private readonly ConvertedPackage package;

    public HierarchyBuilder(ConvertedPackage package)
    {
        this.package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public ConvertedPackage Package => package;

    public IGameObject Node(string name, ITransform? parent, Vector3 position, Quaternion rotation, Vector3 scale, string? originalPath = null)
    {
        IGameObject gameObject = package.Create<IGameObject>(ClassIDType.GameObject, name, originalPath);
        gameObject.SetIsActive(true);
        ITransform transform = package.Create<ITransform>(ClassIDType.Transform, name, null);
        transform.InitializeDefault();
        transform.LocalPosition_C4.SetValues(position.X, position.Y, position.Z);
        transform.LocalRotation_C4.SetValues(rotation.X, rotation.Y, rotation.Z, rotation.W);
        transform.LocalScale_C4.SetValues(scale.X, scale.Y, scale.Z);
        transform.GameObject_C4P = gameObject;
        gameObject.AddComponent(ClassIDType.Transform, transform);
        if (parent is not null)
        {
            Attach(transform, parent);
        }
        return gameObject;
    }

    public void Attach(ITransform child, ITransform parent)
    {
        child.Father_C4P = parent;
        parent.Children_C4P.Add(child);
        if (child.Has_RootOrder_C4())
        {
            child.RootOrder_C4 = parent.Children_C4.Count - 1;
        }
    }

    public IMeshRenderer StaticMesh(IGameObject node, IMesh? mesh, IReadOnlyList<IMaterial?> materials)
    {
        IMeshFilter filter = package.Create<IMeshFilter>(ClassIDType.MeshFilter, node.Name, null);
        filter.GameObjectP = node;
        filter.MeshP = mesh;
        node.AddComponent(ClassIDType.MeshFilter, filter);

        IMeshRenderer renderer = package.Create<IMeshRenderer>(ClassIDType.MeshRenderer, node.Name, null);
        renderer.GameObjectP = node;
        renderer.Enabled = true;
        foreach (IMaterial? material in materials)
        {
            renderer.MaterialsP.Add(material);
        }
        node.AddComponent(ClassIDType.MeshRenderer, renderer);
        return renderer;
    }

    public ISkinnedMeshRenderer SkinnedMesh(IGameObject node, IMesh? mesh, IReadOnlyList<ITransform> bones, ITransform? rootBone,
        IReadOnlyList<IMaterial?> materials, Vector3 boundsCenter, Vector3 boundsExtent)
    {
        ISkinnedMeshRenderer renderer = package.Create<ISkinnedMeshRenderer>(ClassIDType.SkinnedMeshRenderer, node.Name, null);
        renderer.GameObjectP = node;
        renderer.Enabled = true;
        renderer.MeshP = mesh;
        foreach (ITransform bone in bones)
        {
            renderer.BonesP.Add(bone);
        }
        renderer.RootBoneP = rootBone;
        renderer.AABB.Center.SetValues(boundsCenter.X, boundsCenter.Y, boundsCenter.Z);
        renderer.AABB.Extent.SetValues(boundsExtent.X, boundsExtent.Y, boundsExtent.Z);
        foreach (IMaterial? material in materials)
        {
            renderer.MaterialsP.Add(material);
        }
        node.AddComponent(ClassIDType.SkinnedMeshRenderer, renderer);
        return renderer;
    }

    public T Component<T>(IGameObject node, ClassIDType classId) where T : IComponent
    {
        T component = package.Create<T>(classId, node.Name, null);
        component.GameObject_C2P = node;
        node.AddComponent(classId, component);
        return component;
    }
}
