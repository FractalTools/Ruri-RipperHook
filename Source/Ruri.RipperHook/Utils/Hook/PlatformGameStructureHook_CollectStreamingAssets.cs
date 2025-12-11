using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Platforms;
using System.Reflection;
using System.Collections.Generic;

namespace Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectStreamingAssets;

public class PlatformGameStructureHook_CollectStreamingAssets : CommonHook
{
    // 获取 protected 方法。注意：目标方法签名已变更为 (string, List<...>)
    private static readonly MethodInfo CollectAssetBundlesRecursively = typeof(PlatformGameStructure)
        .GetMethod("CollectAssetBundlesRecursively", BindingFlags.Instance | BindingFlags.NonPublic);

    // 委托定义更新：保持 MethodInfo 传递，但实现方需注意 Invoke 的参数变化
    public delegate bool CollectStreamingAssetsDelegate(PlatformGameStructure _this, List<KeyValuePair<string, string>> files, MethodInfo CollectAssetBundlesRecursively);

    public static CollectStreamingAssetsDelegate CustomCollectStreamingAssets;

    [RetargetMethod(typeof(PlatformGameStructure), nameof(CollectStreamingAssets))]
    protected void CollectStreamingAssets()
    {
        // 1. 获取目标实例上下文
        var _this = (PlatformGameStructure)(object)this;

        // 2. 执行自定义 Hook 逻辑
        // 如果自定义逻辑返回 true，说明它已经接管了流程，直接返回
        if (CustomCollectStreamingAssets != null &&
            CustomCollectStreamingAssets(_this, _this.Files, CollectAssetBundlesRecursively))
        {
            return;
        }

        // 3. 执行原版逻辑（适配新版本代码）
        // 原版逻辑：if (string.IsNullOrWhiteSpace(StreamingAssetsPath)) return;
        if (string.IsNullOrWhiteSpace(_this.StreamingAssetsPath))
        {
            return;
        }

        Logger.Info(LogCategory.Import, "Collecting Streaming Assets...");

        // 原版逻辑：if (FileSystem.Directory.Exists(StreamingAssetsPath))
        // 使用 _this.FileSystem 进行判断
        if (_this.FileSystem.Directory.Exists(_this.StreamingAssetsPath))
        {
            // 原版逻辑：CollectAssetBundlesRecursively(StreamingAssetsPath, Files);
            // 反射调用：注意参数必须是 string 类型，不能是 DirectoryInfo
            CollectAssetBundlesRecursively.Invoke(_this, new object[] { _this.StreamingAssetsPath, _this.Files });
        }
    }
}