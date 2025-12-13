using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Metadata;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using System.Diagnostics;
using System.Reflection;

namespace Ruri.RipperHook;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RetargetMethodAttribute : Attribute
{
    public RetargetMethodAttribute(Type sourceType, string sourceMethodName = "ReadRelease", bool isBefore = true, bool isReturn = true, Type[] methodParameters = null)
    {
        SourceType = sourceType;
        SourceMethodName = sourceMethodName;
        IsBefore = isBefore;
        IsReturn = isReturn;
        MethodParameters = methodParameters;
    }

    public RetargetMethodAttribute(string sourceTypeName, string sourceMethodName = "ReadRelease", bool isBefore = true, bool isReturn = true, Type[] methodParameters = null)
    {
        SourceType = Type.GetType(sourceTypeName);
        Debug.Assert(SourceType != null);
        SourceMethodName = sourceMethodName;
        IsBefore = isBefore;
        IsReturn = isReturn;
        MethodParameters = methodParameters;
    }

    /// <summary>
    /// 自动根据 ClassIDType + Unity 版本字符串，动态算出真正的目标类型。
    /// 用法示例：
    /// [RetargetMethod(ClassIDType.Camera, "2019.4.34f1")]
    /// </summary>
    public RetargetMethodAttribute(ClassIDType classIdType, string unityVersion, string sourceMethodName = "ReadRelease", bool isBefore = true, bool isReturn = true, Type[] methodParameters = null) : this(
            $"{GetSourceTypeFullName(classIdType, UnityVersion.Parse(unityVersion))}, {typeof(ClassIDType).Assembly.GetName().Name}",
            sourceMethodName,
            isBefore,
            isReturn,
            methodParameters)
    {
    }

    /// <summary>
    /// 通过反射调用工厂的 Create 方法，拿到实例后取其实际类型全名。
    /// </summary>
    public static string GetSourceTypeFullName(ClassIDType classIdType, UnityVersion version)
    {
        int id = (int)classIdType;
        string enumName = classIdType.ToString();
        // 基础命名空间，例如: AssetRipper.SourceGenerated.Classes.ClassID_238
        string baseNamespace = $"AssetRipper.SourceGenerated.Classes.ClassID_{id}";

        Assembly asm = typeof(ClassIDType).Assembly;

        // 1. 尝试标准命名 (例如: ClassID_20.Camera)
        string factoryTypeName = $"{baseNamespace}.{enumName}";
        Type? factoryType = asm.GetType(factoryTypeName);

        // 2. 特殊情况修正 (例如: NavMeshData_238 -> NavMeshData)
        // 如果找不到类型，且枚举名字是以 "_ID" 结尾的，说明是 AssetRipper 为了防冲突加的后缀，尝试去掉后缀查找。
        if (factoryType == null)
        {
            string suffix = $"_{id}";
            // 检查枚举名是否以 "_ID" 结尾 (例如 "NavMeshData_238" 结尾是 "_238")
            if (enumName.EndsWith(suffix))
            {
                // 移除后缀
                string cleanName = enumName.Substring(0, enumName.Length - suffix.Length);
                string cleanTypeName = $"{baseNamespace}.{cleanName}";
                factoryType = asm.GetType(cleanTypeName);

                // 可选：调试日志
                // if (factoryType != null) Console.WriteLine($"[RipperHook] Fixed ClassID name: {enumName} -> {cleanName}");
            }
        }

        if (factoryType == null)
            throw new InvalidOperationException($"找不到工厂类型: {factoryTypeName} (已尝试清理后缀 _{id}) in {asm.GetName().Name}");

        var mi = factoryType.GetMethod("Create", new[] { typeof(AssetInfo), typeof(UnityVersion) });
        if (mi == null)
            throw new InvalidOperationException($"在 {factoryType.FullName} 中找不到 Create(AssetInfo,UnityVersion)");

        object instance = mi.Invoke(null, new object[] { null, version });
        return instance.GetType().FullName;
    }

    public Type[] MethodParameters { get; }
    public Type SourceType { get; }
    public string SourceMethodName { get; }
    public bool IsBefore { get; }
    public bool IsReturn { get; }
}