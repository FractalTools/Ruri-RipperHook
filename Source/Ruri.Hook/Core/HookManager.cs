using MonoMod.RuntimeDetour;
using System.Collections.Generic;

namespace Ruri.Hook.Core
{
    public static class HookManager
    {
        private static readonly List<ILHook> _hooks = new List<ILHook>();

        public static void Register(ILHook hook)
        {
            _hooks.Add(hook);
        }

        public static void DisposeAll()
        {
            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }
            _hooks.Clear();
        }
    }
}
