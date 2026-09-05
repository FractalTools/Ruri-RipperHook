# Ruri-RipperHook — 框架事实

> 约束/铁律 = [CLAUDE.md](CLAUDE.md)。本文只放**事实与坑**：类型名、方法签名、流水线顺序、数值、判据。
> **维护指令**：这是快照不是圣经。发现说法过期 → 同一次编辑里改对；发现某段描述的 workaround 已有官方 API → 换掉并删旧路径；踩到新坑 → 加一条。错的/过期的文档比没有更糟。写事实,不写经过。

---

## 1. 构建 / 运行

| 需求 | 命令 |
|---|---|
| 编译 | `dotnet build Source/Ruri.RipperHook/Ruri.RipperHook.csproj -c Debug --nologo` |
| exe 目录 | `AssetRipper/Source/0Bins/AssetRipper/Debug/`（`Ruri.RipperHook.CLI/.GUI.exe`、`Ruri.FModelHook.GUI.exe`） |
| 列 hook | `Ruri.RipperHook.CLI.exe --list-hooks`（JSON，退出码 3） |
| 无头导出 | `--hook <Id> --load <path> --export <dir> [--fail-fast false] [--log-level Info]`（写入前清空 `--export`） |
| GUI 锁 DLL | `Get-Process Ruri.RipperHook.GUI -EA SilentlyContinue \| Stop-Process -Force` —— 仅拷贝失败时 |
| GameType | `Source/Ruri.RipperHook/Core/GameType.cs`，成员名 = 该游戏 player 的 productName（§7）；特性不进此枚举 |

---

## 2. Ruri.Hook attribute

| Attribute | 用途 |
|---|---|
| `[RetargetMethod(typeof(T), name, isBefore, isReturn)]` | IL 注入。`true,true`=prefix-replace(跳过原方法)；`true,false`=prefix-continue；`false,false`=postfix-continue |
| `[RetargetMethodFunc(typeof(T), name)]` | 完整 IL manipulator，签名 `static bool Foo(ILContext il)` |
| `[RetargetMethodCtorFunc(typeof(T))]` | patch 无参 ctor，签名同上；用于翻默认字段值 |

- **postfix-continue 在只有单个 `Ret` 的实例 `void` 方法上不可信**：`while(TryGotoNext(Before, Ret))` 实测误触发。行为等价时改用 prefix-continue。
- **继承发现坑**：`Registry.ApplyTypeHooks(GetType())` 用的 `GetMethods` 没有 `FlattenHierarchy` → **继承来的 `public static` 方法不返回**。基类/公共 partial 上的 `[RetargetMethod]` 不会被派生 hook 拾取，需在派生的 `InitAttributeHook()` 里显式 `Registry.ApplyTypeHooks(typeof(MyCommon_Hook));`（范例 `Arknights_2_7_31_Hook`）。
- **生命周期**：`Bootstrap.ApplyHooks(config)` → `RuriHook.ApplyHooks` 实例化匹配 `config.EnabledHooks` 的类型 → `Initialize()` → `RipperHookCommon.Initialize` 先 `InitAttributeHook()` 再注册 `RuriRuntimeHook`。

### ⚠️ 上游改了、这边不报错 —— hook 怪问题一律先查这条

整体替换 = 留下**写 hook 当天的行为快照**。上游往原方法加一步后三道防线全不响：签名没变、方法还在、行为照旧。**实付代价**：上游给 block 读取加了一次虚拟文件系统分配，替换版没有，不报错，直接 OOM。

**闸门**（`HookTargetFingerprint` + `HookBaselines`）：装 detour 前算目标方法 IL 指纹，比对 `Source/Ruri.Hook/HookBaselines.json`。指纹 = opcode 序列（跳过 `nop`）+ 每条指令引用的成员名，**不含 metadata token 与分支偏移** → 上游重编译但没动该方法不误报。

**★基线按构建配置分开存**（`{"optimized":{…},"unoptimized":{…}}`）：同一份源码 Debug 与 Release 的 IL 不同（Debug 留 `nop`、多余 `br`、`stloc/ldloc` 往返，Release 优化掉）。实测拿 Debug 基线跑 Release 构建**41 条里 21 条误报**——这种闸门必被无视，比没有更糟。配置**按声明程序集逐个判**（`DebuggableAttribute.IsJITOptimizerDisabled`），因为一次运行里 NuGet 交付的程序集恒优化、源码构建的随解决方案配置走（当前 41 个目标：17 个只有 optimized、24 个两套都有）。**换配置跑之前先 `RURI_HOOK_BASELINE=1` 录一遍那个配置**，否则只是 `have no baseline` 提示、不是警告。

| 黄字 | 处置 |
|---|---|
| `Upstream rewrote <方法> since the hook '<id>' was written` | 方法体变了，带新旧指纹 |
| `N hooked upstream method(s) have no baseline` | 该目标从未录基线 |
| `X.Y has N overloads and the hook names none of them` | 绑哪个重载由反射返回顺序决定 → attribute 补 `typeof(...)` |
| `'A' was relaxed to 'B'` | 省略方法名被放松匹配，上游改名会静默绑到别处 → 显式写目标名 |

**顺序不可颠倒**：先 `git -C AssetRipper log -p <上游文件>` 读该方法 diff、判断要不要跟，再 `RURI_HOOK_BASELINE=1` 重录基线并提交。**反了 = 把真 bug 盖章成正常。**

---

## 3. AssetRipper 数据流（冻结）

```
SchemeReader.LoadFile → FileBase（FilePath 由 Scheme<T>.Read 设）
  bundle: ReadFileStreamData 加 ResourceFile（FilePath=bundle.FilePath, Name=entry.Path=CAB）
  FileContainer.ReadContents → ResourceFile 提升为 SerializedFile，保留 FilePath
GameBundle.FromPaths → SerializedBundle.FromFileContainer → bundle.AddCollectionFromSerializedFile（此处丢 container.FilePath）
  → SerializedAssetCollection.FromSerializedFile（设 Name=file.NameFixed，从不设 FilePath）
    → ReadData 遍历 file.Objects → factory.ReadAsset(assetInfo, ObjectData, type, refTypes) → collection.AddAsset
ExportHandler.Process → GetProcessors() 顺序：
  SceneDefinition → OriginalPath → MainAsset → AnimatorController → AudioMixer
  → EditorFormat → LightingData → Prefab → Sprite → ScriptableObject
ExportHandler.Export → ExportCollections 写 YAML/贴图
```

**死胡同**：
- `ObjectInfo.ObjectData` / `byteSize` load 后即 GC，只能在 `SerializedAssetCollection.ReadData` postfix 拿；AR 不保存到任何地方。
- `AssetCollection.FilePath` 可设但 AR 从不设 → hook `ReadData` postfix 做 `collection.FilePath = file.FilePath`。
- 二进制 `asset.Write(AssetWriter)` 对 source-gen 类可用但很慢，**禁在 BuildAssetList 期间调**。
- **Bundle 重建不支持**（`FileStreamBundleFile.WriteFileStreamData` 抛 `NotImplementedException`、`ArchiveBundleFile.Write` 抛 `NotSupportedException`、无 YAML reader）→ 那种场景用 `AssetsTools.NET`（已 PackageReference）。

---

## 4. Path / OriginalPath

- `OriginalPath` setter 存 fullPath；`OriginalDirectory/Name/Extension` 经 `Path.*` 派生（Windows 派生字段是反斜杠，**存储值保留正斜杠**）。
- `GetBestDirectory()` = `OverrideDirectory > OriginalDirectory > "Assets/{ClassName}"`。
- `OriginalPathProcessor` 遍历每个 `IAssetBundle.Container`，给 `Asset.FileID == 0` 的条目设 `OriginalPath`。
- `BundledAssetsExportMode` 默认 `DirectExport`（只经 `OriginalPathHelper.EnsureStartsWithAssets` 加 `Assets/` 前缀，保留斜杠）；`GroupByBundleName` 走 `Path.Join` → 反斜杠。

**合成 Container 条目**（Arknights 路径修复）：
```csharp
AccessPairBase<Utf8String, IAssetInfo> pair = bundle.Container.AddNew();
pair.Key = new Utf8String(myForwardSlashPath);
pair.Value.Asset.SetAsset(bundle.Collection, asset as IObject);  // 需 IObject，不是 IUnityObjectBase
```
把 `OriginalPathProcessor.Process` 作 **prefix-continue** hook，让 AR 自己消费我们的条目。跳过已在 Container 里的、跳过 `IAssetBundle` 本身、跳过非 `IObject`。

---

## 5. Source-generated 参考

