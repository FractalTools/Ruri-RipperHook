using System.Reflection;
using MonoMod.RuntimeDetour;

namespace Ruri.RipperHook
{
    public static class RuriRuntimeHook
    {
        public static List<ILHook> ilHooks = new List<ILHook>();

        // Missing fields referenced in errors
        public static string gameVer = "";
        public static string gameName = "";

        public static void Init()
        {
            HookLogger.Log($"Initializing hook: {gameName}");

            // Scan for the class with [GameHook(gameName)]
            // We scan the current assembly (Ruri.RipperHook) and maybe others if loaded? 
            // Creating FModelHook project implies it might be in another assembly.
            // Assuming referenced assemblies are loaded.

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type? hookClass = null;

            foreach (var asm in assemblies)
            {
                try
                {
                    var types = asm.GetTypes();
                    hookClass = types.FirstOrDefault(t =>
                    {
                        var attr = t.GetCustomAttribute<GameHookAttribute>();
                        if (attr == null) return false;

                        // 1. Exact Name Match
                        if (attr.GameName == gameName) return true;

                        // 2. Name_Version Match (Generic: GameName + "_" + Version)
                        // Handles mismatch where Enum uses underscores but Version uses dots.
                        if (!string.IsNullOrEmpty(attr.Version))
                        {
                            var constructedName = $"{attr.GameName}_{attr.Version}".Replace(".", "_");
                            var targetName = gameName.Replace(".", "_");
                            if (constructedName == targetName) return true;
                        }
                        return false;
                    });
                    if (hookClass != null) break;
                }
                catch { }
            }

            if (hookClass != null)
            {
                // Instantiate and Initialize
                var instance = (RipperHook)Activator.CreateInstance(hookClass, true);
                instance.Initialize();
                HookLogger.LogSuccess($"Hook {gameName} initialized successfully.");
            }
            else
            {
                HookLogger.LogWarning($"No implementation found for hook: {gameName}");
            }
        }

        public static void DisposeAll()
        {
            // Dispose hooks tracked by Core
            HookManager.DisposeAll();

            // Dispose hooks tracked locally (if any)
            foreach (var hook in ilHooks)
            {
                hook.Dispose();
            }
            ilHooks.Clear();
        }
    }
}
