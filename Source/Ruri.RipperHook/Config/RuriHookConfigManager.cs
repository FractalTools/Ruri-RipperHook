using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Ruri.RipperHook.Config;

public static class RuriHookConfigManager
{
    private const string ConfigFileName = "RuriHookConfig.json";

    public static IEnumerable<GameHookType> Load()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

        if (!File.Exists(configPath))
        {
            CreateDefaultConfig(configPath);
        }

        return ParseConfig(configPath);
    }

    private static void CreateDefaultConfig(string path)
    {
        var config = new RuriHookConfig();
        var enumType = typeof(GameHookType);
        var names = Enum.GetNames(enumType);

        foreach (var name in names)
        {
            var value = (int)Enum.Parse(enumType, name);
            config.AvailableOptionsReference.Add($"{name} = {value}");
        }

        // 默认不开启任何 Hook，或者可以在这里添加一些默认值
        // config.EnabledHooks.Add("AR_ShaderDecompiler");

        try
        {
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(path, json);
            Console.WriteLine($"[Config] Generated default configuration at: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to create default config: {ex.Message}");
        }
    }

    private static IEnumerable<GameHookType> ParseConfig(string path)
    {
        RuriHookConfig? config = null;
        try
        {
            var json = File.ReadAllText(path);
            config = JsonConvert.DeserializeObject<RuriHookConfig>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to load config: {ex.Message}");
        }

        if (config?.EnabledHooks == null)
        {
            yield break;
        }

        foreach (var item in config.EnabledHooks)
        {
            if (TryResolvHook(item, out var hookType))
            {
                yield return hookType;
            }
        }
    }

    private static bool TryResolvHook(object input, out GameHookType hookType)
    {
        hookType = default;

        // 处理字符串形式 (枚举名)
        if (input is string strVal)
        {
            if (Enum.TryParse(strVal, true, out hookType))
            {
                return true;
            }
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Config] Warning: Unknown hook name '{strVal}'");
            Console.ResetColor();
            return false;
        }

        // 处理整数形式 (枚举值)
        // Newtonsoft.Json可能会将数字反序列化为 Int64 (long)
        if (input is int || input is long)
        {
            var intVal = Convert.ToInt32(input);
            if (Enum.IsDefined(typeof(GameHookType), intVal))
            {
                hookType = (GameHookType)intVal;
                return true;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Config] Warning: Unknown hook id '{intVal}'");
            Console.ResetColor();
            return false;
        }

        return false;
    }
}