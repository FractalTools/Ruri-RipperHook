using Ruri.RipperHook.Attributes;
using AssetRipper.SourceGenerated;
using System.Text;
using Ruri.RipperHook.HookUtils.ExportHandlerHook;

namespace Ruri.RipperHook.AR;

[GameHook("AR_AssetMapCreator")]
public partial class AR_AssetMapCreator_Hook : RipperHook
{
    public static Dictionary<string, HashSet<ClassIDType>> assetClassIDLookup = new Dictionary<string, HashSet<ClassIDType>>();
    public static Dictionary<string, HashSet<string>> assetDependenciesLookup = new Dictionary<string, HashSet<string>>();
    public static Dictionary<string, HashSet<string>> assetListLookup = new Dictionary<string, HashSet<string>>();
}