using System.Reflection;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Ruri.Hook.Attributes;
using Ruri.Hook.Core;
using Ruri.RipperHook.Conversion;

namespace Ruri.RipperHook.AR;

/// <summary>
/// AssetRipper turns every decoded texture over because Unity stores them bottom-up. A texture
/// a converter stored top-down (see <see cref="TextureOrientation"/>) must not be turned, so the
/// one place the exporter turns a Texture2D is rewritten to ask first. Registered for every
/// decoder: a texture nobody registered is turned exactly as before.
/// </summary>
public class TextureOrientationHook : CommonHook, IHookModule
{
    public void OnApply()
    {
        Registry.ApplyTypeHooks(GetType());
    }

    /// <summary>
    /// Every hook instance registers this module, and a manipulator later in the chain sees the
    /// IL its predecessor already rewrote, so finding the call already replaced is success too.
    /// </summary>
    [RetargetMethodFunc(typeof(TextureConverter), nameof(TextureConverter.TryConvertToBitmap), typeof(ITexture2D), typeof(DirectBitmap))]
    public static bool AskBeforeTurning(ILContext il)
    {
        MethodInfo replacement = typeof(TextureOrientationHook).GetMethod(nameof(TurnUnlessTopDown), BindingFlags.Public | BindingFlags.Static)!;
        ILCursor cursor = new(il);
        bool rewritten = false;
        while (cursor.TryGotoNext(MoveType.Before, instruction => IsCallTo(instruction, nameof(DirectBitmap), nameof(DirectBitmap.FlipY))))
        {
            cursor.Remove();
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.Emit(OpCodes.Call, replacement);
            rewritten = true;
        }
        if (rewritten)
        {
            return true;
        }
        cursor.Index = 0;
        return cursor.TryGotoNext(MoveType.Before, instruction => IsCallTo(instruction, nameof(TextureOrientationHook), nameof(TurnUnlessTopDown)));
    }

    private static bool IsCallTo(Instruction instruction, string declaringType, string method) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
        && instruction.Operand is MethodReference reference
        && reference.Name == method
        && reference.DeclaringType.Name == declaringType;

    public static void TurnUnlessTopDown(DirectBitmap bitmap, ITexture2D texture)
    {
        if (!TextureOrientation.IsTopDown(texture))
        {
            bitmap.FlipY();
        }
    }
}
