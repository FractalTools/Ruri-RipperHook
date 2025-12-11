using System.Reflection;
using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Assets.Metadata;
using AssetRipper.IO.Endian;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using MonoMod.Cil;

namespace Ruri.RipperHook;

public abstract class RipperHook
{
    // 定义支持 ref 参数的委托
    public delegate void ReadReleaseDelegate(object asset, ref EndianSpanReader reader);

    protected List<string> additionalNamespaces = new();
    protected List<string> excludedNamespaces = new();

    // [Fix] 使用自定义委托替代 Action<object, ref EndianSpanReader>
    private static readonly Dictionary<Type, (MethodInfo? CreateMethod, UnityVersion TargetVersion, ReadReleaseDelegate? Callback)> _genericHookCache = new();

    protected RipperHook()
    {
        InitAttributeHook();
    }

    protected virtual void AddExtraHook(string nameSpace, Action action)
    {
        additionalNamespaces.Add(nameSpace);
        action();
    }

    protected virtual void InitAttributeHook()
    {
        var bindingFlags = ReflectionExtensions.AnyBindFlag();
        var namespacesToConsider = new List<string> { GetType().Namespace };
        namespacesToConsider.AddRange(additionalNamespaces); // 添加一些通用空间 避免写编写重复代码
        namespacesToConsider = namespacesToConsider.Where(ns => !excludedNamespaces.Contains(ns)).ToList(); // 排除继承等情况导致重复的hook

        var assembly = this.GetType().Assembly; // 要用this获取真实类型
        var types = assembly.GetTypes();

        // 包括处理嵌套类
        var allTypes = types.Concat(types.SelectMany(t => t.GetNestedTypes(bindingFlags)));
        var methods = allTypes
            .Where(t => t.Namespace != null && namespacesToConsider.Any(ns => t.Namespace.StartsWith(ns)))
            .SelectMany(t => t.GetMethods(bindingFlags));

        // 方法转发处理
        var targetMethods = methods.Where(m => m.GetCustomAttributes<RetargetMethodAttribute>(true).Any());
        foreach (var methodDest in targetMethods)
        {
            var attrs = methodDest.GetCustomAttributes<RetargetMethodAttribute>().ToArray();
            foreach (var attr in attrs)
            {
                MethodInfo? methodSrc;
                if (attr.MethodParameters == null)
                {
                    methodSrc = attr.SourceType.GetMethod(attr.SourceMethodName, bindingFlags);
                }
                else
                {
                    methodSrc = attr.SourceType.GetMethod(attr.SourceMethodName, bindingFlags, attr.MethodParameters);
                }
                int srcParameterCount = methodSrc.GetParameters().Length;
                int destParameterCount = methodDest.GetParameters().Length;
                if (srcParameterCount != destParameterCount)
                {
                    throw new Exception("Hook函数和目标函数参数数量不一致 如果是静态方法 看括号内参数数量 如果是实例方法 需要+1 因为this始终在实例方法中传递");
                }
                if (methodSrc.IsStatic)
                    srcParameterCount--;

                ReflectionExtensions.RetargetCall(methodSrc, methodDest, srcParameterCount, attr.IsBefore, attr.IsReturn);
            }
        }
        // 字节码插入处理
        var targetFuncMethods = methods.Where(m => m.GetCustomAttributes<RetargetMethodFuncAttribute>(true).Any());
        foreach (var methodDest in targetFuncMethods)
        {
            var attrs = methodDest.GetCustomAttributes<RetargetMethodFuncAttribute>().ToArray();
            foreach (var attr in attrs)
            {
                MethodInfo? methodSrc;
                if (attr.MethodParameters == null)
                {
                    methodSrc = attr.SourceType.GetMethod(attr.SourceMethodName, bindingFlags);
                }
                else
                {
                    methodSrc = attr.SourceType.GetMethod(attr.SourceMethodName, bindingFlags, attr.MethodParameters);
                }

                var HookCallback = (Func<ILContext, bool>)Delegate.CreateDelegate(typeof(Func<ILContext, bool>), methodDest);
                ReflectionExtensions.RetargetCallFunc(HookCallback, methodSrc);
            }
        }
        // 字节码插入处理 构造函数版
        var targetCtorFuncMethods = methods.Where(m => m.GetCustomAttributes<RetargetMethodCtorFuncAttribute>(true).Any());
        foreach (var methodDest in targetCtorFuncMethods)
        {
            var attrs = methodDest.GetCustomAttributes<RetargetMethodCtorFuncAttribute>().ToArray();
            foreach (var attr in attrs)
            {
                ConstructorInfo? methodSrc;
                if (attr.MethodParameters == null)
                {
                    methodSrc = attr.SourceType.GetConstructor(Type.EmptyTypes);
                }
                else
                {
                    methodSrc = attr.SourceType.GetConstructor(bindingFlags, attr.MethodParameters);
                }

                var HookCallback = (Func<ILContext, bool>)Delegate.CreateDelegate(typeof(Func<ILContext, bool>), methodDest);
                ReflectionExtensions.RetargetCallCtorFunc(HookCallback, methodSrc);
            }
        }
    }

