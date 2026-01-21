using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using MonoMod.Cil;
using Ruri.RipperHook.Attributes;
using AssetRipper.Assets.Generics;
using AssetRipper.Assets;
using AssetRipper.Assets.Metadata;

namespace Ruri.RipperHook.Core
{
    public class HookRegistry
    {
        // Namespace filtering removed as requested. 
        // Logic now relies purely on [GameHook] attributes and explicit module registration.

        /// <summary>
        /// Scans the assembly for methods with RetargetMethod attributes.
        /// Supports filtering by GameName if provided.
        /// Also incorporates manually added method hooks for absolute compatibility.
        /// </summary>
        public void ApplyAttributeHooks(Assembly assembly, string? targetGameName = null, IEnumerable<MethodInfo>? manualMethods = null)
        {
            var bindingFlags = ReflectionExtensions.AnyBindFlag();
            var types = assembly.GetTypes();

            IEnumerable<Type> targetTypes;

            if (!string.IsNullOrEmpty(targetGameName))
            {
                // Attribute based filtering: Only scan types with [GameHook(targetGameName)]
                targetTypes = types.Where(t => 
                {
                    var attr = t.GetCustomAttribute<GameHookAttribute>();
                    return attr != null && attr.GameName == targetGameName;
                });
            }
            else
            {
                // Legacy / General Mode: If no target game name is specified, what do we scan?
                // The requirement is "Unified ... No distinguishing version".
                // If targetGameName is null, we shouldn't indiscriminately apply hooks unless intended.
                // For safety, and absolute equivalence, if no game name, we might not want to apply anything UNLESS explicitly manual.
                targetTypes = Enumerable.Empty<Type>();
            }

            var scannedMethods = targetTypes.SelectMany(t => t.GetMethods(bindingFlags));

            // Merge with manual methods if any
            var allMethods = manualMethods != null 
                ? scannedMethods.Concat(manualMethods).Distinct() 
                : scannedMethods;

            ApplyRetargetMethodAttributes(allMethods);
            ApplyRetargetMethodFuncAttributes(allMethods);
            ApplyRetargetMethodCtorFuncAttributes(allMethods);
        }

        private void ApplyRetargetMethodAttributes(IEnumerable<MethodInfo> methods)
        {
             var targetMethods = methods.Where(m => m.GetCustomAttributes<RetargetMethodAttribute>(true).Any());

             foreach (var methodDest in targetMethods)
             {
                 var attrs = methodDest.GetCustomAttributes<RetargetMethodAttribute>();
                 foreach (var attr in attrs)
                 {
                     ProcessRetarget(methodDest, attr);
                 }
             }
        }

        private void ProcessRetarget(MethodInfo methodDest, RetargetMethodAttribute attr)
        {
            var bindingFlags = ReflectionExtensions.AnyBindFlag();
            MethodInfo? methodSrc;
            
            if (attr.MethodParameters == null)
                methodSrc = attr.SourceType.GetMethod(attr.SourceMethodName, bindingFlags);
            else
                methodSrc = attr.SourceType.GetMethod(attr.SourceMethodName, bindingFlags, attr.MethodParameters);

            if (methodSrc == null)
                 throw new Exception($"[HookRegistry] Could not find source method {attr.SourceType.Name}.{attr.SourceMethodName}");

            int srcParamCount = methodSrc.GetParameters().Length;
            int destParamCount = methodDest.GetParameters().Length;

            if (methodSrc.IsStatic) srcParamCount--;
            
            ReflectionExtensions.RetargetCall(methodSrc, methodDest, srcParamCount, attr.IsBefore, attr.IsReturn);
        }

        private void ApplyRetargetMethodFuncAttributes(IEnumerable<MethodInfo> methods)
        {
             var targetMethods = methods.Where(m => m.GetCustomAttributes<RetargetMethodFuncAttribute>(true).Any());
             foreach(var methodDest in targetMethods)
             {
                 foreach(var attr in methodDest.GetCustomAttributes<RetargetMethodFuncAttribute>())
                 {
                     var bindingFlags = ReflectionExtensions.AnyBindFlag();
                     MethodInfo? methodSrc = attr.MethodParameters == null 
                        ? attr.SourceType.GetMethod(attr.SourceMethodName, bindingFlags)
                        : attr.SourceType.GetMethod(attr.SourceMethodName, bindingFlags, attr.MethodParameters);

                     var hookCallback = (Func<ILContext, bool>)Delegate.CreateDelegate(typeof(Func<ILContext, bool>), methodDest);
                     ReflectionExtensions.RetargetCallFunc(hookCallback, methodSrc);
                 }
             }
        }

        private void ApplyRetargetMethodCtorFuncAttributes(IEnumerable<MethodInfo> methods)
        {
             var targetMethods = methods.Where(m => m.GetCustomAttributes<RetargetMethodCtorFuncAttribute>(true).Any());
             foreach(var methodDest in targetMethods)
             {
                 foreach(var attr in methodDest.GetCustomAttributes<RetargetMethodCtorFuncAttribute>())
                 {
                     var bindingFlags = ReflectionExtensions.AnyBindFlag();
                     ConstructorInfo? methodSrc = attr.MethodParameters == null 
                        ? attr.SourceType.GetConstructor(Type.EmptyTypes)
                        : attr.SourceType.GetConstructor(bindingFlags, attr.MethodParameters);

                     var hookCallback = (Func<ILContext, bool>)Delegate.CreateDelegate(typeof(Func<ILContext, bool>), methodDest);
                     ReflectionExtensions.RetargetCallCtorFunc(hookCallback, methodSrc);
                 }
             }
        }

        public void RegisterClassHooks(
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
                     Console.WriteLine($"[HookRegistry] Error hooking {classId}: {ex.Message}");
                 }
             }
        }

        public void ApplyTypeHooks(Type type)
        {
            var bindingFlags = ReflectionExtensions.AnyBindFlag();
            var methods = type.GetMethods(bindingFlags);
            
            ApplyRetargetMethodAttributes(methods);
            ApplyRetargetMethodFuncAttributes(methods);
            ApplyRetargetMethodCtorFuncAttributes(methods);
        }

        public void ApplyManualHooks(IEnumerable<MethodInfo> methods)
        {
            ApplyRetargetMethodAttributes(methods);
            ApplyRetargetMethodFuncAttributes(methods);
            ApplyRetargetMethodCtorFuncAttributes(methods);
        }

        private void RegisterSingleClassHook(
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
                string sourceTypeName = RetargetMethodAttribute.GetSourceTypeFullName(classId, lookupVersion); 
                Type? sourceType = originalAssembly.GetType(sourceTypeName);
                if (sourceType == null) return;

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

                MethodInfo? createMethod = ruriType?.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(AssetInfo), typeof(UnityVersion) }, null);

                HookDispatcher.ReadReleaseDelegate? callback = null;
                if (customCallbacks != null && customCallbacks.TryGetValue(classId, out var customAction))
                {
                    callback = customAction;
                }

                if (createMethod == null && callback == null) return;

                // Register with executionVersion (Custom Version) so generated Create uses it
                HookDispatcher.Register(sourceType, createMethod, executionVersion, callback);

                var readReleaseMethod = sourceType.GetMethod("ReadRelease", BindingFlags.Public | BindingFlags.Instance);
                if (readReleaseMethod != null)
                {
                    ReflectionExtensions.RetargetCall(readReleaseMethod, universalDestMethod, 1, true, true);
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine($"[HookRegistry] Failed to register single class hook for {classId}: {ex.Message}");
            }
        }
    }
}
