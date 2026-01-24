using System;

namespace Ruri.Hook.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class GameHookAttribute : Attribute
    {
        public string GameName { get; }
        public string Version { get; }
        public string BaseUnityVersion { get; }

        public GameHookAttribute(string gameName, string version = "", string baseUnityVersion = "")
        {
            GameName = gameName;
            Version = version;
            BaseUnityVersion = baseUnityVersion;
        }
    }
}