只读源码镜像（grep 用）：`D:\Ruri\Git\FractalTools\AssemblyDumper\AssetRipper\SourceGenerated\` —— 接口在 `Classes/ClassID_<N>/I<Class>.cs`，实现在 `<Class>_<version>.cs`。核对签名（`IMonoBehaviour.GameObjectP`、`IAssetBundle.Container`）用它。

---

## 6. 自定义 IAssetProcessor 注入

`Utils/Hook/ExportHandlerHook.cs` 对 `ExportHandler.GetProcessors` 下 ILHook，在每个 `ret` 处 `EmitDelegate` 接管返回值 —— **上游流水线原样跑，本仓库一行不复制**，只在返回序列上插入。`ExportHandler.Process` 完全不碰。

```csharp
RegisterModule(new ExportHandlerHook());
ExportHandlerHook.Register(new AssetProcessorRegistration
{
    InsertBefore = typeof(LightingDataProcessor),
    Factory = MyDelegate,   // MyDelegate(FullConfiguration s) => yield return ...
});
```

- **禁抄上游 processor 表**：曾用手工镜像，漏了 `OriginalPathProcessor` → 全闭包资产 `OriginalPath` 为 null → `ImportReachable` 的 seed→guid join 全断，整场景静默导入为空（base01_lv001：1619 个 placement 全 UNRESOLVED、0 source）。镜像 = 第二真源。
- **位置是注册方的事实**：`InsertBefore` 由注册方声明，共享代码 grep 不出任何具体 processor 类型。当前三个 hook 都锚 `LightingDataProcessor`（= 上游 `//Static mesh separation goes here` 处）。
- 锚点消失时 `Splice` **抛异常并点名注册方**，绝不静默丢弃。
- `Settings` 按类型反查（唯一那个 `FullConfiguration` 属性），不用属性名字符串。
- `Register` 按 `Factory` 委托幂等 —— hook 停用后重启用会再跑 `InitAttributeHook`（Blender 切游戏 tab 就会），否则同一 processor 越堆越多。

---

## 7. 解码器与特性（正交）

| | 解码器 decoder | 特性 feature |
|---|---|---|
| attribute | `[RipperHook(GameType.X, "游戏版本", "引擎版本")]` | `[RipperFeature("Name")]` |
| 语义 | 一个游戏 | 宿主能力，与游戏无关 |
| id | `产品名_版本` | 就是那个名字（无 `AR_` 前缀、无尾下划线） |
| 并存 | 一个进程只活一个（`RuriHook.ApplyHooks` 保证） | 互不排斥 |

区别是 **attribute 类型**，不是 flag+前缀 —— 没有宿主再 `StartsWith("AR_")`。唯一反射目录 = `HookCatalog`（一次扫描，`AssemblyLoad` 时失效重建）。

**特性只保留给 AR 原生支持之上的扩展。** 新增前先 grep `ProcessingSettings`/`ExportSettings`/`ImportSettings` 找等价属性；已有带合理默认值的属性就用 `[RetargetMethodCtorFunc]` 翻默认值或暴露成 Settings 控件，别发并行特性。
- 活着的特性：`SkipStreamingAssetsCopy`、`SkipProcessingAnimation`、`ShaderDecompiler`、`PrefabOutlining`、`StaticMeshSeparation`、`Il2CppMethodDump`（§12）。
- 已删（原生默认够用）：`BundledAssetsExportMode`。

**宿主呈现**：WinForms GUI 解码器进 Hooks 树（互斥）、特性进 Settings「Features」组（同一个 `HookConfig.EnabledHooks`，只是从树里隐藏），首次运行默认值在 `GUI/Program.cs:Main` 种下、门控 `!File.Exists(configPath)`；Blender 插件**特性不出现在 UI**，由 `RipperBlenderBridge.HostFeatures` 一处常量无条件启用（当前只有 `HumanoidToGeneric`），解码器按安装身份自动解析（§8），每 tab 一个。名字写错 = 启动即抛。

---

## 8. 身份探测与解码器解析

真源是**游戏自己发布的那一个文件**，整合包目录名/cabmap 文件名/用户勾选一律不算。

| 字段 | 来自 |
|---|---|
| `Company`/`Product`/`GameVersion` | `globalgamemanagers` 里 **ClassID 129 (PlayerSettings)** 对象的自有字节 |
| `EngineVersion` | 同一文件的序列化头 |

**`app.info` 不参与身份**（引擎写的副本，实测会改写真值：`illusion\Koikatu`→`illusion__Koikatu`；Endfield 公司在那里是 `Gryphline`，PlayerSettings 是 `Hypergryph`）。

- `Core.Install.InstallProbe`：`Read(root)` 每 player 一行；`Project(root)` 挑出本安装是哪个 player —— 规则只用安装自己的字段（companyName 以哪个 product 结尾、哪个 product 被别的当前缀扩展），**不查已知游戏名单**。一个安装常带多个 player（本体+VR+Studio）。
- 定位 PlayerSettings：用 AR 的 `SchemeReader`/`SerializedFile` 解析头+类型表+对象表，取 `ClassID==129` 的 `ObjectData` —— **精确字节，不是搜文件**。AR 不生成 ClassID 129 的类，故按写入顺序读长度前缀字符串：companyName、productName、其后第一个版本形状的是 bundleVersion。实测 5.6.2/5.6.3/2019.4.9/2019.4.40/2021.3.x 共 10 个 player 全中。**这是唯一一处按顺序取值**；彻底结构化需给 class 129 备类型树（stock tpk 有）。
- **自己文件被变换过的 build**：类打 `[RipperEngineFile]` + `public static bool TryDecrypt(byte[] data)`（认自己的 magic、原地还原、返回 true）。由已有的 `HookCatalog` 扫描顺带收集，**不按游戏索引**（读它就是为了知道是哪个游戏，按游戏索引成环），generic 解析失败时逐个问。探测早于任何 hook 应用，故是纯静态方法。打了属性没那个方法 = 第一次探测就抛。
- `HookCatalog.Resolve(product, gameVersion, engineVersion)`：该 product 的解码器里**最新且 `Version` ≤ 本 build 游戏版本**的那个，排除声明了别的引擎版本的。解码器 `Version` = **它从哪个游戏版本起适用** → 补丁没改坏就不加类。任一侧未知不构成约束。手动指定永远压过解析。
- 桥出口 `ReadInstall(root)`/`ListDecoders()`/`ResolveDecoder(...)` 只要 CLR，不要 session/cabmap/hook。

**EXILIUM 变换已 1:1 移植**（`EXILIUMCommon_EngineFileDecryptor`）：只变换开头 **0x3EA (1002)** 字节（头+引擎版本串+类型表+对象表），其后磁盘上本就是明文（所以从前能裸读到 `SunBorn`/`EXILIUM`/`2.7` 却解析不了头）。算法：认 magic（`4E 50` 或 `3D A?`，盖掉的正是恒为 0 的 metadataSize 高两字节）→ 按首个 body dword 低 2 位四选一算 seed → **改造版 RC4**（每密钥字节先 `ror8(K,2)` 再 `+0x3A`）→ 自定义 CRC（步进 `(c^0x09823D6E)>>1`）从尾 8 字节推 key → 前 5 个 32 字节块按 `blk%3` 三种变换，每块拿上一块第 0x1C 字节当下块 key。
- **入口怎么找的**：`NEP2.dll` 唯一导出 `NEP_Unknown` 是 `ret` 桩；游戏靠它在 **stock `UnityPlayer.dll` RVA `0x822280`（`FileStream::Read`）** 上打 inline jmp。解密函数在 **NEP2 RVA `0x4B0880`**，它和被调用者是未混淆静态代码（磁盘映像==运行时映像）。静态搜常量永远搜不到（那些 AES/SM4/MD5 表是静态链接的 OpenSSL 1.1.1 + Lua VM，属 FairGuard 授权层，与文件密码无关）。移植结果与 DLL 输出全文件逐字节一致。
- **引擎文件与 bundle 是两个编辑器版本打的**：引擎文件 `2019.4.40f1`（与 exe 版本资源一致），LocalCache bundle `2019.4.29f1`（StreamingAssets 的 bundle revision 被抹成 `0.0.0`）。身份取前者，hook 内解析 bundle 的 `ImportSettings` 版本用后者，**两者是不同事实，别互相覆盖**。

---

## 9. 恢复旧 AR 代码的危险清单

- `IMonoBehaviour.IsSceneObject()` 已移除 → `monoBehaviour.GameObjectP is not null`（ScriptableObject 的为 null）。
- 类型名冲突需 FQN：`AssetRipper.Processing.PrefabProcessor`（恢复的）vs `AssetRipper.Processing.Prefabs.PrefabProcessor`（当前内置）。
- `LibraryConfiguration` → `FullConfiguration`。
- 旧代码的 `: RipperHook`（那是**命名空间**不是类型）+ `AddExtraHook(...)` 都不存在 → 改 `: RipperHookCommon` + `[RipperHook(GameType.X)]` + `RegisterModule(new ExportHandlerHook())`（镜像 `StaticMeshSeparationHook`）。

---

## 10. Logger sink

`AssetRipper.Import.Logging.Logger` 是全局静态 + `List<ILogger>`，**没 sink 就什么都不做**。
- CLI：`HeadlessRunner` 接 `StderrLogger`+`FileLogger`。
- GUI：`Program.cs` 在 `Bootstrap.InstallAssemblyResolver()` 之后接 `ConsoleLogger()`；缺它则加载期所有 `Logger.Info` 静默，只有 hook 的 `Console.WriteLine` 漏出来。
- FModel GUI：`ConsoleLogSinkHook` 在 `App.OnStartup` 后重配 Serilog（FModel 只在 `#if DEBUG` 加 Console sink）。

