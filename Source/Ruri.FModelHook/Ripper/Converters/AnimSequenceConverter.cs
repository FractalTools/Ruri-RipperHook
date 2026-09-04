using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_74;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;
using Ruri.RipperHook.Conversion;
using System.Numerics;

namespace Ruri.FModelHook.Ripper.Converters;

/// <summary>
/// An animation sequence as a generic AnimationClip: every bone track of its skeleton sampled
/// per frame (CUE4Parse decodes whatever codec the sequence used, ACL included) and written as
/// local position, rotation and scale curves addressed by the bone's transform path -- the same
/// path the rig prefab lays its bones out under, so the clip plays on it unchanged.
/// </summary>
public sealed class AnimSequenceConverter : IUnrealConverter
{
    public IReadOnlyList<string> ClassNames { get; } = ["AnimSequence"];

    public IReadOnlyList<ClassIDType> Produces { get; } = [ClassIDType.AnimationClip];

    public bool Handles(UObject export) => export is UAnimSequence;

    public void Allocate(UnrealConversion conversion, UObject export)
    {
        IAnimationClip clip = conversion.Package.Create<IAnimationClip>(ClassIDType.AnimationClip, export.Name, conversion.UnityPath(export));
        conversion.Register(export, clip);
    }

    public void Fill(UnrealConversion conversion, UObject export)
    {
        if (export is not UAnimSequence source || conversion.Table.Find<IAnimationClip>(export) is not { } clip)
        {
            return;
        }
        if (!source.Skeleton.TryLoad<USkeleton>(out USkeleton? skeleton))
        {
            Logger.Warning(LogCategory.Import, $"[Unreal] {conversion.PackagePath}:{export.Name} references no loadable skeleton.");
            return;
        }
        CAnimSet animSet = skeleton.ConvertAnims(source);
        if (animSet.Sequences.Count == 0)
        {
            return;
        }
        CAnimSequence sequence = animSet.Sequences[0];
        int frameCount = Math.Max(1, sequence.NumFrames);
        float sampleRate = sequence.FramesPerSecond > 0f ? sequence.FramesPerSecond : 30f;

        SkeletonDto skeletonDto = new(skeleton);
        UnrealRig rig = UnrealRig.From(skeletonDto.Bones, conversion.Basis);
        skeletonDto.Dispose();

        List<ClipTrack> tracks = new();
        for (int bone = 0; bone < rig.Bones.Count && bone < sequence.Tracks.Count; bone++)
        {
            CAnimTrack track = sequence.Tracks[bone];
            if (!track.HasKeys())
            {
                continue;
            }
            UnrealRig.Bone rest = rig.Bones[bone];
            FTransform restPose = skeleton.ReferenceSkeleton.FinalRefBonePose[bone];
            bool hasRotation = track.KeyQuat.Length > 0;
            bool hasPosition = track.KeyPos.Length > 0;
            bool hasScale = track.KeyScale.Length > 0;
            Quaternion[]? rotations = hasRotation ? new Quaternion[frameCount] : null;
            Vector3[]? positions = hasPosition ? new Vector3[frameCount] : null;
            Vector3[]? scales = hasScale ? new Vector3[frameCount] : null;
            for (int frame = 0; frame < frameCount; frame++)
            {
                FQuat quaternion = restPose.Rotation;
                FVector position = restPose.Translation;
                FVector scale = restPose.Scale3D;
                track.GetBoneTransform(frame, frameCount, ref quaternion, ref position, ref scale);
                if (rotations is not null)
                {
                    rotations[frame] = conversion.Basis.Rotation(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
                }
                if (positions is not null)
                {
                    positions[frame] = conversion.Basis.Position(position.X, position.Y, position.Z);
                }
                if (scales is not null)
                {
                    scales[frame] = conversion.Basis.Scale(scale.X, scale.Y, scale.Z);
                }
            }
            tracks.Add(new ClipTrack { Path = rest.Path, Positions = positions, Rotations = rotations, Scales = scales });
        }
        ClipBuilder.Fill(clip, sampleRate, frameCount, tracks, []);
    }
}
