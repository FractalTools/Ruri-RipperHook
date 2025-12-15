# ⚠️⚠️⚠️ **WARNING** ⚠️⚠️⚠️

# **❌ 不要 star，更不要 fork 项目备份 保持低调 ❌**

# **❌ DO NOT star, and DO NOT fork this project for backup Keep a low profile ❌**

# **否则我将视情况私有化此项目，你将永远无法获得任何更新**
# **唯一的例外是PR 但合并或拒绝后立刻删除fork**

# **Otherwise, I may privatize this project at my discretion, and you will permanently lose access to all future updates**

## 不要问我怎么用
- 作者不喜欢回复小白问题 因为他很忙 并且曾经是小白的时候受过心理创伤现在极度反社会 你应该找ai帮你
- 最基本的使用是clone项目选择Ruri.RipperHook启动 通过RuriHookConfig.json配置游戏 默认会跳过动画导出 因为解析太慢了
- 有时候会hook失败 但这好像是因为内存遗留的原因 通常加个空格然后触发重新编译就可以解决

## Feature
- AssemblyDumper support
- Free Shader Decompile (DX11)

## Todo
- 需要优化Block格式的AB包解析(WMW/VFS/BLK等) 内存拆分读取容易过于碎片化导致内存无法分配 (临时方案是先dump到磁盘再读取)
- 更小的AssemblyDumper生成 目前有太多代码实际上不需要生成 最小能优化到1mb以下的dll 只需要里面的定义和Read就够了
- AssemblyDumper生成工作流简化
- 如果不同游戏版本依赖同样的加密 新版本应该直接依赖旧版本 任何相同的代码都不应该出现2次

## Special Thanks to:
- **ds5678**: Original author.
- **AnimeStudio**: For anything.
- **nesrak1**: USCSandbox author.
- **Razmoth**: For anything.
