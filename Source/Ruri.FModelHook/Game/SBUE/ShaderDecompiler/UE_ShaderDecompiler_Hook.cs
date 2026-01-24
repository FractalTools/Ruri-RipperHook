using System;
using MonoMod.Cil;
using Ruri.Hook;
using Ruri.Hook.Attributes;
using FModel.ViewModels;
using FModel.Services;
using System.IO;
using FModel.Settings;
using CUE4Parse.FileProvider.Objects;
using Serilog;
using Ruri.FModelHook.Game.SBUE.ShaderDecompiler;

namespace Ruri.FModelHook.Game.SBUE.ShaderDecompiler
{
    [GameHook("UE_ShaderDecompiler")]
    public class UE_ShaderDecompiler_Hook : RuriHook
    {
        // Use RetargetMethodFunc to allow C# logic injection with IL control flow
        [RetargetMethodFunc(typeof(CUE4ParseViewModel), "ExportData")]
        public static bool ExportData_Hook(ILContext il)
        {
            var cursor = new ILCursor(il);
            
            var labelContinue = cursor.DefineLabel();

            cursor.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1); 
            
            cursor.EmitDelegate<Func<GameFile, bool>>(TryExportShaderLibrary);
            
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Brfalse, labelContinue);
            
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            
            cursor.MarkLabel(labelContinue);

            return true;
        }

        public static bool TryExportShaderLibrary(GameFile entry)
        {
            if (entry == null) return false;

            if (entry.Extension.Equals("ushaderbytecode", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var libraryBytes = ShaderArchiveExporter.SaveShaderLibrary(entry);
                    if (libraryBytes != null)
                    {
                        string p = Path.Combine(UserSettings.Default.RawDataDirectory, UserSettings.Default.KeepDirectoryStructure ? entry.PathWithoutExtension : entry.NameWithoutExtension).Replace('\\', '/') + ".ushaderlib";
                        Directory.CreateDirectory(Path.GetDirectoryName(p));
                        File.WriteAllBytes(p, libraryBytes);
                        
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }
    }
}