---

## 11. 运行时类型树（`RuriTypeTree.tpk`）

游戏的 Unity 类型模型是**数据**不是生成 assembly：`Source/Ruri.RipperHook/Libraries/RuriTypeTree.tpk`（0.15 MB，`EmbeddedResource`），由 `Core.TypeTree` 运行时解释直接读进 stock `AssetRipper.SourceGenerated` 对象。（旧方案是跑 AssemblyDumper 60+ pass 生成 53 MB 孪生 DLL + 逐资产 deep-copy。）

| 部件 | 角色 |
|---|---|
| `Source/Ruri.Tpk` | 打包器：TypeTree JSON 输出目录 → `RuriTypeTree.tpk` |
| `Core.TypeTree` | 解释器：`TypeTreeDatabase` 载 tpk，`TypeTreeReadPlan` 为每个 (class, 引擎版本, AR 类型) 编译缓存读取计划 |

- **读取语义真源**（改之前必读）：`AssemblyDumper` 的 `Pass100_FillReadMethods`（节点分派/count-then-loop/`Capacity` 收缩/align 位置）、`Pass015_AddFields`（字段名=消毒后节点名）、`Pass002_RenameSubnodes`（只有两处影响字节：`ValidNameGenerator.GetValidFieldName`、`ChangeStringToUtf8String` 把 align 从内层 `Array` 抬到 string 节点）。
- **绑定**：AR 字段名 = 消毒后节点名（`m_SubMeshes`、`m_MeshMetrics_0_`），可见性 `internal`，引用型字段 ctor 已预分配 → 计划直接绑字段，`DynamicMethod` 访问器避免基元装箱。stock 类没有对应字段的节点按树结构消费掉字节。
- **打包**：`dotnet build Source/Ruri.Tpk/Ruri.Tpk.csproj -c Debug` 再跑 `…/0Bins/Ruri.Tpk/Debug/Ruri.Tpk.exe`（无参 ⇒ 输入 `D:\Ruri\Git\FractalTools\TypeTree\output`，输出 `Libraries/RuriTypeTree.tpk`）。迭代可用 `RURI_TYPE_TREE_TPK` 指向别的 tpk。

**tpk 表达不了的三类偏差**（Unity 类型树无条件，每节点必序列化）—— 都是 `[Since]` 竞争解析的 capability，禁在共享代码加游戏分支：

| attribute | 用途 | 例子 |
|---|---|---|
| `[TypeTreeNodeGate(classID, nodePath, Captures=[...])]` | 条件节点 | Endfield Mesh 的 `m_CompressedMesh` 仅在 `m_CollisionMeshBaked` 为假时写 |
| `[TypeTreeValueFix(classID, nodePath)]` | 值改写 | Endfield `m_MeshCompression==4` 归一成 0 |
| `[TypeTreePostRead(classID, Slot, Captures=[...])]` | 读后解码 | ACL 解压、`m_TOSData`→CRC32→`m_TOS`、shader blob 上提 |

stock 类无处安放的私有节点在 `Captures` 声明后捕获成 `TypeTreeValue`（标量/字节数组/序列/结构），由 gate 与 post-read 经 `TypeTreeReadContext` 取用；没声明的只消费字节。路径 = 从类根起、消毒后节点名以 `/` 连（`m_MuscleClip/m_Clip/m_Data/m_DenseClip/m_ACLArray`）。

**输入数据集**
- `D:\Ruri\Git\FractalTools\TypeTreeDumps` —— 官方 dump，1384 个版本，`InfoJson/<ver>.json`。规范真源。
- `D:\Ruri\Git\FractalTools\TypeTree` —— 分叉引擎树。文件夹按 `CustomEngineType` id 命名（`1`=Houkai、`2`=StarRail、`5`=Endfield），每个 `<gamever>/info.json`；`RazTreeConverter.py` → 扁平 `output/`（`{maj}.{min}.{build}x{id}`，`x`⇒`Experimental`，`TypeNumber`=引擎 id）+ 拷 `Common/*.json`。**`output/` 是产物且 gitignore；`Common/` + `1,2,5/` 才是真源 —— 「补全数据集」= 填 `Common/`。**
- `Core/CustomEngineType.cs` —— 引擎→id，存进版本 `TypeNumber`（byte ≤255）。

**版本模型**
- `UnityVersion` = `Major.Minor.Build` + `Type` + `TypeNumber`；真实 dump = `Final`/`Beta`，**自定义覆盖层 = `Experimental`**（可靠判别依据）。
- `Pass000_ProcessTpk`：`MinimumVersion=3.5.0`；`MakeVersionRedirectDictionary` 按相邻差异把版本吸附到边界；丢弃 ID 100000-100011 与 `129`。
- `TypeTreeTpkBuilder.Create`：版本排序；`CommonString` = 只追加、前缀一致的并集（索引不匹配就抛）；类只在 dump 变化时 emit（奇点压缩）；**类在某版本缺席 = null 标记 = 在此被移除**。
- `SharedState.GetGeneratedInstanceForObjectType`/`ClassGroupBase.GetInstanceForVersion` 做**精确**版本范围匹配，无覆盖即抛。

**自定义引擎是覆盖层不是快照** —— 分叉游戏在某基础 Unity 版本之上发布**部分**树。Endfield（id 5，基础 2021.3）是 ECS：发布叶子组件 + `MonoBehaviour(114)` 但**丢掉整条抽象链**（`GameObject(1)`/`Transform(4)`/`Component(2)`/`Behaviour(8)`/`Renderer`/`Collider`/`Joint`/`Effector2D` 等 ~15 个基类，被 100+ 叶子引用）。StarRail（id 2）在 2019.4.210+ 发布精简版 `UnityConnectSettings(310)`（6 字段）。

**`ArAssemblyDumperHook` 现存 4 个 hook**（原 6 个）：
- 已删 `Pass005.GetClass`（Endfield 断掉的祖先链 —— 覆盖层规则让 Experimental 版本不对省略的类打 null 标记后，祖先前向携带、基类精确解析）。
- 已删 `Pass555`（期望 113 个 common string，数据集顶到 112；加入 `6000.5.0a8`——1384 个 dump 里第一个有 113 个的——即满足）。
- **保留** `SharedState.GetGeneratedInstanceForObjectType` + `ClassGroupBase.GetTypeForVersion` 的最近版本回退：自定义子类按引擎不相交定义（StarRail `[2019.4.100,2020.0)`、Endfield `[2021.3.527,2022.0)`），中间是真实 Unity 空隙；`Pass015`→`GenericTypeResolver.ResolveNode`→`GetTypeForVersion`（还有 `Pass100/101`、`UniqueNameFactory`）会撞空隙抛 `No instance found`。**没有任何真实奇点版本能填上——那是自定义数据本身的洞。**
- **保留** 数据适配器 `Pass506` no-op（StarRail 精简的 310 没有 `m_CrashReportingSettings`/`m_UnityPurchasingSettings` 地标）、`Pass039` prune（doc 注入引用了不完整 dump 里缺失的 enum 成员）。

**最小 Common 集（8 文件 ~75 MB，保持它小）**：因回退容忍空隙，必需的只有每个自定义引擎的**基础**（`2017.4.0f1`/`2019.4.0f1`/`2021.3.0f1`）、**113-string 上限**（`6000.5.0a8`）、**下限+早期锚点**（`3.5.7`/`4.1.0`/`5.0.0f4`/`5.6.0b5`）。**禁重新加入中间 minor**（2018.x/2020.x/2022.x/6000.1-4）—— 回退下冗余，只拖慢生成。只有当某个非自定义游戏需要精确建模那个确切类型树时才加真实版本。

---

## 12. IL2CPP 原生方法反汇编（`Il2CppMethodDump`）

AR 经 `AssetRipper.Cpp2IL.Core` 把 `GameAssembly.dll` 变成**哑 assembly**（桩方法体），ILSpy 再反编译成 `ExportedProject/Assets/Scripts/**.cs`。本 hook 搭同一趟分析的车，把每个方法的**原生 x86/ARM 方法体**作 `//` 注释注入到匹配的 C# 方法体里。源：`AssetRipperHook/Il2CppMethodDump/`。

**模型来源**：`IL2CppManager.Initialize`（冻结）在加载期跑 `Cpp2IlApi.InitializeLibCpp2Il`，之后静态 `Cpp2IlApi.CurrentAppContext` 持有完整模型并**贯穿 export 存活**。GUI 每次加载经 `ClearStaticState` + 新 `InitializeLibCpp2Il` 重置。**禁在 DllPostExporter/哑 DLL 保存阶段 dump**（那写的是 `AuxiliaryFiles/GameAssemblies/` 的原始 DLL，不是用户读的 C#）。

