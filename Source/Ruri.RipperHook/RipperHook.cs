using System.Reflection;
using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Assets.Metadata;
using AssetRipper.IO.Endian;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using MonoMod.Cil;
using Ruri.RipperHook.Attributes;
using Ruri.RipperHook.Core;

namespace Ruri.RipperHook;

public abstract class RipperHook
{
    // Re-expose for compatibility
    public delegate void ReadReleaseDelegate(object asset, ref EndianSpanReader reader);

    protected readonly HookRegistry Registry = new();
    protected List<MethodInfo> methodHooks = new();
    private List<IHookModule> _modules = new();

    /// <summary>
    /// If set during Init, scanning will prioritize types with [GameHook(TargetGameName)].
    /// </summary>
    protected string? TargetGameName { get; set; }

    protected RipperHook()
    {
        // Init logic should be called by derived class if needed, or usually it's automatic.
        // But we want to allow derived classes to RegisterModule BEFORE init.
        // So we delay ProcessAutoClassHooks?
        // Original: InitAttributeHook() called in constructor.
    }
    
    public void Initialize()
    {
        InitAttributeHook();
        ProcessAutoClassHooks();
    }

    protected void RegisterModule(IHookModule module)
    {
        _modules.Add(module);
        module.OnApply();
        Registry.ApplyTypeHooks(module.GetType());
    }

    protected virtual void InitAttributeHook()
    {
        // User Request: Analyze the Game Class methods for attributes (RetargetMethod).
        // This replaces the old namespace/assembly scanning.
        Registry.ApplyTypeHooks(GetType());
        
        // Also apply any manually added methods (from AddMethodHook)
        if (methodHooks.Count > 0)
        {
             Registry.ApplyManualHooks(methodHooks);
        }
    }

    /// <summary>
    /// Scans for [HookObjectClass] attributes on the current class and registers them.
    /// </summary>
    /// <summary>
    /// Scans for [HookObjectClass] attributes on the current class and registers them.
    /// </summary>
    protected void ProcessAutoClassHooks()
    {
        var type = GetType();
        var gameHookAttr = type.GetCustomAttribute<GameHookAttribute>();
        if (gameHookAttr == null) return; // Only process if tagged as a GameHook

        var hookClassAttrs = type.GetCustomAttributes<TypeTreeHookAttribute>();
        if (!hookClassAttrs.Any()) return;

        var classIds = hookClassAttrs.Select(a => a.ClassID).ToList();
        
        UnityVersion targetVersionVec = GetTargetVersion(gameHookAttr);
        if (targetVersionVec == default) return; // Skip if version resolution failed or returned empty

        // Let's assume GeneratedNamespace is standard unless overridden
        string generatedNamespace = "Ruri.SourceGenerated";
        
        // Check if any attribute overrides namespace
        var firstNamespaceOverride = hookClassAttrs.FirstOrDefault(a => a.GeneratedAssemblyNamespace != null);
        if (firstNamespaceOverride != null) generatedNamespace = firstNamespaceOverride.GeneratedAssemblyNamespace!;

        HookClasses(classIds, gameHookAttr.BaseUnityVersion, targetVersionVec, generatedNamespace);
    }

    protected virtual UnityVersion GetTargetVersion(GameHookAttribute attr)
    {
        return UnityVersion.Parse(attr.BaseUnityVersion);
    }

    protected void HookClasses(
        IEnumerable<ClassIDType> classIds,
        string sourceUnityVersion,
        UnityVersion targetVersion,
        string generatedAssemblyNamespace = "Ruri.SourceGenerated",
        Dictionary<ClassIDType, ReadReleaseDelegate>? customCallbacks = null)
    {
        Dictionary<ClassIDType, HookDispatcher.ReadReleaseDelegate>? coreCallbacks = null;
        if (customCallbacks != null)
        {
            coreCallbacks = new Dictionary<ClassIDType, HookDispatcher.ReadReleaseDelegate>();
            foreach(var kvp in customCallbacks)
            {
                coreCallbacks[kvp.Key] = (obj, ref reader) => kvp.Value(obj, ref reader);
            }
        }

        Assembly? ruriAssembly = null;
        try
        {
            ruriAssembly = Assembly.Load(generatedAssemblyNamespace);
        }
        catch
        {
            Console.WriteLine($"[RipperHook] Warning: Could not load assembly {generatedAssemblyNamespace}");
        }

        if (ruriAssembly != null)
        {
            // Parse sourceUnityVersion (Base) for lookup
            UnityVersion lookupVersion = UnityVersion.Parse(sourceUnityVersion);
            Registry.RegisterClassHooks(classIds, lookupVersion, targetVersion, ruriAssembly, generatedAssemblyNamespace, coreCallbacks);
        }
    }
    
    protected void AddMethodHook(Type type, string name)
    {
        var method = type.GetMethod(name, ReflectionExtensions.AnyBindFlag());
        if (method != null)
        {
            methodHooks.Add(method);
        }
    }

    // Legacy Accessors
    protected void SetPrivateField(Type type, string name, object newValue)
    {
        type.GetField(name, ReflectionExtensions.PrivateInstanceBindFlag())?.SetValue(this, newValue);
    }

    protected object? GetPrivateField(Type type, string name)
    {
        return type.GetField(name, ReflectionExtensions.PrivateInstanceBindFlag())?.GetValue(this);
    }
    
    protected void SetAssetListField<T>(Type type, string name, ref EndianSpanReader reader, bool isAlign = true) where T : UnityAssetBase, new()
    {
        var field = type.GetField(name, ReflectionExtensions.PrivateInstanceBindFlag());
        if (field == null) return;

        var fieldType = field.FieldType;
        var filedObj = Activator.CreateInstance(fieldType);
        
        if (isAlign)
            ((AssetList<T>)filedObj).ReadRelease_ArrayAlign_Asset(ref reader);
        else
            ((AssetList<T>)filedObj).ReadRelease_Array_Asset(ref reader);

        field.SetValue(this, filedObj);
    }
}