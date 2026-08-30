# Ruri-RipperHook — 内核约束(唯一常驻)

> 框架事实/流水线/坑表 = [FRAMEWORK.md](FRAMEWORK.md)(写或调 hook 前读那份,不是这份) · UE 符号真值矩阵 = [Source/Ruri.ShaderDecompiler/UE_SYMBOL_SOURCES.md](Source/Ruri.ShaderDecompiler/UE_SYMBOL_SOURCES.md)。
> 通用工程律 = skill `ruri-engineering-discipline`(写码/重构/移植前先过)。规则与用户指令冲突或规则本身错 → 先改本文,再写代码。
> 定位:`RuriRipperImporter`(Blender 插件)的上游数据管线 —— 通用跨引擎资产格式转换工具链;对外描述中立技术化,各来源的容器格式适配住 `AssetRipperGameHook` 子模块,不进对外描述。

1. **可编辑区 = 现有 `Source/Ruri.*/**`**(RipperHook/Tpk/Hook/ShaderDecompiler/FModelHook);`AssetRipper/**` 与所有子模块冻结,只读。
2. **禁新建 assembly**:任何特性(含重型/原生 NuGet 依赖)落进现有 csproj,默认 `Ruri.RipperHook`;想为"隔离依赖"或"可扩展性"起新项目=信号错误,改为往核心加 hook。
3. **只用 AOP**:游戏行为走 `[RipperHook(GameType.X,"游戏版本","引擎版本")]`,宿主能力走 `[RipperFeature("Name")]`(两者正交,FRAMEWORK §7);禁在子模块里子类化/monkey-patch,禁共享代码里 `if(game==X)`,禁 ProjectReference 上游再改它。临时探查可改子模块,收工 `git checkout` 还原。
4. **hook 只走 Ruri.Hook** 的 `[RetargetMethod]`/`[RetargetMethodFunc]`/`[RetargetMethodCtorFunc]` + `Initialize()`;禁裸 `new MonoMod...Hook/ILHook`(唯一例外=`Ruri.Hook` 自身的 `ReflectionExtensions.RetargetCall*`)。
5. **上游改了不报错是本仓库头号静默故障** —— 出任何 hook 相关报错/错数据/内存暴涨,先查 FRAMEWORK §2 的 IL 指纹闸门(黄字 `Upstream rewrote ...`);先读上游 diff 再重录基线,顺序反了=把真 bug 盖章成正常。
6. **写 hook 优先级**:上游有扩展点就用/没有就去上游加 > 包装原方法 > 整体替换(整体替换必须录基线)。
7. **扩展点而非特例**:新游戏/格式/导出器必须零改共享代码即可插入;分发靠数据(注册表/委托表/attribute 发现),标准接缝 = `ExportHandlerHook.CustomAssetProcessors` + `RegisterModule(...)`(FRAMEWORK §6);一条数据分支胜过 N 份编译期分叉。
8. **导出看到的是纯净 Unity 数据**:解密/ACL 解码/自定义容器全由读路径 hook 变透明,处理与导出阶段禁重新处理;新导出格式(如 USD)=hook 替换或增强某个 AR 导出方法直接消费干净模型,禁并行服务重推数据。
9. **类型树是数据不是生成代码**:真源 `Source/Ruri.RipperHook/Libraries/RuriTypeTree.tpk`(内嵌资源),重打 = `dotnet build Source/Ruri.Tpk/Ruri.Tpk.csproj -c Debug` 再跑其 exe;tpk 表达不了的偏差走 `[TypeTreeNodeGate]`/`[TypeTreeValueFix]`/`[TypeTreePostRead]`,禁共享代码加分支。
10. **引擎级 hook 装在 Common 类的 `InitAttributeHook`**,不是每版本各一份;安装函数须幂等(范例 `EndfieldShaderBindingHook.Install()` 跨 5 版本重入无害)。
11. 风格:**代码=英文**;日志走项目 logger 带分类(FRAMEWORK §10);并行时只对共享非线程安全状态串行(范例 FRAMEWORK §12 逐次反编译锁);其余(禁缩写/一文件一单元/禁注释/0-GC/SIMD)见 skill。
12. **git**:里程碑即提交即 push,每里程碑各自一个 commit 各自 push,禁攒大包;`add` 按名点名禁 `-A`/`.`;严禁 AI 署名 trailer;子模块先提交推送再 bump 父仓 gitlink;禁提交 WIP/坏构建/琐碎回退。消息:代码=一行简短中文(匹配现有日志风格);`.md`=多行正文,点明加/重构了哪些章节以及**原因**(结构或行为转变,非字面改动),2–4 行抓意图。
13. **测试循环**:一律导出到 `D:\Ruri\Temp\AntiGravity\AssetRipperHookOutput` 与 `FModelHookOutput`(CLI 每次运行自动清空,禁塞额外文件夹);开新运行前先杀残留 `Ruri.RipperHook.CLI.exe`;长运行走 `run_in_background`+`Monitor` until-loop,禁用短 sleep 串绕过死锁守卫。
14. **两个反汇编 GameType 可叠加**:`Il2CppMethodDump`(把原生 asm 注释注入反编译脚本)、`DisassemblyExporter`(只出代码、跳过资产、全程序集强制反编译);模型来自加载期 `Cpp2IlApi.CurrentAppContext`,仅 IL2CPP、opt-in,**禁在导出/哑 DLL 保存阶段 dump**;架构/坑/迭代探针见 FRAMEWORK §12。
15. **FModelHook 唯一入口 = 无头 CLI,绝不 `new FModel.App()`**;导出级别全由命令行参数控制;架构/桥/缓存/native 依赖见 FRAMEWORK §15。
