using MonoMod.RuntimeDetour;
using Ruri.RipperHook.Crypto;

namespace Ruri.RipperHook;

/// <summary>
/// Hook到方法体时当前this相当于变成了方法所在的类
/// 因此不能在类里添加任何成员 否则会访问到错误的内存
/// </summary>
public static class RuriRuntimeHook
{
    public static List<ILHook> ilHooks = new();
    public static Dictionary<GameHookType, RipperHook> currentGameHook = new();
    public static string gameName;
    public static string gameVer;

    // [Refactor] 统一使用 CommonDecryptor，具体实现由各游戏Hook初始化时赋值
    // 支持无参解密和带BlockIndex的解密
    public static CommonDecryptor CurrentDecryptor { get; set; } = new CommonDecryptor();

    public static void Init(GameHookType gameName)
    {
        Console.ForegroundColor = ConsoleColor.Blue;

        InstallHook(gameName);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Initialization {gameName} completed Current Hooks Count {ilHooks.Count}");
        Console.ResetColor();
    }

    public static void DestoyHook(ILHook iLHook)
    {
        iLHook.Dispose();
        RuriRuntimeHook.ilHooks.Remove(iLHook);
    }

    private static void InstallHook(GameHookType hookName)
    {
        var name = hookName.ToString();
        if (!name.StartsWith("AR_"))
        {
            var na = name.Split('_');
            gameName = na[0];
            gameVer = string.Join(".", na.Skip(1));
        }

        var type = Type.GetType("Ruri.RipperHook." + name + "." + name + "_Hook");
        if (type == null)
            throw new InvalidOperationException($"Could not find Hook class for {name}");

        currentGameHook[hookName] = (RipperHook)Activator.CreateInstance(type, true);
    }
}