using System.Reflection;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Extensions;
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
/// one place the exporter turns a Texture2D is rewritten to ask first. Registered by the active
/// decoder, which a process has one of: a texture nobody registered is turned exactly as before.
/// </summary>
public class TextureOrientationHook : CommonHook, IHookModule
{
    public void OnApply()
    {
    }

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
        SerializeTheRead(il);
        return rewritten;
    }

    /// <summary>
    /// Put the image-data read behind a lock, so the conversion is safe to call from several
    /// threads at once. The bytes of a streamed texture come off the one stream its resource
    /// file is: read it concurrently and the stream position interleaves, which shows up as
    /// corrupted pixels and never as an error. Everything after the read is computation over a
    /// buffer, which is what the caller then spreads across cores.
    /// </summary>
    private static void SerializeTheRead(ILContext il)
    {
        MethodInfo serialized = typeof(TextureOrientationHook).GetMethod(nameof(ReadImageDataSerially), BindingFlags.Public | BindingFlags.Static)!;
        ILCursor cursor = new(il);
        while (cursor.TryGotoNext(MoveType.Before, IsImageDataRead))
        {
            cursor.Remove();
            cursor.Emit(OpCodes.Call, serialized);
            ReadsSerialized = true;
        }
    }

    /// <summary>Whether the read inside the conversion is serialised, so a caller may convert on every core.</summary>
    public static bool ReadsSerialized { get; private set; }

    private static readonly object ImageDataGate = new();

    public static byte[] ReadImageDataSerially(ITexture2D texture)
    {
        lock (ImageDataGate)
        {
            return texture.GetImageData();
        }
    }

    /// <summary>
    /// The image-data read, whichever extension container the compiler put it in: AssetRipper
    /// declares it as an extension MEMBER, which lowers into the type holding that block
    /// (Texture2DExtensions), not the one whose interface it extends.
    /// </summary>
    private static bool IsImageDataRead(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
        && instruction.Operand is MethodReference reference
        && reference.Name == "GetImageData"
        && reference.DeclaringType.Name.EndsWith("Extensions", StringComparison.Ordinal);

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
