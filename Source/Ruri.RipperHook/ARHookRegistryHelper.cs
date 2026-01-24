using System.Reflection;
using AssetRipper.Assets;
using AssetRipper.Assets.Metadata;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using Ruri.Hook.Attributes;
using Ruri.Hook.Core;
using Ruri.Hook.Utils;
using Ruri.RipperHook.Core;

namespace Ruri.RipperHook
{
    public static class ARHookRegistryHelper
    {
        // Helper to resolve AssetRipper type name from ClassID
        private static string GetSourceTypeFullName(ClassIDType classIdType, UnityVersion version)
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
                }
            }

            if (factoryType == null)
                throw new InvalidOperationException($"[ARHookRegistryHelper] Could not find factory type for {classIdType} in {asm.GetName().Name} (Tried {factoryTypeName})");

            var mi = factoryType.GetMethod("Create", new[] { typeof(AssetInfo), typeof(UnityVersion) });
            if (mi == null)
                throw new InvalidOperationException($"[ARHookRegistryHelper] Create method missing on {factoryType.FullName}");

            // invoke Create(null, version)
            // We pass null for AssetInfo because for purely getting the type, the implementation usually doesn't use it, 
            // OR we are relying on the fact that existing logic (RetargetMethodAttribute) did exactly this.
            object instance = mi.Invoke(null, new object[] { null, version });
            return instance.GetType().FullName;
        }

        public static void RegisterClassHooks(
            HookRegistry registry,
            IEnumerable<ClassIDType> classIds, 
            UnityVersion lookupVersion,
            UnityVersion executionVersion, 
            Assembly ruriGeneratedAssembly,
            string generatedNamespaceBase,
            Dictionary<ClassIDType, HookDispatcher.ReadReleaseDelegate>? customCallbacks = null)
        {
             var universalDestMethod = typeof(HookDispatcher).GetMethod(nameof(HookDispatcher.Universal_ReadRelease), BindingFlags.Public | BindingFlags.Static);
             if (universalDestMethod == null) throw new Exception("Universal_ReadRelease missing");

             var originalAssembly = typeof(ClassIDType).Assembly; 

             foreach(var classId in classIds)
             {
                 try 
                 {
                     RegisterSingleClassHook(classId, lookupVersion, executionVersion, ruriGeneratedAssembly, generatedNamespaceBase, customCallbacks, universalDestMethod, originalAssembly);
                 }
                 catch (Exception ex)
                 {
                    HookLogger.LogFailure($"Error hooking {classId}: {ex.Message}");
                 }
             }
        }

        private static void RegisterSingleClassHook(
            ClassIDType classId, 
            UnityVersion lookupVersion,
            UnityVersion executionVersion, 
            Assembly ruriAssembly, 
            string generatedNamespaceBase,
            Dictionary<ClassIDType, HookDispatcher.ReadReleaseDelegate>? customCallbacks,
            MethodInfo universalDestMethod,
            Assembly originalAssembly)
        {
            try
            {
                // Use lookupVersion (Base Version) to find the Source Type in AR
                string sourceTypeName = GetSourceTypeFullName(classId, lookupVersion); 
                Type? sourceType = originalAssembly.GetType(sourceTypeName);
                if (sourceType == null) 
                {
                    TypeTreeLogger.LogFailure(classId, $"Failed to find source type: {sourceTypeName}");
                    return;
                }

                int id = (int)classId;
                string enumName = classId.ToString();
                string ruriBaseNamespace = $"{generatedNamespaceBase}.Classes.ClassID_{id}";
                string ruriTypeName = $"{ruriBaseNamespace}.{enumName}";
                
                Type? ruriType = ruriAssembly.GetType(ruriTypeName);
                if (ruriType == null && enumName.EndsWith($"_{id}"))
                {
                     string cleanName = enumName.Substring(0, enumName.Length - $"_{id}".Length);
                     ruriType = ruriAssembly.GetType($"{ruriBaseNamespace}.{cleanName}");
                }

                HookDispatcher.ReadReleaseDelegate? callback = null;
                if (customCallbacks != null && customCallbacks.TryGetValue(classId, out var customAction))
                {
                    callback = customAction;
                }

                if (ruriType == null && callback == null)
                {
                    // Silent return if type hook class doesn't exist AND no callback.
                    return; 
                }

                MethodInfo? createMethod = ruriType?.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(AssetInfo), typeof(UnityVersion) }, null);
                if (ruriType != null && createMethod == null)
                {
                     TypeTreeLogger.LogFailure(classId, $"Missing 'Create' method on {ruriType.Name}");
                }

                if (createMethod == null && callback == null) 
                {
                    TypeTreeLogger.LogFailure(classId, "No callback or Create method");
                    return;
                }

                // Register with executionVersion (Custom Version) so generated Create uses it
                HookDispatcher.Register(sourceType, createMethod, executionVersion, callback);
                
                var readReleaseMethod = sourceType.GetMethod("ReadRelease", BindingFlags.Public | BindingFlags.Instance);
                if (readReleaseMethod != null)
                {
                    ReflectionExtensions.RetargetCall(readReleaseMethod, universalDestMethod, 1, true, true);
                    TypeTreeLogger.LogSuccess(sourceType.Name, "Dispatch + Detour");
                }
                else
                {
                    TypeTreeLogger.LogSuccess(sourceType.Name, "Dispatch Only"); 
                }
            } 
            catch (Exception ex)
            {
                TypeTreeLogger.LogFailure(classId, ex.Message);
            }
        }

        private static class TypeTreeLogger
        {
             public static void LogSuccess(string className, string msg) => HookLogger.LogSuccess($"[+] Hooked {className} ({msg})");
             public static void LogFailure(ClassIDType classId, string msg) => HookLogger.LogFailure($"[-] Failed {classId}: {msg}");
        }
    }
}
