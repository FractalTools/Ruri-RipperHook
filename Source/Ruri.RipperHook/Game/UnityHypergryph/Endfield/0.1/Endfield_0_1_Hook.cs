using Ruri.RipperHook.EndFieldCommon;
using Ruri.RipperHook.Crypto;
using Ruri.RipperHook.HookUtils.BundleFileBlockReaderHook;

namespace Ruri.RipperHook.EndField_0_1;

public partial class EndField_0_1_Hook : RipperHook
{
    public static FairGuardDecryptor Decryptor;

    protected EndField_0_1_Hook()
    {
        Decryptor = new FairGuardDecryptor();
    }

    protected override void InitAttributeHook()
    {
        additionalNamespaces.Add(typeof(EndFieldCommon_Hook).Namespace);
        AddNameSpaceHook(typeof(BundleFileBlockReaderHook).Namespace, () => { BundleFileBlockReaderHook.CustomBlockCompression = EndField_0_1_Hook.CustomBlockCompression; });
        base.InitAttributeHook();
    }
}