**Hook 点**：`WholeProjectDecompiler.CreateDecompiler(DecompilerTypeSystem)`（`AssetRipper.ICSharpCode.Decompiler`，AR 分叉，版本不在 nuget.org）。AR 的 `CustomWholeProjectDecompiler` **没有** override 它，所以基方法会跑。用 `[RetargetMethodFunc]`：`ret` 前 `dup` 返回的 `CSharpDecompiler` 并 `call AddTransform`，把 `IAstTransform` 追加进 `decompiler.AstTransforms`（幂等）。**`AddTransform` 必须 `public static`** —— 注入的调用住在 ILSpy assembly 里。

**AST 变换**（`Il2CppAsmCommentTransform : IAstTransform`）：每个带体的 `EntityDeclaration` → `decl.GetSymbol() as IMethod` → 查反汇编 → `body.InsertChildBefore(firstStatement, …, Roles.Comment)` 逐行插 `Comment(line, SingleLine)`。三个坑：
1. 改树前先 `DescendantsAndSelf.OfType<…>().ToList()` 物化。
2. `GetSymbol()` 在 `ICSharpCode.Decompiler.CSharp` —— 缺那个 `using` 会解析到 `TypeSystemExtensions.GetSymbol(ResolveResult)` 编译失败。
3. **空方法体** `{ }`：ILSpy 把注释 emit 在 `}` **之后**，锚 `Roles.RBrace`/`LBrace` 都修不了 → 加一个 `EmptyStatement`（渲染成孤零零 `;`）再 `InsertChildBefore` 它。

**关联 ILSpy `IMethod` ↔ Cpp2IL `MethodAnalysisContext`**（`Il2CppAsmLookup`）：key = `CleanAssemblyName | Normalize(Type.FullName) :: Name / paramCount`；`Normalize` 把 `+ / \` → `.` 并剥掉泛型 arity `` `\d+ ``（ILSpy 的 `FullName` 两者都不带，Cpp2IL 带）。含 assembly + arity 故精确：测试游戏 Assembly-CSharp **3832/3832，0 漏**。查找非消耗+幂等，`CurrentAppContext` 变则重建。

**反汇编两条路**：
- **x86** → `Il2CppX86Listing.Render` 用 **Iced** 自解码 `method.RawBytes`（有每条指令 `IP`），收集方法内近跳转目标并 emit `loc_<IP>:` 标签；每条指令用本地 `MasmFormatter` + 挂 `Il2CppSymbolResolver`，再叠 `Il2CppRegisterFlow` 的符号注释。
- **其它（ARM/Disarm、WASM）** → `appContext.InstructionSet.PrintAssembly(method)` + `Il2CppAsmAnnotator.Annotate`（纯文本正则回退，无标签、无字段恢复）。
- 经 `app.InstructionSet is X86InstructionSet` 分支；`UnderlyingPointer == 0`（抽象/extern）逐方法跳过。

> **双 Iced 坑**：本项目引用两个暴露 `Iced.Intel` 的 assembly（真 `Iced` 经 Cpp2IL 传递、`MonoMod.Iced` 经 RuntimeDetour 传递）→ `using Iced.Intel;` 报 `CS0433`。修：显式 `<PackageReference Include="Iced" Version="1.21.0" Aliases="icedreal" />` + `extern alias icedreal; using icedreal::Iced.Intel;`，且该文件别 `using System.Text`（`Decoder`/`StringBuilder` 再冲突）→ 完全限定 `System.Text.StringBuilder`。
> **并发坑**：`X86InstructionSet.PrintAssembly` 用 static `MasmFormatter`/`StringOutput`，非线程安全，而 `WholeProjectDecompiler` 并行反编译 → 每次 `PrintAssembly` 串行化在一把锁下（持于 `Il2CppAsmLookup.GetDisassembly`）。

**符号解析**（`Il2CppAsmAnnotator.ResolveAddress`，x86 指令感知与 ARM 文本回退共用）：裸地址无意义，每个**地址操作数**就地换符号、不保留裸地址。x86 由 Iced 告知操作数种类：只解**分支/调用目标**与**绝对数据全局**；**立即数一律不碰**（旧正则曾把 `add eax,5E593F7Ah` 误标 `sub_`）；**寄存器相对位移**留给数据流恢复成字段。按顺序：

1. `appContext.MethodsByAddress[addr]` → 托管方法。
2. **PE 导出表**（反射 `LoadPeExportTable`+`GetExportedFunctions`，测试游戏 242 条）。
3. **关键函数**（反射 `GetOrCreateKeyFunctionAddresses()` 的 `ulong` 成员）—— `il2cpp_codegen_*` wrapper **不在导出表里**，这是它们唯一来源。
4. `GetLiteralByAddress` → 字符串字面量；`GetAnyGlobalByAddress` → `MetadataUsage`（TypeInfo/method/field global）。
5. 未命中 metadata 的地址按 **PE 段表**（`ParsePeSections` 解析各段 VA 范围+可执行/可写/是否落盘）分类：
   - **常量池解引用**：仅放行落在**只读且已落盘段**（`.rdata`）的地址 → `TryMapVirtualAddressToRaw`+`GetByteAtRawAddress` 读出**实际值**（`movss xmm0,[360f]`、整数 `[5h]`、`andps [{7FFFFFFFh x4}]`），作**最低优先级**传入。
   - **可执行段的括号地址**（代码指针/跳转表项）→ `loc_`（体内）/`sub_`（体外）。
   - **只读落盘段的 C 字符串**（`TryReadCString`：NUL 结尾、全可打印 ASCII、长度≥2）→ 引号字符串，救回 icall 签名 `lea rcx,["UnityEngine.Time::get_time()"]`、版本串等（**非托管字面量，`GetLiteralByAddress` 命不中**）。
   - **落盘数据槽里存着指向可执行段的指针** → `->目标符号`（`call qword ptr [->sub_1802178D0]`）。
   - 其余（运行期才填、文件里无值的 `.data`/`.bss`：icall 缓存/once-flag/TypeInfo 缓存）→ `g_XXXX`。**已实测其值不在文件、`GetAnyGlobalByAddress` 也命不中，是可用元数据的真实边界，绝不臆造。**
6. 两个最常见的匿名 global 由 `DetectMetadataInitIdiom` 升级：`cmp byte ptr [X],0 … mov byte ptr [X],1` → `method_init_flag`；`call il2cpp_codegen_initialize_method` 前压入的 token → `method_init_token`。`IsDirectMemoryOperand` 同时接受 32 位绝对 `[disp]` 与 64 位 RIP 相对（都以 Iced `MemoryDisplacement64` 的绝对地址为键）。守卫 `addr < 0x10000` 跳过寄存器相对偏移与 8 位寄存器名（`ah`/`bh`）。

