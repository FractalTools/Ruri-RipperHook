using Ruri.Hook;
using Ruri.Hook.Attributes;
using FModel.ViewModels;
using FModel.Settings;
using CUE4Parse.FileProvider.Objects;
using Ruri.Hook.Core;

namespace Ruri.FModelHook.Game.SBUE.ShaderDecompiler
{
    [GameHook("UE_ShaderDecompiler")]
    public class UE_ShaderDecompiler_Hook : RuriHook
    {
        // Use RetargetMethod to safely inject C# logic before the original method and fall through (IsReturn = false)
        // Positional args: Type source, string methodName, bool isBefore, bool isReturn
        [RetargetMethod(typeof(CUE4ParseViewModel), "ExportData", true, false)]
        public static void ExportData_Hook(CUE4ParseViewModel self, GameFile entry, bool updateUi)
        {
            if (entry == null) return;

            if (entry.Extension.Equals("ushaderbytecode", StringComparison.OrdinalIgnoreCase))
            {
                var libraryBytes = ShaderArchiveExporter.SaveShaderLibrary(entry);
                if (libraryBytes != null)
                {
                    string path = Path.Combine(UserSettings.Default.RawDataDirectory, UserSettings.Default.KeepDirectoryStructure ? entry.PathWithoutExtension : entry.NameWithoutExtension).Replace('\\', '/') + ".ushaderlib";
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, libraryBytes);

                    // Log success via standard logger if desired, or silent
                    HookLogger.LogSuccess($"[+] Exported ShaderLibrary: {path}");
                }
            }
        }
    }
}
