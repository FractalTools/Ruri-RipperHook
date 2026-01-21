using Ruri.RipperHook.UnityMihoyo;

namespace Ruri.RipperHook.Houkai;

public struct WMVInfo
{

    public string FilePath { get; set; }
    public int FileSize { get; set; }
    public int FileCount { get; set; }
    public BlockAssetInfo[] UnitAssetArray { get; set; }
}