**符号恢复（`Il2CppRegisterFlow` + `Il2CppTypeModel`）** —— 把元数据里**本就已知**的（字段偏移/静态类/返回类型）从裸偏移还原，而不是留 `[rcx+18h]`。每个 x86 方法一趟前向抽象解释：
- **播种**参数寄存器（镜像 Cpp2IL `X64CallingConventionResolver`：实例方法 `rcx=this`、`rdx/r8/r9` 前三整型参、浮点走 xmm 占同 slot、**>8 字节值类型返回**走隐藏返回指针使 `rcx`=返回缓冲且 `this`→`rdx`、尾随 `MethodInfo*`）。
- **基本块 meet 传播** `TrackedValue`（`ManagedRef`/`TypeInfo`/`StaticBase`/`Klass`），前驱一致则保留否则 Unknown。
- 恢复项：实例字段 `[rcx+18h] → ; this.groundDetector`（`FieldAnalysisContext.Offset` 反查，走继承链、含 0x10 对象头）；链式 `; this.a.b`；静态字段（`offsetof(Il2CppClass,static_fields)` **自动发现**=`0xB8`）；数组 `.Length`(+0x18)/`[i]`(+0x20+i*8) 并传播元素类型；调用返回类型；**虚/接口调用** `call [klass+N] → ; -> Type::VirtualMethod`（用该类型自己的 `VTable`，`offsetof(Il2CppClass,vtable)` **自动发现**=`0x150`，已对 `Object.Equals`=slot0 核对）；对象分配 `call il2cpp_codegen_object_new → ; rax = new T()`；`Il2CppClass` 结构读 → `; T::class[0xNN]`；泛型实例字段（`Unwrap` 到 `.GenericType`）；icall 惰性缓存槽 `[icall<UnityEngine.Time::get_time()>]`（`DetectIcallCacheIdiom`：近旁签名 C 字符串 + 同槽读写 once-cache 不变量）；**PE 镜像基址** `lea reg,[image_base]` 单列（Assembly-CSharp 约 47 处，不再误标 `g_<base>`）；`inc`/`dec [field]` → `; field++/--`。
- **一致性回撤 `RetractInconsistentArrows`**：元数据 `VTable` 槽序与运行期内存 vtable 对某些偏移不符会串名，`method.slot==i` 过滤挡不住。后处理按命名槽的**返回种类**（`ClassifyReturn`：Void/ScalarInt/ScalarFloat/Bool/Ref/Struct/Pointer）挑**绝不会命中正确名**的矛盾信号即撤名降级 `T::class[0xNN]`：void|标量的 `rax` 被解引用/整宽存/全 64 位捕获/当 `this`；float 名却 `movsd xmm,[rax]`；**xmm0 被读**（非 float 名）；**`test al,al`**（非 bool 名，真 bool 豁免）；**MethodInfo 载 r8/r9**（0 形参名却有整型实参）；引用结果存进不相关具体引用字段（`AreUnrelatedRefClasses` 保守）。结构/IntPtr 返回 rax 本是合法指针 → 不测（真结构 getter `get_position→Vector3` 名保留）。两种分派形态都认（`mov reg,[klass+disp]; call reg` 与直接 `call [klass+disp]`），连带撤配对的 MethodInfo 载入（`+8` 同槽）。**全局根治**：`CondemnedVtableSlots` + `EnsureCondemnationScan` 一次性预扫全 Assembly-CSharp 再渲染 → 某 (类型,槽) 被任一站点证伪即全方法一致撤（确定性，与渲染序无关）。`Object.*` 基槽误标 553→~281，余下无可观测矛盾（结果丢弃/尾 jmp）= 诚实极限。
- **编译器异常抛出助手**（`Il2CppHelperNamer`，`CodeLabel` 在 `sub_` 兜底前调用、按地址缓存）：il2cpp 为 `throw new XException(...)` 生成的助手无 global-metadata 身份，本会永留 `sub_`；但它把异常类型名作 C 字符串内嵌（`lea r8,["IndexOutOfRangeException"]`）并经 `object_new`/raise（`IsAllocOrRaiseFunction` 按关键函数**语义名子串**判，版本稳健）或尾调用共享构造器处置。反汇编目标一趟、读 bare `*Exception`/`*Error` 标识符（含空格的消息串排除）、**仅当 body 也 alloc/raise/尾调用时**才命名 `il2cpp_throw_<Type>`。全库扫：4973 个 `sub_` 助手、恰 3 个内嵌类型名，**全部命中，0 误报 0 漏报**；泛型/共享助手（类型来自运行期数据、无内嵌串）正确留 `sub_`。
- **回写失效用 Iced `InstructionInfoFactory` 精确算写寄存器集** + 调用点叠 ABI volatile 集 → 残留旧类型绝不误标后续访问（**错标比漏标更糟，宁缺毋滥**）。值类型字段偏移 0-based（`Vector3` x@0/y@4/z@8），引用类型含 0x10 头。
- **恢复天花板**（`_dumpprobe -- audit <seed> <N> [cs]` 随机抽样）：global-metadata 的所有符号种类已全恢复；候选 managed-field 命中率 ~52%，残留是 (a) il2cpp 运行期 C 结构访问、(b) 值类型/SIMD 批量拷贝/native 跳转表（本就非托管符号）、(c) 寄存器对象身份经任意计算丢失 —— **(c) 是静态数据流分析的固有极限**（需 SSA/值编号/过程间分析），非符号缺失。**栈槽跟踪试过 0 收益已回滚**（MSVC 用 callee-saved 寄存器存 this/局部，不 spill 到栈）。
- **残余上游边界**（非本 hook）：折叠 RVA（两方法共享一 VA，ASM 忠实但 C# 声明配错 = Cpp2IL 方法→RVA 分配）、`[StructLayout((LayoutKind)N)]` 枚举渲染、特性构造参数（v24 是 generator 函数）。

**迭代探针**（不跑完整 AR）：`Il2CppX86Listing`/`Il2CppAsmAnnotator`/`Il2CppTypeModel`/`Il2CppRegisterFlow`/`Il2CppSymbolResolver` **只依赖 Cpp2IL 模型** → 一个 `net10.0` 控制台直接 `<Compile Include>` 这五个真源 + `PackageReference AssetRipper.Cpp2IL.Core`/`Iced`(`Aliases="icedreal"`)，`InitializeLibCpp2Il` 后对挑出的 `MethodAnalysisContext` 调 `Il2CppX86Listing.Render(app, method)`，秒级看结果（改源即重编真源、非拷贝）。这是打磨符号恢复的主循环；ILSpy 探针只在验证 AST 注入接缝时才需要（`net9.0` 控制台复刻 `IL2CppManager` 静态 ctor → `DetermineUnityVersion` → `InitializeLibCpp2Il`，再 `new WholeProjectDecompiler` 子类 override `CreateDecompiler`，用文件夹 `IAssemblyResolver` 反编译一个哑 `Assembly-CSharp.dll`；经 HintPath 引 `ICSharpCode.Decompiler` 构建输出 DLL）。**PowerShell 5.1 反射不了这些包**（.NET Framework vs net9）。

---

## 13. CAB 虚拟文件 —— 名字索引 + bundle-granular 加载

把整个游戏当**一张 CAB 依赖图**按需取用，而不是把 21 GB 全载进内存。核心 `Core/CabMapping/{CabMap,CabTable,CabSelection}.cs` 是唯一实现，CLI/GUI（`Services/ExportCabMap.cs` 是薄门面）/Blender pythonnet 桥全部消费它。

**`<game>.cabmap`，RCM6 `0x52434D36`（唯一格式，自包含列式）**：列式 blob + 偏移表 —— CAB 名（**OrdinalIgnoreCase 排序**，查名二分、无字典）、**distinct chunk 文件表 + 每 CAB 一个 int 索引**（237k CAB 实际只落 ~40 个 chunk，按行存路径是 231× 冗余）、chunk 条目文件名、AssetBundle Container 可读寻址路径、ClassID[]、int 依赖图。
- **base 存绝对游戏根 → map 文件位置无关**（曾存相对 base，map 一被复制就静默重锚、种子归零）；游戏搬家 = 缓存失效，加载时 40 个 chunk 一个都探不到就响亮报错要求重建，绝不静默空结果。
- 加载 = 单趟顺序流读直入最终数组（无整文件中间缓冲），**顺手转置出反向邻接**（~3ms，`Dependents`/`ReverseClosureIds` 零构建成本）。
- `--build-cab-map` 生成：外层多 lane 并行流水 `.chk` × 内层每 bundle 一 worker，合并按目录枚举序确定性落盘。
- **可重建缓存**：格式一变即 bump magic 整体重建，**绝不写多格式兼容 reader**（旧 RCM2/3/4 与 `.names`/`--build-name-index` sidecar 已全删）。

**为什么名字进 map**：CAB map 全按内容 hash 索引，可读名只活在每个 bundle 的 **AssetBundle(142) 对象的 Container** 里，必须实际加载解析才看得到。Endfield 每个 CAB 100% 含一个 142 对象。合并扫描单趟把名字并进 map 本体。

**合并扫描（有界内存）**：`GameBundleHook.AssetBundleOnlyFactory` 只物化 ClassID 142、其它返 `null`，于是 `SerializedAssetCollection.FromSerializedFile`（反射调）只读那一个小对象，跳过 Mesh/AnimationClip/Texture。`ReadFullMetadata(sf, fileName)` = `ReadSerializedMetadata`（deps+ClassID）+ `ReadContainerNames`（条目名+Container 路径）单趟双投影，需 `NameScanVersion` 解析 142 的 source-gen 布局。`VirtualFileSystem.ScanChunk<T>(chkPath, project)` 是**单一**有界并行流式扫描器（逐 bundle 解密+解析+投影+即弃），`ScanChunkMetadata/Names/Full` 都是它的薄包装。258k CAB 全扫峰值 ~3.5 GB。

**★最关键的坑：chunk 条目文件名 ≠ CAB 名，无法互转。** 条目名 `fileInfo.fileName` = `Data/Bundles/Windows/<initial|main>/<24位hex>.ab`；CAB 名 = `cab-<32位hex>`，来自 bundle **内部目录**里 SerializedFile 的 `NameFixed`（=`SpecialFileNames.FixFileIdentifier(内部名)`，小写）。两套独立标识。**所以名字索引必须给每个 CAB 记录它的 chunk 条目文件名**（连 Container 为空的也记，否则 load 过滤拿不到条目名）。

**bundle-granular 加载（Endfield 必须，否则 OOM）**：AR 载一个 `.chk` 会经 `VirtualFileSystem.TryLoadChunkFiles → ExtractChunkFiles` 解出**该 chunk 全部 bundle**，而 Endfield 把 161k bundle 塞进单个 `.chk`（1.8 GB）—— 一个角色闭包只要几千个，整块加载在 13 GB 空闲下要 24 GB+。解法 = `GameBundleHook.LoadIncludeFile`（`Func<string,bool>?`，`null`=全载）：`ExtractChunkFiles` 用它在**解密前**按 chunk 条目名过滤。调用方把闭包每个 CAB 经名字索引映射回条目名组成集合，load 前置、`finally` 清。实测 pelica 闭包 4240 CAB 跨 20 chunk，big chunk 只取 ~2297/161113。

