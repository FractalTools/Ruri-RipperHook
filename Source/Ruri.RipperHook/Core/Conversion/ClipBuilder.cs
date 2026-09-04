using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_74;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Keyframe.TangentMode;
using AssetRipper.SourceGenerated.Subclasses.FloatCurve;
using AssetRipper.SourceGenerated.Subclasses.Keyframe_Quaternionf;
using AssetRipper.SourceGenerated.Subclasses.Keyframe_Single;
using AssetRipper.SourceGenerated.Subclasses.Keyframe_Vector3f;
using AssetRipper.SourceGenerated.Subclasses.QuaternionCurve;
using AssetRipper.SourceGenerated.Subclasses.Vector3Curve;
using System.Numerics;

namespace Ruri.RipperHook.Conversion;

/// <summary>
/// One animated node: its transform path inside the rig and the sampled local transform per
/// frame, already in Unity's basis. A stream left null is not animated on that node.
/// </summary>
public sealed class ClipTrack
{
    public required string Path { get; init; }

    public Vector3[]? Positions { get; init; }

    public Quaternion[]? Rotations { get; init; }

    public Vector3[]? Scales { get; init; }
}

/// <summary>One sampled scalar channel: a blend shape weight, a custom property.</summary>
public sealed class ClipFloatTrack
{
    public required string Path { get; init; }

    public required string Attribute { get; init; }

    public required ClassIDType TargetClass { get; init; }

    public required float[] Values { get; init; }
}

/// <summary>
/// Writes sampled tracks into an AnimationClip as the editor curves Unity's own importer
/// writes (m_RotationCurves, m_PositionCurves, m_ScaleCurves, m_FloatCurves): one key per
/// frame, tangents from the neighbouring samples so playback interpolates the way the source
/// sampled it, rotations kept on one hemisphere so no frame takes the long way round.
/// </summary>
public static class ClipBuilder
{
    private const float DefaultWeight = 1f / 3f;

