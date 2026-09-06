using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_74;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using Ruri.RipperHook.Conversion;

namespace Ruri.FModelHook.Unreal.Converters;

/// <summary>
/// An animation sequence as a generic AnimationClip: the sequence read by
/// <see cref="UnrealClip"/>, written as local position, rotation and scale curves addressed by
/// the bone transform path the rig prefab lays its bones out under, so the clip plays on it
/// unchanged.
/// </summary>
public sealed class AnimSequenceConverter : IUnrealConverter
{
    public IReadOnlyList<string> ClassNames { get; } = ["AnimSequence"];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.AnimationClip];

    public void Allocate(UnrealConversion conversion, ResolvedObject header)
    {
        IAnimationClip clip = conversion.Package.Create<IAnimationClip>(ClassIDType.AnimationClip, header.Name.Text, conversion.UnityPath(header));
        conversion.Register(header, clip);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UAnimSequence source || conversion.Table.Find<IAnimationClip>(export) is not { } clip
            || UnrealClip.Read(source, conversion.Basis, conversion.PackagePath) is not { } sampled)
        {
            return;
        }
        ClipBuilder.Fill(clip, sampled.SampleRate, sampled.FrameCount, sampled.Tracks, [], sampled.Tolerance);
    }
}
