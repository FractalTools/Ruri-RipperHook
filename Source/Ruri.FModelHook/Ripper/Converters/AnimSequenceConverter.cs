using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_74;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
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
        int sourceFrames = Math.Max(1, sequence.NumFrames);
        float assetRate = AssetRate(source, sourceFrames);
        float statedRate = UnrealSourceOptions.AnimationSampleRateValue();
        float sampleRate = statedRate > 0f ? statedRate : assetRate;
        int frameCount = statedRate > 0f ? Math.Max(1, (int)MathF.Round(source.SequenceLength * statedRate) + 1) : sourceFrames;
        float sourceStep = frameCount > 1 ? (sourceFrames - 1f) / (frameCount - 1f) : 0f;

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
                track.GetBoneTransform(frame * sourceStep, sourceFrames, ref quaternion, ref position, ref scale);
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
        float toleranceScale = UnrealSourceOptions.AnimationToleranceValue();
        ClipBuilder.Fill(clip, sampleRate, frameCount, tracks, [], toleranceScale > 0f ? Tolerance(source, conversion.Basis, toleranceScale) : null);
    }

    /// <summary>
    /// The precision the sequence was compressed at, as the tolerance its written curves keep:
    /// the ACL codec that compressed it states an error threshold and the virtual vertex distance
    /// that threshold is measured at (UAnimBoneCompressionCodec_ACLBase), so a key is dropped
    /// only where the played curve stays within the displacement the game itself accepted,
    /// scaled by the stated multiple. A codec property the cooked object leaves unstated holds
    /// its class default, which is what an unstated property means. A sequence compressed by
    /// anything else keeps every sample.
    /// </summary>
    private static ClipTolerance? Tolerance(UAnimSequence source, SourceBasis basis, float scale)
    {
        if (source.BoneCompressionSettings?.Load<UAnimBoneCompressionSettings>() is not { } settings
            || settings.GetCodec(source.BoneCodecDDCHandle ?? string.Empty) is not { } codec
            || !codec.ExportType.Contains(AclCodecMarker, StringComparison.Ordinal))
        {
            return null;
        }
        float threshold = codec.GetOrDefault("ErrorThreshold", AclErrorThresholdCentimetres);
        float vertexDistance = codec.GetOrDefault("DefaultVirtualVertexDistance", AclVirtualVertexDistanceCentimetres);
        Logger.Verbose(LogCategory.Import, $"[Unreal] {source.Name}: {codec.ExportType} '{codec.Name}' threshold {threshold} cm at {vertexDistance} cm, tolerance x{scale}; stated {string.Join(", ", codec.Properties.Select(static property => property.Name.Text + "=" + property.Tag?.GenericValue))}.");
        float displacement = threshold * basis.UnitScale * scale;
        float angular = threshold / vertexDistance * scale;
        return new ClipTolerance(displacement, angular, angular, displacement);
    }

    private const string AclCodecMarker = "ACL";
    private const float AclErrorThresholdCentimetres = 0.01f;
    private const float AclVirtualVertexDistanceCentimetres = 3f;

    /// <summary>
    /// The rate the sequence was sampled at, as the engine defines it: the platform target frame
    /// rate the cooked sequence carries (UAnimSequence::GetSamplingFrameRate), and for a sequence
    /// cooked before that field existed, the relation the engine keeps between its key count and
    /// its length (keys = length * rate + 1).
    /// </summary>
    private static float AssetRate(UAnimSequence source, int sourceFrames)
    {
        FPerPlatformFrameRate? target = source.GetOrDefault<FPerPlatformFrameRate>("PlatformTargetFrameRate");
        if (target is { Default.Denominator: > 0 })
        {
            return (float)target.Default.Numerator / target.Default.Denominator;
        }
        return source.SequenceLength > 0f && sourceFrames > 1 ? (sourceFrames - 1) / source.SequenceLength : 1f;
    }
}