    public static IAnimationClip Build(ConvertedPackage package, string name, string? originalPath, float sampleRate, int frameCount,
        IReadOnlyList<ClipTrack> tracks, IReadOnlyList<ClipFloatTrack> floatTracks)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (sampleRate <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "A clip needs a positive sample rate.");
        }
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "A clip needs at least one frame.");
        }

        IAnimationClip clip = package.Create<IAnimationClip>(ClassIDType.AnimationClip, name, originalPath);
        Fill(clip, sampleRate, frameCount, tracks, floatTracks);
        return clip;
    }

    public static void Fill(IAnimationClip clip, float sampleRate, int frameCount,
        IReadOnlyList<ClipTrack> tracks, IReadOnlyList<ClipFloatTrack> floatTracks)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (sampleRate <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "A clip needs a positive sample rate.");
        }
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "A clip needs at least one frame.");
        }
        clip.SampleRate_C74 = sampleRate;
        clip.Legacy_C74 = false;
        clip.Compressed_C74 = false;
        clip.UseHighQualityCurve_C74 = true;
        clip.WrapMode_C74 = (int)WrapMode.Default;
        float stopTime = (frameCount - 1) / sampleRate;
        if (clip.Has_MuscleClip_C74())
        {
            clip.MuscleClip_C74!.StartTime = 0f;
            clip.MuscleClip_C74.StopTime = stopTime;
        }
        if (clip.Has_MuscleClipInfo_C74())
        {
            clip.MuscleClipInfo_C74!.StartTime = 0f;
            clip.MuscleClipInfo_C74.StopTime = stopTime;
            clip.MuscleClipInfo_C74.KeepOriginalPositionY = true;
            clip.MuscleClipInfo_C74.KeepOriginalPositionXZ = true;
            clip.MuscleClipInfo_C74.KeepOriginalOrientation = true;
        }

        foreach (ClipTrack track in tracks)
        {
            if (track.Rotations is not null)
            {
                Validate(track.Rotations.Length, frameCount, track.Path);
                WriteRotation(clip, track.Path, track.Rotations, sampleRate);
            }
            if (track.Positions is not null)
            {
                Validate(track.Positions.Length, frameCount, track.Path);
                WriteVector(clip.PositionCurves_C74, clip.Collection.Version, track.Path, track.Positions, sampleRate);
            }
            if (track.Scales is not null)
            {
                Validate(track.Scales.Length, frameCount, track.Path);
                WriteVector(clip.ScaleCurves_C74, clip.Collection.Version, track.Path, track.Scales, sampleRate);
            }
        }
        foreach (ClipFloatTrack track in floatTracks)
        {
            Validate(track.Values.Length, frameCount, track.Path + "/" + track.Attribute);
            WriteFloat(clip, track, sampleRate);
        }
    }

    private static void Validate(int length, int frameCount, string path)
    {
        if (length != frameCount)
        {
            throw new ArgumentException($"[ClipBuilder] '{path}' has {length} samples for {frameCount} frames.");
        }
    }

    private static void WriteRotation(IAnimationClip clip, string path, Quaternion[] rotations, float sampleRate)
    {
        AlignHemispheres(rotations);
        IQuaternionCurve curve = clip.RotationCurves_C74.AddNew();
        curve.SetValues(path);
        int count = rotations.Length;
        for (int frame = 0; frame < count; frame++)
        {
            Quaternion value = rotations[frame];
            Quaternion inSlope = Slope(rotations, frame, sampleRate, incoming: true);
            Quaternion outSlope = Slope(rotations, frame, sampleRate, incoming: false);
            IKeyframe_Quaternionf key = curve.Curve.Curve.AddNew();
            key.Time = frame / sampleRate;
            key.Value.SetValues(value.X, value.Y, value.Z, value.W);
            key.InSlope.SetValues(inSlope.X, inSlope.Y, inSlope.Z, inSlope.W);
            key.OutSlope.SetValues(outSlope.X, outSlope.Y, outSlope.Z, outSlope.W);
            key.TangentMode = TangentMode.FreeFree.ToTangent(clip.Collection.Version);
            key.WeightedMode = (int)WeightedMode.None;
            key.InWeight?.SetValues(DefaultWeight, DefaultWeight, DefaultWeight, DefaultWeight);
            key.OutWeight?.SetValues(DefaultWeight, DefaultWeight, DefaultWeight, DefaultWeight);
        }
    }

    private static void WriteVector(AssetRipper.Assets.Generics.AccessListBase<IVector3Curve> curves, AssetRipper.Primitives.UnityVersion version,
        string path, Vector3[] values, float sampleRate)
    {
        IVector3Curve curve = curves.AddNew();
        curve.SetValues(path);
        int count = values.Length;
        for (int frame = 0; frame < count; frame++)
        {
            Vector3 value = values[frame];
            Vector3 inSlope = Slope(values, frame, sampleRate, incoming: true);
            Vector3 outSlope = Slope(values, frame, sampleRate, incoming: false);
            IKeyframe_Vector3f key = curve.Curve.Curve.AddNew();
            key.Time = frame / sampleRate;
            key.Value.SetValues(value.X, value.Y, value.Z);
            key.InSlope.SetValues(inSlope.X, inSlope.Y, inSlope.Z);
            key.OutSlope.SetValues(outSlope.X, outSlope.Y, outSlope.Z);
            key.TangentMode = TangentMode.FreeFree.ToTangent(version);
            key.WeightedMode = (int)WeightedMode.None;
            key.InWeight?.SetValues(DefaultWeight, DefaultWeight, DefaultWeight);
            key.OutWeight?.SetValues(DefaultWeight, DefaultWeight, DefaultWeight);
        }
    }

    private static void WriteFloat(IAnimationClip clip, ClipFloatTrack track, float sampleRate)
    {
        IFloatCurve curve = clip.FloatCurves_C74.AddNew();
        curve.Path = track.Path;
        curve.Attribute = track.Attribute;
        curve.ClassID = (int)track.TargetClass;
        float[] values = track.Values;
        for (int frame = 0; frame < values.Length; frame++)
        {
            IKeyframe_Single key = curve.Curve.Curve.AddNew();
            key.Time = frame / sampleRate;
            key.Value = values[frame];
            key.InSlope = Slope(values, frame, sampleRate, incoming: true);
            key.OutSlope = Slope(values, frame, sampleRate, incoming: false);
            key.TangentMode = TangentMode.FreeFree.ToTangent(clip.Collection.Version);
            key.WeightedMode = (int)WeightedMode.None;
            key.InWeight = DefaultWeight;
            key.OutWeight = DefaultWeight;
        }
    }

    private static void AlignHemispheres(Quaternion[] rotations)
    {
        for (int frame = 1; frame < rotations.Length; frame++)
        {
            if (Quaternion.Dot(rotations[frame], rotations[frame - 1]) < 0f)
            {
                rotations[frame] = -rotations[frame];
            }
        }
    }

    private static float Slope(float[] values, int frame, float sampleRate, bool incoming)
    {
        int last = values.Length - 1;
        if (last == 0)
        {
            return 0f;
        }
        int previous = incoming ? Math.Max(frame - 1, 0) : frame;
        int next = incoming ? frame : Math.Min(frame + 1, last);
        if (previous == next)
        {
            previous = Math.Max(frame - 1, 0);
            next = Math.Min(frame + 1, last);
        }
        return (values[next] - values[previous]) * sampleRate / (next - previous);
    }

    private static Vector3 Slope(Vector3[] values, int frame, float sampleRate, bool incoming)
    {
        int last = values.Length - 1;
        if (last == 0)
        {
            return Vector3.Zero;
        }
        int previous = incoming ? Math.Max(frame - 1, 0) : frame;
        int next = incoming ? frame : Math.Min(frame + 1, last);
        if (previous == next)
        {
            previous = Math.Max(frame - 1, 0);
            next = Math.Min(frame + 1, last);
        }
        return (values[next] - values[previous]) * (sampleRate / (next - previous));
    }

    private static Quaternion Slope(Quaternion[] values, int frame, float sampleRate, bool incoming)
    {
        int last = values.Length - 1;
        if (last == 0)
        {
            return default;
        }
        int previous = incoming ? Math.Max(frame - 1, 0) : frame;
        int next = incoming ? frame : Math.Min(frame + 1, last);
        if (previous == next)
        {
            previous = Math.Max(frame - 1, 0);
            next = Math.Min(frame + 1, last);
        }
        float scale = sampleRate / (next - previous);
        Quaternion difference = values[next] - values[previous];
        return new Quaternion(difference.X * scale, difference.Y * scale, difference.Z * scale, difference.W * scale);
    }
}
