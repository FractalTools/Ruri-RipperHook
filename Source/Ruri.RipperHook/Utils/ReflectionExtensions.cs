using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Ruri.RipperHook;

public static class ReflectionExtensions
{
    #region 通用判断

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindingFlags AnyBindFlag()
    {
        return BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindingFlags PublicInstanceBindFlag()
    {
        return BindingFlags.Public | BindingFlags.Instance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindingFlags PrivateInstanceBindFlag()
    {
        return BindingFlags.NonPublic | BindingFlags.Instance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindingFlags PublicStaticBindFlag()
    {
        return BindingFlags.Public | BindingFlags.Static;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindingFlags PrivateStaticBindFlag()
    {
        return BindingFlags.NonPublic | BindingFlags.Static;
    }

    #endregion

    #region 方法反射

    public static void RetargetCallFunc(Func<ILContext, bool> func, MethodInfo srcMethod)
    {
        var hookDest = new ILContext.Manipulator(il =>
        {
            if (!func(il))
                throw new Exception($"Hook {srcMethod.DeclaringType.Name}.{srcMethod.Name} Fail");
        });
        RuriRuntimeHook.ilHooks.Add(new ILHook(srcMethod, hookDest));
        Console.WriteLine($"Created Hook of {srcMethod.DeclaringType.Name}.{srcMethod.Name} Success");
    }

    public static void RetargetCallCtorFunc(Func<ILContext, bool> func, ConstructorInfo srcMethod)
    {
        var hookDest = new ILContext.Manipulator(il =>
        {
            if (!func(il))
                throw new Exception($"Hook {srcMethod.DeclaringType.Name}.{srcMethod.Name} Fail");
        });
        RuriRuntimeHook.ilHooks.Add(new ILHook(srcMethod, hookDest));
        Console.WriteLine($"Created Hook of {srcMethod.DeclaringType.Name}.{srcMethod.Name} Success");
    }

    /// <summary>
    /// 默认的情况下是从起点插入并直接返回(替换原函数)
    /// isBefore可以选择从前后插入代码
    /// isReturn可以选择不返回 也就是继续执行原函数之后的代码
    /// </summary>
    /// <param name="srcMethod"></param>
    /// <param name="targetMethod"></param>
    /// <param name="maxArgIndex"></param>
    /// <param name="isBefore"></param>
    /// <param name="isReturn"></param>
    public static void RetargetCall(MethodInfo srcMethod, MethodInfo targetMethod, int maxArgIndex = 1, bool isBefore = true, bool isReturn = true)
    {
        var hookDest = new ILContext.Manipulator(il =>
        {
            var ilCursor = new ILCursor(il);
            Action inject = () =>
            {
                for (var i = 0; i <= maxArgIndex; i++)
                {
                    switch (i)
                    {
                        case 0:
                            ilCursor.Emit(OpCodes.Ldarg_0);
                            continue;
                        case 1:
                            ilCursor.Emit(OpCodes.Ldarg_1);
                            continue;
                        case 2:
                            ilCursor.Emit(OpCodes.Ldarg_2);
                            continue;
                        case 3:
                            ilCursor.Emit(OpCodes.Ldarg_3);
                            continue;
                        default:
                            ilCursor.Emit(OpCodes.Ldarg, i);
                            continue;
                    }
                }
                ilCursor.Emit(OpCodes.Call, targetMethod);
                if (isReturn)
                {
                    ilCursor.Emit(OpCodes.Ret);
                }
                ilCursor.SearchTarget = SearchTarget.Next; // 保证插入后从当前位置继续查找避免死循环
            };

            if (!isBefore) // 从起点注入还是末尾注入
                while (ilCursor.TryGotoNext(MoveType.Before, instr => instr.OpCode == OpCodes.Ret))
                    inject();
            else
                inject();
        });

        RuriRuntimeHook.ilHooks.Add(new ILHook(srcMethod, hookDest));
        Console.WriteLine($"Created Hook of {srcMethod.DeclaringType.Name}.{srcMethod.Name} Success");
    }

    #endregion

    #region 深度对象拷贝 (Deep Duck Copy)

    // 缓存类型的字段信息，避免重复反射
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> _fieldCache = new();

    // 简单的引用相等比较器，用于处理循环引用
    private class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        public static readonly ReferenceEqualityComparer Instance = new();
    }

    /// <summary>
    /// 顶配版深度复制：将 src 的所有字段递归复制到 dst。
    /// 支持跨程序集同名类（Duck Typing）、集合（List/Array/Dict）转换、循环引用处理。
    /// </summary>
    public static void ClassDeepCopy(object src, object dst)
    {
        if (src == null) throw new ArgumentNullException(nameof(src));
        if (dst == null) throw new ArgumentNullException(nameof(dst));

        // 上下文用于追踪已复制的对象，防止循环引用导致栈溢出
        // Key: 源对象, Value: 目标对象
        var context = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        // 注册根对象映射
        context[src] = dst;

        // 开始递归填充
        DeepCopyFields(src, dst, context);
    }

    /// <summary>
    /// 递归复制核心逻辑
    /// </summary>
    private static object DeepCopy(object srcObj, Type targetType, Dictionary<object, object> context)
    {
        if (srcObj == null) return null;

        // 1. 如果源对象已经在上下文中（处理循环引用），直接返回对应的目标对象
        if (context.TryGetValue(srcObj, out var existingDst))
            return existingDst;

        var srcType = srcObj.GetType();

        // 2. 类型兼容性检查与直接返回
        // 如果目标类型可以直接接收源类型（例如 string, int, 或者是同一个类的实例），
        // 且不是集合（集合需要特殊处理泛型转换），则直接返回。
        // 注意：这里为了深度克隆的效果，如果不是值类型或字符串，我们倾向于继续深度复制，
        // 但如果类型完全一致且没有特殊结构，直接赋值通常也是安全的。
        // 考虑到你的需求是“跨程序集同名类”，它们类型通常不兼容，所以会跳过这一步进入下方逻辑。
        if (targetType.IsAssignableFrom(srcType) && srcType.IsValueType)
            return srcObj; // 值类型直接拷贝
        if (srcType == typeof(string))
            return srcObj; // 字符串不可变，直接拷贝

        // 3. 处理数组
        if (srcType.IsArray && targetType.IsArray)
        {
            var srcArray = (Array)srcObj;
            var elementType = targetType.GetElementType()!;
            var length = srcArray.Length;
            var dstArray = Array.CreateInstance(elementType, length);

            context[srcObj] = dstArray; // 注册引用

            for (var i = 0; i < length; i++)
            {
                var srcVal = srcArray.GetValue(i);
                var dstVal = DeepCopy(srcVal, elementType, context);
                dstArray.SetValue(dstVal, i);
            }
            return dstArray;
        }

        // 4. 处理 IList (包括 List<T>, AssetList<T> 等)
        // 只要源和目标都实现了 IList 接口，就可以进行元素转换
        if (typeof(IList).IsAssignableFrom(srcType) && typeof(IList).IsAssignableFrom(targetType))
        {
            // 尝试创建目标集合实例
            var dstList = (IList)CreateInstance(targetType);
            context[srcObj] = dstList;

            var srcList = (IList)srcObj;

            // 获取目标集合的元素类型（泛型参数）
            // 如果是 List<T>，取 T；如果是 ArrayList，取 object
            var dstItemType = targetType.IsGenericType
                ? targetType.GetGenericArguments()[0]
                : typeof(object);

            foreach (var item in srcList)
            {
                var convertedItem = DeepCopy(item, dstItemType, context);
                dstList.Add(convertedItem);
            }
            return dstList;
        }

        // 5. 处理 IDictionary
        if (typeof(IDictionary).IsAssignableFrom(srcType) && typeof(IDictionary).IsAssignableFrom(targetType))
        {
            var dstDict = (IDictionary)CreateInstance(targetType);
            context[srcObj] = dstDict;

            var srcDict = (IDictionary)srcObj;

            // 获取目标字典的 Key 和 Value 类型
            var genericArgs = targetType.IsGenericType ? targetType.GetGenericArguments() : new[] { typeof(object), typeof(object) };
            var keyType = genericArgs[0];
            var valueType = genericArgs[1];

            foreach (DictionaryEntry entry in srcDict)
            {
                var newKey = DeepCopy(entry.Key, keyType, context);
                var newValue = DeepCopy(entry.Value, valueType, context);
                dstDict.Add(newKey, newValue);
            }
            return dstDict;
        }

        // 6. 复杂对象深度拷贝（Duck Typing）
        // 创建目标类型的实例
        var newDstObj = CreateInstance(targetType);
        context[srcObj] = newDstObj; // 必须在填充字段前注册，防止无限递归

        DeepCopyFields(srcObj, newDstObj, context);

        return newDstObj;
    }

    /// <summary>
    /// 填充字段：将 src 的字段值转换并赋值给 dst 的同名字段
    /// </summary>
    private static void DeepCopyFields(object src, object dst, Dictionary<object, object> context)
    {
        var srcType = src.GetType();
        var dstType = dst.GetType();

        // 获取所有字段（包括基类私有）
        var srcFields = GetCachedFields(srcType);
        var dstFields = GetCachedFields(dstType);

        // 构建目标字段映射表：Name -> FieldInfo
        // 如果子类隐藏了父类字段，我们优先使用子类的（GetFields返回顺序通常是Base->Derived，字典覆盖）
        var dstMap = new Dictionary<string, FieldInfo>();
        foreach (var f in dstFields)
            dstMap[f.Name] = f;

        foreach (var srcField in srcFields)
        {
            // 查找同名字段
            if (!dstMap.TryGetValue(srcField.Name, out var dstField))
                continue;

            // 获取源值
            var srcValue = srcField.GetValue(src);

            // 递归转换值
            var dstValue = DeepCopy(srcValue, dstField.FieldType, context);

            // 赋值
            // 即使是 readonly 字段，反射 SetValue 也能写入
            try
            {
                dstField.SetValue(dst, dstValue);
            }
            catch (Exception)
            {
                // 忽略赋值失败的情况（如类型严重不匹配且转换失败）
            }
        }
    }

    /// <summary>
    /// 获取类型的所有字段（递归包含基类），带缓存
    /// </summary>
    private static FieldInfo[] GetCachedFields(Type type)
    {
        return _fieldCache.GetOrAdd(type, t =>
        {
            var fields = new List<FieldInfo>();
            while (t != null && t != typeof(object))
            {
                fields.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                t = t.BaseType;
            }
            return fields.ToArray();
        });
    }

    /// <summary>
    /// 创建实例，优先使用 Activator，失败则使用 FormatterServices (绕过构造函数)
    /// </summary>
    private static object CreateInstance(Type type)
    {
        try
        {
            // 尝试调用私有/公有无参构造
            return Activator.CreateInstance(type, true);
        }
        catch
        {
            try
            {
                // 如果没有无参构造（如 strict 的数据类），强行创建未初始化对象
                // 需要 System.Runtime.Serialization
                return FormatterServices.GetUninitializedObject(type);
            }
            catch
            {
                // 如果连这个都失败了（例如字符串或特定系统类），返回 null 或抛出
                return null;
            }
        }
    }

    #endregion
}