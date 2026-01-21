using AssetRipper.GUI.Web;
using Ruri.RipperHook.Config;

namespace Ruri.RipperHook;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Hook(args);
        RunAssetRipper(args);
    }

    private static void Hook(string[] args)
    {
        var hooks = RuriHookConfigManager.Load();
        foreach (var hook in hooks)
        {
            RuriRuntimeHook.Init(hook);
        }
    }

    private static void RunAssetRipper(string[] args)
    {
        WebApplicationLauncher.Launch(args);
    }
}