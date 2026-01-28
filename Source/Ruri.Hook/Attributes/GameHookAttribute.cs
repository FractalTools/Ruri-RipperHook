using System;

namespace Ruri.Hook.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class GameHookAttribute : Attribute
    {
        public string GameName { get; }
        public string Version { get; }
        public string BaseEngineVersion { get; }

        public GameHookAttribute(string gameName, string version = "", string baseEngineVersion = "")
        {
            GameName = gameName;
            Version = version;
            BaseEngineVersion = baseEngineVersion;
        }
    }
}
