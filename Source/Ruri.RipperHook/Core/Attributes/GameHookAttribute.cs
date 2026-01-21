using System;

namespace Ruri.RipperHook.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class GameHookAttribute : Attribute
{
    public string GameName { get; }
    public string GameVersion { get; }
    public string BaseUnityVersion { get; }

    // Unified constructor with optional parameters
    // 1-arg: [GameHook("Name")] -> Common
    // 2-arg: [GameHook("Name", "GameVer")] -> Game Version specific, unknown Base
    // 3-arg: [GameHook("Name", "GameVer", "BaseVer")] -> Full definition
    // Note: Legacy (Name, Base) must use (Name, "", Base) to avoid assigning Base to GameVer.
    public GameHookAttribute(string gameName, string gameVersion = "", string baseUnityVersion = "")
    {
        GameName = gameName;
        GameVersion = gameVersion;
        BaseUnityVersion = baseUnityVersion;
    }
}
