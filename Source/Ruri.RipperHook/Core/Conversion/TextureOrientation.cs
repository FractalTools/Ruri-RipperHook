using System.Runtime.CompilerServices;
using AssetRipper.Assets;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// Which textures hold their first row at the top. Unity keeps a texture bottom-up and
/// AssetRipper's exporter turns every image over on its way out; a texture registered here was
/// stored the way its source keeps it -- compressed blocks cannot be turned over cheaply, and a
/// decoded image need not be turned at all -- and the export hook leaves it as it is.
/// </summary>
public static class TextureOrientation
{
    private static readonly ConditionalWeakTable<IUnityObjectBase, object> topDown = new();
    private static readonly object mark = new();

    public static void MarkTopDown(IUnityObjectBase texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        topDown.AddOrUpdate(texture, mark);
    }

    public static bool IsTopDown(IUnityObjectBase texture) => topDown.TryGetValue(texture, out _);
}
