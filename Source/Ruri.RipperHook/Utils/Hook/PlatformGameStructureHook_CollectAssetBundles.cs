using AssetRipper.Import.Structure.Platforms;
using System.Reflection;

namespace Ruri.RipperHook.HookUtils.PlatformGameStructureHook_CollectAssetBundles;

public class PlatformGameStructureHook_CollectAssetBundles : CommonHook
{
    // 获取 protected static void AddAssetBundle(...)
    private static readonly MethodInfo AddAssetBundle = typeof(PlatformGameStructure)
        .GetMethod("AddAssetBundle", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);

    // [变更] 委托签名改为 string 参数
    public delegate bool AssetBundlesCheckDelegate(string filePath);

    public static AssetBundlesCheckDelegate CustomAssetBundlesCheck;

    // [变更] 目标方法签名必须匹配: protected void CollectAssetBundles(string root, List<KeyValuePair<string, string>> files)
    [RetargetMethod(typeof(PlatformGameStructure), "CollectAssetBundles")]
    public void CollectAssetBundles(string root, List<KeyValuePair<string, string>> files)
    {
        // 获取当前实例 (this)
        var _this = (PlatformGameStructure)(object)this;
        var fs = _this.FileSystem;

        // 使用新版 FileSystem 遍历文件
        // EnumerateFiles 返回的是 string (full path)
        foreach (string file in fs.Directory.EnumerateFiles(root))
        {
            // 使用自定义检测逻辑
            if (CustomAssetBundlesCheck(file))
            {
                // 获取文件名 (去除扩展名)
                string name = fs.Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

                // 调用 AddAssetBundle (静态方法)
                // AddAssetBundle(List<KeyValuePair<string, string>> files, string name, string path)
                AddAssetBundle.Invoke(null, new object[] { files, name, file });
            }
        }
    }
}