**解析层（`CabSelection`，唯一入口）**：谓词（`NamePatterns`/`ClassIds`/`FileScopes`/`SeedCabNames`）**AND 组合选种子** → 一次 `ClosureIds` 走 int 图 → `CabClosure` 同时给出待载 chunk 与 bundle 粒度过滤器。**谓词只约束种子、永不约束闭包**（种子的依赖可以合法跨目录，砍掉=导出断引用）。谓词扫描全核并行零物化：容器路径按 UTF-8 直接解码进池化 buffer 上 span 正则（**每分区克隆一份解释型 Regex** —— .NET Regex 内部只缓存一个 matcher，共享实例并发 `IsMatch` 会退化成逐调用分配风暴），文件 scope 折叠成 per-distinct-file 的 `bool[]`，`SeedCabNames` 走排序列二分。实测 237k CAB：单正则 200ms/144MB → **14ms/0.4MB**；`ResolveCabsForPaths` 反转成「查询集 HashSet + span AlternateLookup + 全表并行探测」374ms/140MB → **~15ms/0MB**。

**CLI**：`--cab-map <map> --names <regex>`（叠 `--hook Endfield_1.4.4`）→ 载 map → `CabSelection.Resolve` → 设 `LoadIncludeFile` → `handler.Load` → 导出**整个闭包**。
- **`--names`/`--load-types` 驱动时 `--load` 只是范围**：约束谁能当种子，不约束种子需要什么。
- **名字驱动时导出侧不再叠逐资产名过滤**（否则丢掉没带该名的依赖贴图/材质/网格）。`--names` 不配 `--cab-map` 时保持老语义。
- **回归口径**：`--names chr_0004_pelica_postmodel` 必须仍是 `2 seed → 66 CAB / 66 bundle → 12 chunk`，`loaded 3635 / exported 183`。

**GUI 预览 = Asset List 的另一种行**，不开新窗口（旧 `CabFileBrowser`/`AssetBrowser` 已删）。`assetListView` 是**单个虚拟模式 ListView 两种 backing**，`_listMode` ∈ {Assets, CabMap}。列 = Name/Container/Type/PathID/Source/Deps（**Size 列已删** —— CAB 无 size，顺带省掉每资产 YAML 序列化估算）。同一套搜索/类型过滤/排序/多选（**Ctrl+A 走 native `LVM_SETITEMSTATE` iItem=-1**，虚拟模式无托管全选）/右键菜单服务两种模式。
- 「Load CABMap」→ 载 map → `_allCabRows` 缓存 + `EnterCabMapMode()`。
- CabMap 模式：单选在 Preview 显示 CAB 信息（hash/source/deps/容器路径，无 3D）；右键「Load selected」=`LoadCabsScopedAsync`（载闭包→切 Assets 模式，append 跨多次累积 `_scopedLoadFilter`）、「Export with dependencies」=`ExportCabsWithDepsAsync`（`ResolveScopedClosure`→设 `LoadIncludeFile`→复用 `RunFilteredExportAsync`→完事回 `EnterCabMapMode()`）。
- Assets 模式右键：「Export selected」(Converted/YAML)、「Export with dependencies」（选中资产的源 CAB→闭包）。
- Scene tree 经 `_assetIndexByObjectKey`/`_nodeByObjectKey` 双向联动（虚拟模式无持久 AssetItem）。
- **名字缺失则做不了 bundle-granular**（条目名为空→`LoadIncludeFile` 留 null→整块加载→大游戏 OOM）；map 天生带每 CAB 的条目名，没这问题。

**导出格式 = 可重新导入的 Unity 工程**（`ExportedProject/Assets/…` 按原始寻址路径布局），不是 glTF：网格→`.asset`（`S_`静态/`SK_`骨骼）、prefab→`.prefab`、贴图→`.png`、材质→`.mat`、动画→`.anim` + `.state/.transition/.statemachine/.blendtree`。**别拿 `*.glb/*.fbx/*.mesh` 去找网格。** `HeadlessRunner` 默认 `ShaderExportMode.Decompile` —— 大闭包里上百个着色器逐个反编译是耗时与内存的主要尖峰。

---

## 14. GlbExporter —— prefab/Animator 完整 GLB 导出

`AssetRipperHook/GlbExporter/`（GameType 与 hook id 均为 `GlbExporter`）：替换 `GlbModelExporter.ExportModel`（PrimaryContent 路径）为 `RuriGlbSceneBuilder.Build` —— AR 原生 GLB 只有静态刚体网格。入口 = 选中的 prefab 或 Animator（GUI「Export selected (Converted)」经 `RipperPrimaryAssetExportService`；CLI `--export-glb <dir>` 自动启用）。**单 anim 不可用**：脱离 prefab/Avatar 无法还原 path_hash。

- **数据全是 AR 处理后的纯净模型**：曲线路径已被 `PathChecksumCache`（Avatar TOS + 层级 CRC32 反查）在 EditorFormatProcessor 阶段还原；muscle 曲线已被 `AnimationClipConverter` 命名成标准属性串。导出端零重新解码。
- **网格**：复用 AR internal `GlbSubMeshBuilder`（法线/切线/8UV/顶点色/Joints4/全拓扑 + 每 submesh 材质）。internal 访问 = csproj `InternalsAssemblyNames` + **`CompileUsingReferenceAssemblies=false`**（坑：SDK 对 ProjectReference 默认用 ref assembly 编译，会绕过 IgnoresAccessChecksToGenerator 重写的实现程序集 → CS0122）。
- **材质**：`RuriGlbMaterialCache` 镜像 AR GlbLevelBuilder 的私有材质半边（TextureConverter→PNG→MemoryImage，缓存），纹理属性名表数据驱动扩到 URP/游戏命名（`_BaseMap`/`_AlbedoMap`/…）。
- **morph**：blendshape 通道→glTF morph target（每通道取末帧），`extras.targetNames` 带名（Blender 直接出同名 shape key）；`blendShape.*` 曲线→morph 权重轨道（/100，按 SampleRate 采样）。
- **★SharpGLTF 骨架铁律：`AddSkinnedMesh` 要求骨架树内节点名唯一**（`IsValidArmature`；报错只有空消息 `ArgumentException (Parameter 'joints')`）。游戏层级必有重名 → 建树时全局唯一化（`_1` 后缀）。joint 重复、零缩放、根节点进 joints 都合法；**跨两棵根才非法**。蒙皮索引按 joint 数组、动画绑定按 Unity 路径字典，改名无损。
- **被 strip 的骨骼补全**：`SynthesizeMissingAvatarBones`（TOS 路径 + DefaultPose 本地 TRS，父先子后）从 Avatar 骨架长回 prefab 层级缺的骨骼 —— VibeStudio `ModelConverter.DeoptimizeTransformHierarchy` 的移植。

### humanoid 烘焙（`HumanoidSolver/`）

`AvatarMuscleReferential`（Avatar Axes：PreQ/PostQ/Sgn/Limit + TOS 路径 + 全 95 肌肉 DOF 表：55 身体含眼颚 + 40 手指经 `Hand.HandBoneIndex` finger-major）+ `HumanoidClipBaker`。Python (`RuriRipperImporter/humanoid_retarget.py`) 与 C# 两份实现同步。

**解码链只有两步**（反编译 `mecanim::animation::EvaluateAvatarEnd` 结构性证明：拷贝 pose → `TwistSolve` → `MergeHumanToAvatarPose`，**没有第三种机制**）：

1. **`FromAxes_2`（`kZYRoll`/`m_Type=1` 分支，25 根人体骨骼统一走）**：
   `tx,ty,tz = tan(angle/2)`，`q = normalize(1, tx, ty+tx·tz, tz-tx·ty)`（w,x,y,z）。
   该 `q` 经 `preQ * q * postQ⁻¹` **直接就是骨骼的绝对 local rotation** —— 不是 delta、不跟 rest 复合、不需要任何逐骨骼修正表。真值核对误差 0.000°。
   - **`preQ` 与 `postQ` 不是同一个东西**（同一骨骼可差 60°~280°），公式必须非对称。用对称共轭 `postQ*q*postQ⁻¹` 会让整条腿关节方向全反。
   - 旧的 `swing(Y,Z)*twist(X)` 组合轴角写法只在小角度近似成立，双轴同时非零时误差 5°~85°。
   - **四肢 twist 角度（DOF 0）额外 ×0.5**（`_HALF_TWIST_BONES`/`HalfTwistBones`）：`Left/Right {Upper,Lower}{Arm,Leg}` 真实旋转角是预测值的精确一半，躯干/头颈精确 1:1。这条**取代**了旧的 forearm twist 取反补丁（那是在错公式上下的错药）。
2. **`TwistSolve`**（`HumanFixTwist` 的唯一调用方）：8 对固定 `(parent, child, 系数)` **顺序固定** —— `(LeftLowerArm,LeftHand,m_ForeArmTwist)`、`(LeftUpperArm,LeftLowerArm,m_ArmTwist)`、右侧同构、腿部同构（`m_UpperLegTwist`/`m_LegTwist`），系数在 `Avatar.asset` 里（实测该 avatar 全为 0.5）。把 PARENT 自己的 twist 角按系数重新缩放，同时调整 CHILD 的 local 旋转以保持 CHILD **世界朝向不变**：`child_local_new = delta⁻¹ · child_local_old`（代数唯一解，不依赖 `SkeletonAlign` 的 SIMD 实现细节）。

