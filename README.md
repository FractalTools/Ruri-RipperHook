## 不要问我怎么用
- 作者不喜欢回复小白问题 因为他很忙 并且曾经是小白的时候受过心理创伤现在极度反社会 你应该找ai帮你

## Feature
- AssemblyDumper support
- Free Shader Decompile (DX11)

## Todo
- 需要优化Block格式的AB包解析(WMW/VFS/BLK等) 内存拆分读取容易过于碎片化导致内存无法分配 (临时方案是先dump到磁盘再读取)
- 更小的AssemblyDumper生成 目前有太多代码实际上不需要生成 最小能优化到1mb以下的dll 只需要里面的定义和Read就够了
- AssemblyDumper生成工作流简化

## Special Thanks to:
- **ds5678**: Original author.
- **nesrak1**: USCSandbox author.
- **Razmoth**: For anything.
