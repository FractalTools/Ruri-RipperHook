using System;
using System.Linq;
using System.Reflection;
using Ruri.Hook;
using Ruri.Hook.Attributes;
using Ruri.Hook.Core;

namespace Ruri.FModelHook
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            InitializeHooks();
            LaunchFModel();
        }

        private static void InitializeHooks()
        {
            var hookName = "UE_ShaderDecompiler";
            HookLogger.Log($"Initializing hook: {hookName}");

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var hookClass = assembly.GetTypes().FirstOrDefault(t => 
                {
                    var attr = t.GetCustomAttribute<GameHookAttribute>();
                    return attr != null && attr.GameName == hookName;
                });

                if (hookClass != null)
                {
                    var instance = (RuriHook)Activator.CreateInstance(hookClass, true)!;
                    instance.Initialize();
                    HookLogger.LogSuccess($"[+] Hook {hookName} initialized successfully.");
                }
                else
                {
                    HookLogger.LogWarning($"[-] No implementation found for hook: {hookName}");
                }
            }
            catch (Exception ex)
            {
                HookLogger.LogFailure($"Failed to initialize hook {hookName}: {ex}");
            }
        }

        private static void LaunchFModel()
        {
            HookLogger.Log("Launching FModel...");
            try
            {
                var app = new FModel.App();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                HookLogger.LogFailure($"FModel crashed: {ex}");
            }
        }
    }
}