**根运动**：`RootT`/`RootQ` **不是 Hips 自己的变换**，而是 Unity 内部根参考系（IDA 确认 `RetargetTo`/`HumanComputeBoneMassCenter`/`HumanComputeOrientation`/`HumanSetupAxes`）：
- `RootT` = 全身 25 根人体骨骼按 `m_HumanBoneMass` 加权的**质心**；`RootQ` = 由肩心/胯心构造的**朝向系**（`up=normalize(肩心-胯心)`、`right=normalize(肩宽+胯宽)`、`forward=cross(right,up)`）；两者都相对 Avatar rest pose 下同一套计算结果（`m_RootX`，`HumanSetupAxes` 只算一次）。
- **解 Hips**：搭「假设 Hips 在原点、单位旋转」的 provisional FK（其余骨骼用各自当帧绝对 local rotation），算出 provisional 质心与朝向系，联立：`R = RootQ @ m_RootX.q @ 朝向(prov)⁻¹`，`T = RootT − R @ 质心(prov)`。朝向系公式对 avatar 自己 rest pose 复算与序列化 `m_RootX.q` 误差 0.00002°。
- **运动提取开关**：`m_KeepOriginalPositionXZ`/`KeepOriginalPositionY`/`KeepOriginalOrientation`（`IMuscleClipInfo`，Python 侧 `m_AnimationClipSettings` 同名）。为 `false` 时该轴位移/yaw 本该提取到角色根节点、不属于 Hips；**没有独立 `MotionT`/`MotionQ` 曲线时 `RootT − MotionT` 退化成减 0，提取值会原样喂给 Hips → 位置单调发散**。修法：`body_transform()` 按三开关把该轴 RootT 分量清零（或 swing-twist 分解掉 RootQ 的 yaw），把清零那部分单独返回，由 `animation_builder.py`/`HumanoidClipBaker.BakeSynthesizedRootMotion` 烘到**骨架 OBJECT 自身**的 location/rotation_quaternion（而非任何 pose bone）。实测 Hips 位置误差 0.56(最大1.13)→**0.04**，旋转 4.4°→**2.5°**。
- `body_transform()` 返回的就是 Hips 最终绝对 local transform，**调用方不再乘 hips rest 朝向** —— 与其余骨骼同一套「直接用、不复合 rest」契约。

**残留**：`RightUpperArm` ~15°、`UpperArm_L`/`UpperLeg_L` 对参考 `.blend` ~85°/37°（这两根在 TwistSolve 8 对里**永远只当 parent**，故那轮修复对它们零增量）。已排除：固定每骨骼偏移（同骨骼 9 帧的偏移差 40°+，非常数）、循环相位未对齐（9×9 网格两两比对最低仍 75°~94°）、解码侧第三机制（已结构性排除）。**最可信未证假设**：参考 `.blend`（内嵌 `AutoSaveAnimUnity.py`，是流向 Unity 的 FBX 导出源头而非 clip 的下游产物，Rigify 骨架，还留有「删除烘焙隐藏帧」等历史补丁脚本）保存的动画可能不是这条 clip 的逐帧对应版本 —— 即问题在正向 FBX 导出/Unity 首次编码，不在本项目的解码。

> **教训（五轮事故同指一件事）**：数值自洽（对称/连续/无 NaN）、单骨骼吻合、乃至「扩大到 15/18 根验证通过」都不能证明 humanoid 解算正确。**取真值的脚本本身要被审计**（曾有前后两版脚本一版打印 `cur`、一版打印 `delta`，混在一张表里得出「某些骨骼需要修正」的伪结论）；**公式接进真实调用点后要重核**（曾实现「除以 rest 再由调用方乘回」，代数上恒等于什么都没做，单测却通过）；**引擎语义靠 IDA 反编译确认，不靠拟合候选公式**；**只测单轴永远测不出多轴耦合的 bug**。

**Endfield 事实**：全部角色 avatar 是 generic（名字带 `_genericAvatar`，无 Human）—— 身体动画 = 逐骨骼 generic 曲线，muscle 空间只有 Root/Motion 14 通道；humanoid 烘焙对 Endfield 正确休眠，将在真 Mecanim humanoid 数据上首次生效（届时核对 `HandBoneIndex[15]` finger-major 假设 —— 对抗审计置信 medium-high 的唯一遗留）。角色模型入口 = `…/prefabs/uimodels/chr_XXXX_<name>_uimodel.prefab`（1.3.3 共 29 个）。

**CLI**：`--export-glb <dir>`（配 `--cab-map`+`--names` 闭包加载；`--names` 同时过滤要写的 prefab；**绝不删目标目录**，只加文件）。`GlbBatchExporter` 遍历 `MainAsset is PrefabHierarchyObject`（**注意 FQN**：现行 AR 的 `Processing.Prefabs` 版，别撞恢复版旧类，§9）。

---

## 15. FModelHook —— UE 着色器反编译（无头优先）

`Source/Ruri.FModelHook` + `.CLI` + `.GUI`：把 UE `.ushaderbytecode` 归档反编译成带「用到它的材质球 + 材质符号」的 `.shader`。shader 内符号（UB 成员名/纹理名）真值矩阵在 [`Source/Ruri.ShaderDecompiler/UE_SYMBOL_SOURCES.md`](Source/Ruri.ShaderDecompiler/UE_SYMBOL_SOURCES.md)；本节只讲**材质链接**与入口。

- **唯一入口 = 无头 CLI**：`Ruri.FModelHook.CLI.exe --game-config <AppSettings.json> [--skip-global] [--list-archives] [--archive-filter <tok>] [--split-variants|--no-split-variants] [--export-only]`（`--headless` 已是默认可省）。直接从 AppSettings 解析（AES 动态 key + mappings + EGame）构造 CUE4Parse `DefaultFileProvider` 跑完整 export+decompile，**绝不 `new FModel.App()`**。流水线只依赖 `state.Provider`（`AbstractVfsFileProvider`），与 WPF view-model 解耦 —— 这是无头化的关键。旧 `AutoExport/` 已整删。代码 `Game/SBUE/Headless/`。`--list-archives` 挂载后只打印归档名+大小再退出（IoStore 归档是挂载后的虚拟条目，磁盘上没有 loose 文件）。
- **.usmap mappings 是材质符号的硬前置**：UE5 IoStore 材质包用 unversioned property 序列化，缺 mappings 时每个材质 `LoadPackage` 抛 `MappingException` → Pass030 提取 0 个 → 全退化 `UnknownMaterial`。扫描前必须 gate `Provider.MappingsContainer != null`（FModel 在 `MainWindow.OnLoaded` 的 `UpdateProvider` 之后才异步 `InitMappings`，「文件已挂载」先于 mappings 就绪 —— 这个竞态曾让全部材质提取失败）。
- **hash → 材质的桥**（都折进 `HashToMaterialsFromUnified`）：主桥 = 每材质内联 `FShaderMapBase.ResourceHash`（非 bShareCode 走 `Code.ResourceHash`），等于归档 `ShaderMapHashes`，对每个 cook 材质都在（headless 设 `ReadShaderMaps=true`）。**容器头 `FFilePackageStoreEntry.ShaderMapHashes`（Pass020）在 InfinityNikki/X6Game 上极稀疏**，单靠它 18–85% shader-map 退化 `UnknownMaterial`，**不能当主桥**。**`CookedShaderMapIdHash` 是另一个 ID 空间（`BaseMaterialId` 派生），IoStore 下绝不拿它匹配归档 hash**。Niagara 是第三条独立桥（Pass035 `FNiagaraShaderMap.ResourceHash`）。
- **Pass030 两层解析**：Tier1 = 完整 hash→材质桥，冷启动建一次并缓存；候选集 = 容器头 shader-map-owning 包（`PackageShaderMapHashes` keys）∪ `M_/MI_/MF_/MPC_/MAT_` 前缀材质（**禁**用旧的 `path.Contains("Material")` —— 会膨胀到 157k 贴图/曲线；前缀也有 11 万但只 ~715 个真有 inline map），逐包 `LoadPackage` 读 inline ResourceHash 后**立即丢弃**（`LoadPackage` 不缓存 → 内存有界 ~2.7GB），存顶层 `MaterialResourceHashes`（**每 hash 封顶 16**，一个 shader-map 常被几十个 MI 共享）。Tier2 = 归档级富提取，**每 hash 只加载 1 个代表**做 UES/RenderState（共享同一父级 UES）。冷扫 ~5–8min 一次性。
- **并行反序列化竞态（关键坑）**：CUE4Parse `UMaterial.Deserialize` 把 inline shader-map 反序列化包在 try/catch，**并行 `LoadPackage` 下偶发异常被吞 → `LoadedShaderMap` 静默置空 → 材质从桥漏掉，桥变非确定性**（bridge-hash 逐次漂移、整归档材质可能消失）。修：容器头子集的空包**单线程重试**；前缀空包不重试（多为继承父级的真空 MI，11 万重试太贵）。
- **黑洞缓存**：Pass005 会话开头从上次 `UnifiedShaderMetadata.json` **流式只读**需要的几段（`MaterialResourceHashes` 桥 + 已 enrich 材质 + Niagara 桥，跳过重型 `ShaderCodeArchives`），于是 Tier1 全桥扫描 + Pass035 只冷启动跑一次、暖启动 ~200ms。失效守卫：`CacheFormatVersion`（提取形状变了就 bump，当前 7）+ `GameVersionEnum` + `MaterialScanComplete`。**坑：暖路径信任缓存时必须同时设 `state.Root.MaterialScanComplete=true`（不只 `state.MaterialScanComplete`），否则 Pass080 写回 false → 下次又冷建整桥。** Tier1/Pass035 都是 8 路并行 `LoadPackage`。
- **native 依赖全来自 NuGet**，build 还原到 `<bin>/runtimes/<rid>/native/`（`dxil-spirv-c-shared.dll`←`AssetRipper.Bindings.DxilSpirV`，内建 dxbc-spirv 直译 SM5 DXBC；`spirv-cross.dll`←`Silk.NET.SPIRV.Cross.Native`）。`NativeToolsLoader` 优先探 `runtimes/<rid>/native` 再回退旧 `Tools/`。⚠ **绝不再往 `<bin>/Tools/` 拷旧 native** —— 残留的过期 dxil-only `dxil-spirv-c-shared.dll` 会遮蔽 NuGet 的 dxbc-spirv 版，把 DXBC 报成 `dxil_spv_parse_dxil_blob failed (-4)`（1958→129 退化的根因）。真出 `DllNotFoundException` 就删 obj+bin 重建。

