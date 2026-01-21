using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.ExAstrisCommon;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;

namespace Ruri.RipperHook.ExAstris_1_0;

public partial class ExAstris_1_0_Hook : RipperHook
{
    protected ExAstris_1_0_Hook()
    {
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(ExAstrisCommon_Hook).Namespace);
        AddNameSpaceHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = ExAstrisCommon_Hook.CustomBlockCompression; });
        base.InitAttributeHook();
    }
}