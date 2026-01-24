using System.Reflection;
using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Assets.Metadata;
using AssetRipper.IO.Endian;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using MonoMod.Cil;
using Ruri.Hook;
using Ruri.Hook.Core;
using Ruri.RipperHook.Attributes; // Explicit import
using Ruri.RipperHook.Core;

namespace Ruri.RipperHook;

public abstract class RipperHook : RuriHook
{
    // Re-expose for compatibility
    public delegate void ReadReleaseDelegate(object asset, ref EndianSpanReader reader);

    private List<IHookModule> _modules = new();

    /// <summary>
    /// If set during Init, scanning will prioritize types with [GameHook(TargetGameName)].
    /// </summary>
    protected string? TargetGameName { get; set; }

    protected RipperHook()
    {
    }
    
    public override void Initialize()
    {
        base.Initialize(); // Calls InitAttributeHook
        ProcessAutoClassHooks();
    }

    protected void RegisterModule(IHookModule module)
    {
        _modules.Add(module);
        module.OnApply();
        Registry.ApplyTypeHooks(module.GetType());
    }

    protected override void InitAttributeHook()
    {
        base.InitAttributeHook();
        // Custom RipperHook logic can go here if needed
    }

    /// <summary>
    /// Scans for [HookObjectClass] attributes on the current class and registers them.
    /// </summary>
    protected void ProcessAutoClassHooks()
    {
        var type = GetType();
        var gameHookAttr = type.GetCustomAttribute<GameHookAttribute>();
        if (gameHookAttr == null) return; // Only process if tagged as a GameHook


        // TypeTreeHookAttribute is AssetRipper specific
        var hookClassAttrs = type.GetCustomAttributes<TypeTreeHookAttribute>();
        if (!hookClassAttrs.Any()) 
        {
            return;
        }

        HookLogger.Log($"Found {hookClassAttrs.Count()} TypeTreeHook attributes.");

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
            // Registry is now from Base (RuriHook). 
            // RegisterClassHooks WAS REMOVED from generic hook registry.
            // We need to implement it here or via extension.
            // Since it heavily relies on Registry internals or AR types, let's implement duplicate/local logic using Registry primitives?
            // Actually, Registry.RegisterClassHooks logic was AR specific. 
            // I should have moved that logic TO THIS CLASS or a helper in THIS project.
            
            ARHookRegistryHelper.RegisterClassHooks(Registry, classIds, lookupVersion, targetVersion, ruriAssembly, generatedAssemblyNamespace, coreCallbacks);
        }
    }
    
    // SetAssetListField is AR specific
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