## 16. Unreal lane —— UE 资产在内存里直接成为 Unity 资产

UE 是 `GameType.UnrealEngine` 这一个解码器(`UnrealEngine_4.0`,按**引擎家族**认领:没有自己解码器的 UE 标题都走它),和 Endfield 一样进 cabmap→浏览→内存导入,只是容器是 CUE4Parse 的提供器、对象在 `GameBundleHook.CustomFilePreInitialize` 里被换成 AR 资产,后面的流水线不知道数据不是 Unity 的。

- **依赖方向(钦定)**:内核 `Ruri.RipperHook` **不引用 CUE4Parse**;解码器整棵住 `Source/Ruri.FModelHook/Ripper/`(`Ruri.FModelHook.Ripper[.Converters|.TypeTree]`),FModelHook 引用 RipperHook。宿主按路径加载它:CLI `--module <dll>`、GUI `RuriRipperHook.json` 的 `Modules`、Blender 首选项 `Decoder Module DLL`;都汇到 `Bootstrap.LoadModule`(模块目录探测托管与原生依赖,`HookCatalog` 见到新程序集即失效重扫)。`HookCatalog.DeclareHost(属性类型)` 让每个宿主只列自己家族的解码器——同一个 FModelHook.dll 里 FModel 的 `UE_ShaderDecompiler` 与 RipperHook 的 `UnrealEngine_4.0` 互不串台。模块在 FModel 的 bin(`FModel/FModel/bin/<配置>/net10.0-windows/win-x64/Ruri.FModelHook.dll`,`CUE4Parse-Natives.dll` 放旁边);CI 的 FModelHook job 因此也 init AssetRipper 并带 `-p:PureRelease=true`。
- **身份与选项**:`[RipperInstallProbe]` 静态探针读 exe 版本资源与 DefaultGame.ini,报 `PlayerIdentity.Engine="UnrealEngine"`;解码器解析先按 product 再按引擎家族。读取参数(AES/EGame/贴图平台/usmap/版本覆盖/额外 pak 目录/`unreal.codecs`)= 源选项,`--source-option name=value` / Blender 表单;解码器发 `unreal.settings.schema` 数据集,面板按它画,**代码不认识任何选项名**。
- **数据流**:cabmap 一行=一个包(CAB 名=包路径,容器路径 `Assets/<包路径>`),依赖零解析取自 IoStore 容器头 `ImportedPackages`,ClassIds=导出类名经转换器表(`UnrealConverters.All`,按 usmap 父链匹配)映射。加载:`UnrealPackageLoader` 两道并行屏障——所有包先 Allocate(造空 AR 资产入 `UnrealAssetTable`,键=`GetPathName()`+槽位),再 Fill(引用一律可解),fileStack 留空。转换器:StaticMesh/SkeletalMesh/Skeleton/Texture/Material/AnimSequence/World,其余一切 → PropertyBag(MonoBehaviour+按类名的 MonoScript)。引擎中立建造器在 `Core/Conversion/`(Mesh 14 通道 Float32 单流,2019+ 蒙皮权重走通道 12/13;Clip 写编辑器曲线;Texture 行翻转;Material=Dummy Shader+SavedProperties,贴图属性声明 2D;PropertyBag 结构来自类型树)。坐标:Unity=(UE.y, UE.z, UE.x)/100,四元数同置换,UV v'=1-v,绕序按法线表决。
- **类型树**:usmap → `UsmapTypeTreeBuilder`(与 Ruri.Tpk 链接同一份源)→ 运行期注册 lineage `"6"`(`CustomEngineType.UnrealEngine`),类名索引。⚠ 运行期把 tpk 的 `string` 改名成 `Utf8String`/`PropertyName`,喂 AR 的 `TypeTreeNodeStruct` 必须用 `TypeTreeNode.UnityTypeName`,否则每个字符串字段都被 AR 当成结构。`UnrealValueWriter` 按**字段形状**写值(指针/结构/字符串/数值),形状不合 ReportOnce,不抛。
- **usmap 从哪来**:`Ruri.Tpk --unreal-reflection <game exe> [out.usmap]`——exe 旁有 pdb 就够:AsmResolver 读 PE+PDB,按 `Z_Construct_U*_Statics::{Class,Struct,Enum}Params` 公开符号定位静态反射数据,布局/位域/枚举值全部读自 PDB 类型记录,重放 `ConstructFProperty` 的反向走法;类名=IMPLEMENT_CLASS 去前缀,enum 照 `SetEnums` 补 `_MAX`,内建类父级读 PDB 里的 `Super` typedef,数据库没记录的 legacy `UProperty` 家族显式列出并省略。Titan:6.5s 出 10,524 structs / 2,166 enums。`Ruri.Tpk --unreal <usmap>` 再把它直出为自定义引擎 tpk。别再注入 UnrealMappingsDumper——DebugGame 会崩。
- **动画**:采样率是序列自带的 `PlatformTargetFrameRate.Default`(引擎 `GetSamplingFrameRate` 的定义;cooked 数据没有编辑器侧的 NumberOfSampledKeys),帧数是编码数据的键数;Titan 全部 1920fps。`unreal.animation.samplerate` 可重采样(0=源率)。写曲线前按**源编码精度**做关键帧约减(`Core/Conversion/CurveReducer`:RDP,误差按 Unity 真会播的 Hermite 曲线量,切线取自稠密采样):ACL 编码的 `ErrorThreshold`/`DefaultVirtualVertexDistance` 就是位移/角度容差(cooked 未写=类缺省 0.01cm/3cm),非 ACL 编码保留全部采样。一条 5.3s 的 1920fps clip 由 292MB 降到 21MB,时长与采样率不变。
- **类型树构建**:10,524 类原本 10s,现 1.3s——继承字段列表按父链共享、节点与字符串在 blob 自己的线性查重前面加字典备忘、blob 上限 8192 节点(运行期按多 blob lineage 注册,查类跨 blob)。
- **Blender**:首选项 `Decoder Module DLL` 指向 FModelHook.dll;`Game/UnrealEngine` 模块按引擎家族认领,源选项表单从 `unreal.settings.schema` 画,`Unreal` 页签显示挂载会话。GUI 探针口径:面板要真 draw 得把 `bl_category` 临时改到侧栏当前页签(`Region.active_panel_category` 只读),draw 用包装计数。
- **验收命令**:`Ruri.RipperHook.CLI.exe --module <FModelHook.dll> --hook UnrealEngine_4.0 --load <游戏根> --build-cab-map <x.cabmap>`;导出:同前缀 + `--cab-map <x.cabmap> --names <子串> --export <目录> --source-option unreal.mappings=<usmap>`。Titan 实测:SkySphere(网格/prefab/材质含 3 张贴图)、SKM_Glider 闭包 87 包 0 失败、LI_SmallBuilding 关卡 244 包 103 个 actor 落进 `.unity` 场景、Backflip 动画经 ACL 解码约减;Blender 面板全链路(识别→选项→建图→导入)通过。全量导出按文件夹分批(32GB 机器上每 ~1000 包闭包峰值 ~10GB),Release 构建约 1000 包/45s。