    /// <summary>
    /// 通用 Class Hook 注册方法
    /// </summary>
    /// <param name="classIds">需要 Hook 的 ClassID 列表</param>
    /// <param name="sourceUnityVersion">原始 Unity 版本（用于定位 AssetRipper 原始类）</param>
    /// <param name="targetVersion">自定义引擎的目标版本（用于创建 Ruri 生成的类）</param>
    /// <param name="generatedAssemblyNamespace">Ruri.SourceGenerated 的命名空间前缀</param>
    /// <param name="customCallbacks">可选：自定义回调字典 (ClassID -> Callback)。Callback 替代默认的 Copy 逻辑。</param>
    protected void HookClasses(
        IEnumerable<ClassIDType> classIds,
        string sourceUnityVersion,
        UnityVersion targetVersion,
        string generatedAssemblyNamespace = "Ruri.SourceGenerated",
        Dictionary<ClassIDType, ReadReleaseDelegate>? customCallbacks = null)
    {
        var unityVersion = UnityVersion.Parse(sourceUnityVersion);
        var universalDestMethod = typeof(RipperHook).GetMethod(nameof(Universal_ReadRelease), BindingFlags.NonPublic | BindingFlags.Static);
        if (universalDestMethod == null) throw new Exception("Universal_ReadRelease method missing!");

        Assembly? ruriAssembly = null;
        try
        {
            // 直接加载程序集，不使用 AppDomain 查找，因为项目引用了它
            ruriAssembly = Assembly.Load(generatedAssemblyNamespace);
        }
        catch
        {
            Console.WriteLine($"[RipperHook] Warning: Could not load assembly {generatedAssemblyNamespace}. Class hooks may fail.");
        }

        // 获取 AssetRipper.SourceGenerated 程序集，用于后续查找 Original Type
        Assembly assetRipperGeneratedAsm = typeof(ClassIDType).Assembly;

        int count = 0;
        foreach (var classId in classIds)
        {
            try
            {
                // 1. 查找原始 AssetRipper 类
                // GetSourceTypeFullName 返回的是全名（不带程序集），所以直接 Type.GetType 找不到
                // 必须在 AssetRipper.SourceGenerated 程序集中查找
                string sourceTypeName = RetargetMethodAttribute.GetSourceTypeFullName(classId, unityVersion);
                Type? sourceType = assetRipperGeneratedAsm.GetType(sourceTypeName);

                if (sourceType == null)
                {
                    Console.WriteLine($"[RipperHook] Warning: Could not find original type for {classId} ({sourceTypeName}) in {assetRipperGeneratedAsm.FullName}");
                    continue;
                }

                // 2. 查找 Ruri 生成的类
                // 命名规则: Ruri.SourceGenerated.Classes.ClassID_{int}.{EnumName}
                string ruriTypeName = $"{generatedAssemblyNamespace}.Classes.ClassID_{(int)classId}.{classId}";
                Type? ruriType = ruriAssembly?.GetType(ruriTypeName);

                // 获取 Create 方法
                MethodInfo? createMethod = null;
                if (ruriType != null)
                {
                    createMethod = ruriType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(AssetInfo), typeof(UnityVersion) }, null);
                }

                // 3. 检查是否有自定义回调
                ReadReleaseDelegate? callback = null;
                if (customCallbacks != null && customCallbacks.TryGetValue(classId, out var customAction))
                {
                    callback = customAction;
                }

                // 如果没有 Create 方法也没有回调，说明这个类在 Ruri 中没生成，也没自定义逻辑，无法 Hook
                if (createMethod == null && callback == null)
                {
                    continue;
                }

                // 4. 注册缓存
                _genericHookCache[sourceType] = (createMethod, targetVersion, callback);

                // 5. 执行 Hook
                // 目标是原类的 ReadRelease
                var readReleaseMethod = sourceType.GetMethod("ReadRelease", BindingFlags.Public | BindingFlags.Instance);
                if (readReleaseMethod == null)
                {
                    Console.WriteLine($"[RipperHook] Warning: ReadRelease not found in {sourceType.Name}");
                    continue;
                }

                ReflectionExtensions.RetargetCall(readReleaseMethod, universalDestMethod, 1, true, true);
                count++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RipperHook] Error hooking {classId}: {ex.Message}");
            }
        }
        Console.WriteLine($"[RipperHook] Generic Class Hook: Successfully hooked {count} classes.");
    }

    // 通用 Hook 目标静态方法
    private static void Universal_ReadRelease(object asset, ref EndianSpanReader reader)
    {
        var type = asset.GetType();

        if (!_genericHookCache.TryGetValue(type, out var cache))
        {
            throw new InvalidOperationException($"[RipperHook] Generic hook called for unregistered type {type.FullName}");
        }

        // 优先执行自定义回调（如果有）
        if (cache.Callback != null)
        {
            cache.Callback(asset, ref reader);
            return;
        }

        // 标准流程：Create Dummy -> Read -> Copy
        var realThis = (IUnityObjectBase)asset;

        if (cache.CreateMethod == null)
            throw new InvalidOperationException($"[RipperHook] Create method is null for {type.Name} and no callback provided.");

        // 1. 创建 Dummy 对象
        var dummyThis = (IUnityObjectBase)cache.CreateMethod.Invoke(null, new object[] { realThis.AssetInfo, cache.TargetVersion })!;

        // 2. 读取数据 (调用 Ruri 生成的 ReadRelease)
        dummyThis.ReadRelease(ref reader);

        // 3. 深度拷贝回原对象
        ReflectionExtensions.ClassCopy(dummyThis, realThis);
    }

    protected void SetPrivateField(Type type, string name, object newValue)
    {
        type.GetField(name, ReflectionExtensions.PrivateInstanceBindFlag()).SetValue(this, newValue);
    }

    protected object GetPrivateField(Type type, string name)
    {
        return type.GetField(name, ReflectionExtensions.PrivateInstanceBindFlag()).GetValue(this);
    }

    protected void SetAssetListField<T>(Type type, string name, ref EndianSpanReader reader, bool isAlign = true) where T : UnityAssetBase, new()
    {
        var field = type.GetField(name, ReflectionExtensions.PrivateInstanceBindFlag());

        var fieldType = field.FieldType;
        var filedObj = Activator.CreateInstance(fieldType);
        if (isAlign)
            ((AssetList<T>)filedObj).ReadRelease_ArrayAlign_Asset(ref reader);
        else
            ((AssetList<T>)filedObj).ReadRelease_Array_Asset(ref reader);

        field.SetValue(this, filedObj);
    }
}