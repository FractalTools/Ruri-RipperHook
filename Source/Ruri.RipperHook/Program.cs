using AssetRipper.GUI.Web;
using Ruri.Hook.Config;
using Ruri.Hook.Core;
using System;
using System.IO;
using System.Windows.Forms;

namespace Ruri.RipperHook;

internal static class Program
{
    private const string ConfigFileName = "RuriRipperHook.json";

    [STAThread]
    public static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        var config = HookConfig.Load(configPath);
        
        // Show Configuration UI
        Application.Run(new Ruri.Hook.UI.HookSelectionForm(config, configPath));

        // Apply selected hooks
        Ruri.Hook.RuriHook.ApplyHooks(config);

        // Continue with AssetRipper
        RunAssetRipper(args);
    }

    private static void RunAssetRipper(string[] args)
    {
        WebApplicationLauncher.Launch(args);
    }
}