using System.Collections.Generic;

namespace Ruri.RipperHook.Config;

public class RuriHookConfig
{
    /// <summary>
    /// 需要启用的 Hook 列表
    /// 支持枚举名称 (字符串) 或 枚举值 (整数)
    /// </summary>
    public List<object> EnabledHooks { get; set; } = new();

    /// <summary>
    /// 仅供参考的可用选项列表，程序启动时自动生成
    /// </summary>
    public List<string> AvailableOptionsReference { get; set; } = new();
}