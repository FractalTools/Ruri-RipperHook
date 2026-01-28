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
                    // ---------------------------------------------------------
                    // 1. Resolve AssetRipper Source Type (Inline GetSourceTypeFullName logic)
                    // ---------------------------------------------------------
                    int id = (int)classId;
                    string enumName = classId.ToString();
                    string baseNamespace = $"AssetRipper.SourceGenerated.Classes.ClassID_{id}";
                    
                    // Try standard name
                    string factoryTypeName = $"{baseNamespace}.{enumName}";
                    Type? factoryType = originalAssembly.GetType(factoryTypeName);

                    // Try removing suffix if not found
                    if (factoryType == null)
                    {
                        string suffix = $"_{id}";
                        if (enumName.EndsWith(suffix))
                        {
                            string cleanName = enumName.Substring(0, enumName.Length - suffix.Length);
                            string cleanTypeName = $"{baseNamespace}.{cleanName}";
                            factoryType = originalAssembly.GetType(cleanTypeName);
                        }
                    }

                    if (factoryType == null)
                        throw new InvalidOperationException($"[ARHookRegistryHelper] Could not find factory type for {classId}");

                    var mi = factoryType.GetMethod("Create", new[] { typeof(AssetInfo), typeof(UnityVersion) });
                    if (mi == null)
                        throw new InvalidOperationException($"[ARHookRegistryHelper] Create method missing on {factoryType.FullName}");

                    // Invoke Create(null, lookupVersion) to get an instance, then get its type.
                    // This seems to be the way to get the concrete type for that version.
                    object instance = mi.Invoke(null, new object[] { null, lookupVersion });
                    Type sourceType = instance.GetType();
                    string sourceTypeName = sourceType.FullName!;

                    // ---------------------------------------------------------
                    // 2. Resolve Ruri Target Type & Hooks
                    // ---------------------------------------------------------
                    string ruriBaseNamespace = $"{generatedNamespaceBase}.Classes.ClassID_{id}";
                    string ruriTypeName = $"{ruriBaseNamespace}.{enumName}";
                    
                    Type? ruriType = ruriGeneratedAssembly.GetType(ruriTypeName);
                    if (ruriType == null && enumName.EndsWith($"_{id}"))
                    {
                         string cleanName = enumName.Substring(0, enumName.Length - $"_{id}".Length);
                         ruriType = ruriGeneratedAssembly.GetType($"{ruriBaseNamespace}.{cleanName}");
                    }

                    HookDispatcher.ReadReleaseDelegate? callback = null;
                    if (customCallbacks != null && customCallbacks.TryGetValue(classId, out var customAction))
                    {
                        callback = customAction;
                    }

                    if (ruriType == null && callback == null)
                    {
                        // No target type and no custom callback - nothing to hook
                        continue; 
                    }

                    MethodInfo? createMethod = ruriType?.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(AssetInfo), typeof(UnityVersion) }, null);
                    if (ruriType != null && createMethod == null)
                    {
                         HookLogger.LogFailure($"[-] Failed {classId}: Missing 'Create' method on {ruriType.Name}");
                         continue;
                    }

                    if (createMethod == null && callback == null) 
                    {
                        HookLogger.LogFailure($"[-] Failed {classId}: No callback or Create method");
                        continue;
                    }

                    // Register with executionVersion (Custom Version) so generated Create uses it
                    HookDispatcher.Register(sourceType, createMethod, executionVersion, callback);
                    
                    var readReleaseMethod = sourceType.GetMethod("ReadRelease", BindingFlags.Public | BindingFlags.Instance);
                    if (readReleaseMethod != null)
                    {
                        ReflectionExtensions.RetargetCall(readReleaseMethod, universalDestMethod, 1, true, true);
                        
                        string targetName = "Unknown";
                        if (createMethod != null)
                        {
                            object targetInstance = createMethod.Invoke(null, new object[] { null, executionVersion });
                            targetName = targetInstance.GetType().Name;
                        }

                        HookLogger.LogSuccess($"[+] {sourceType.Name} -> {targetName}");
                    }
                    else
                    {
                        HookLogger.LogSuccess($"[+] {sourceType.Name} (Dispatch Only)"); 
                    }
                 }
                 catch (Exception ex)
                 {
                    HookLogger.LogFailure($"[-] Failed {classId}: {ex.Message}");
                 }
             }
        }
    }
}
