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
/// writes (m_RotationCurves, m_PositionCurves, m_ScaleCurves, m_FloatCurves): the keys each
/// curve needs to reproduce its samples (see <see cref="CurveReducer"/>) with the tangents
/// fitted to them, every curve reduced on its own core, rotations made unit length (a decoded
/// quaternion's length carries nothing, and one off by a fraction reads as a rotation to any
/// angular metric) and kept on one hemisphere so no frame takes the long way round.
/// </summary>
public static class ClipBuilder
{
    private const float DefaultWeight = 1f / 3f;

    private enum CurveKind
    {
        Rotation,
        Position,
        Scale,
        Float,
    }

    private readonly record struct CurveJob(CurveKind Kind, ClipTrack? Track, ClipFloatTrack? FloatTrack);

    public static IAnimationClip Build(ConvertedPackage package, string name, string? originalPath, float sampleRate, int frameCount,
        IReadOnlyList<ClipTrack> tracks, IReadOnlyList<ClipFloatTrack> floatTracks, ClipTolerance? tolerance = null)
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
        Fill(clip, sampleRate, frameCount, tracks, floatTracks, tolerance);
        return clip;
    }

    /// <summary>
    /// Every track sampled once per frame; with a tolerance stated, each curve keeps only the
    /// keys that reproduce its samples within it, with the clip's length and rate untouched.
    /// </summary>
    public static void Fill(IAnimationClip clip, float sampleRate, int frameCount,
        IReadOnlyList<ClipTrack> tracks, IReadOnlyList<ClipFloatTrack> floatTracks, ClipTolerance? tolerance = null)
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

        List<CurveJob> jobs = new();
        foreach (ClipTrack track in tracks)
        {
            if (track.Rotations is not null)
            {
                Validate(track.Rotations.Length, frameCount, track.Path);
                jobs.Add(new CurveJob(CurveKind.Rotation, track, null));
            }
            if (track.Positions is not null)
            {
                Validate(track.Positions.Length, frameCount, track.Path);
                jobs.Add(new CurveJob(CurveKind.Position, track, null));
            }
            if (track.Scales is not null)
            {
                Validate(track.Scales.Length, frameCount, track.Path);
                jobs.Add(new CurveJob(CurveKind.Scale, track, null));
            }
        }
        foreach (ClipFloatTrack track in floatTracks)
        {
            Validate(track.Values.Length, frameCount, track.Path + "/" + track.Attribute);
            jobs.Add(new CurveJob(CurveKind.Float, null, track));
        }

        ReducedCurve[] reduced = new ReducedCurve[jobs.Count];
        Parallel.For(0, jobs.Count, index => reduced[index] = Reduce(jobs[index], sampleRate, tolerance));

        for (int index = 0; index < jobs.Count; index++)
        {
            CurveJob job = jobs[index];
            switch (job.Kind)
            {
                case CurveKind.Rotation:
                    WriteRotation(clip, job.Track!.Path, job.Track.Rotations!, reduced[index], sampleRate);
                    break;
                case CurveKind.Position:
                    WriteVector(clip.PositionCurves_C74, clip.Collection.Version, job.Track!.Path, job.Track.Positions!, reduced[index], sampleRate);
                    break;
                case CurveKind.Scale:
                    WriteVector(clip.ScaleCurves_C74, clip.Collection.Version, job.Track!.Path, job.Track.Scales!, reduced[index], sampleRate);
                    break;
                default:
                    WriteFloat(clip, job.FloatTrack!, reduced[index], sampleRate);
                    break;
            }
        }
    }

    private static void Validate(int length, int frameCount, string path)
    {
        if (length != frameCount)
        {
            throw new ArgumentException($"[ClipBuilder] '{path}' has {length} samples for {frameCount} frames.");
        }
    }

    private static ReducedCurve Reduce(CurveJob job, float sampleRate, ClipTolerance? tolerance)
    {
        switch (job.Kind)
        {
            case CurveKind.Rotation:
                Quaternion[] rotations = job.Track!.Rotations!;
                Unit(rotations);
                return CurveReducer.Reduce(Components(rotations), sampleRate, tolerance?.RotationRadians, CurveMetric.Angular);
            case CurveKind.Position:
                return CurveReducer.Reduce(Components(job.Track!.Positions!), sampleRate, tolerance?.Position, CurveMetric.Euclidean);
            case CurveKind.Scale:
                return CurveReducer.Reduce(Components(job.Track!.Scales!), sampleRate, tolerance?.Scale, CurveMetric.Euclidean);
            default:
                return CurveReducer.Reduce([job.FloatTrack!.Values], sampleRate, tolerance?.Float, CurveMetric.Euclidean);
        }
    }

    private static float[][] Components(Quaternion[] values)
    {
        float[] x = new float[values.Length];
        float[] y = new float[values.Length];
        float[] z = new float[values.Length];
        float[] w = new float[values.Length];
        for (int frame = 0; frame < values.Length; frame++)
        {
            Quaternion value = values[frame];
            x[frame] = value.X;
            y[frame] = value.Y;
            z[frame] = value.Z;
            w[frame] = value.W;
        }
        return [x, y, z, w];
    }

    private static float[][] Components(Vector3[] values)
    {
        float[] x = new float[values.Length];
        float[] y = new float[values.Length];
        float[] z = new float[values.Length];
        for (int frame = 0; frame < values.Length; frame++)
        {
            Vector3 value = values[frame];
            x[frame] = value.X;
            y[frame] = value.Y;
            z[frame] = value.Z;
        }
        return [x, y, z];
    }

    private static void WriteRotation(IAnimationClip clip, string path, Quaternion[] rotations, ReducedCurve reduced, float sampleRate)
    {
        IQuaternionCurve curve = clip.RotationCurves_C74.AddNew();
        curve.SetValues(path);
        for (int key = 0; key < reduced.Frames.Length; key++)
        {
            int frame = reduced.Frames[key];
            Quaternion value = rotations[frame];
            IKeyframe_Quaternionf keyframe = curve.Curve.Curve.AddNew();
            keyframe.Time = frame / sampleRate;
            keyframe.Value.SetValues(value.X, value.Y, value.Z, value.W);
            keyframe.InSlope.SetValues(reduced.InSlopes[0][key], reduced.InSlopes[1][key], reduced.InSlopes[2][key], reduced.InSlopes[3][key]);
            keyframe.OutSlope.SetValues(reduced.OutSlopes[0][key], reduced.OutSlopes[1][key], reduced.OutSlopes[2][key], reduced.OutSlopes[3][key]);
            keyframe.TangentMode = TangentMode.FreeFree.ToTangent(clip.Collection.Version);
            keyframe.WeightedMode = (int)WeightedMode.None;
            keyframe.InWeight?.SetValues(DefaultWeight, DefaultWeight, DefaultWeight, DefaultWeight);
            keyframe.OutWeight?.SetValues(DefaultWeight, DefaultWeight, DefaultWeight, DefaultWeight);
        }
    }

    private static void WriteVector(AssetRipper.Assets.Generics.AccessListBase<IVector3Curve> curves, AssetRipper.Primitives.UnityVersion version,
        string path, Vector3[] values, ReducedCurve reduced, float sampleRate)
    {
        IVector3Curve curve = curves.AddNew();
        curve.SetValues(path);
        for (int key = 0; key < reduced.Frames.Length; key++)
        {
            int frame = reduced.Frames[key];
            Vector3 value = values[frame];
            IKeyframe_Vector3f keyframe = curve.Curve.Curve.AddNew();
            keyframe.Time = frame / sampleRate;
            keyframe.Value.SetValues(value.X, value.Y, value.Z);
            keyframe.InSlope.SetValues(reduced.InSlopes[0][key], reduced.InSlopes[1][key], reduced.InSlopes[2][key]);
            keyframe.OutSlope.SetValues(reduced.OutSlopes[0][key], reduced.OutSlopes[1][key], reduced.OutSlopes[2][key]);
            keyframe.TangentMode = TangentMode.FreeFree.ToTangent(version);
            keyframe.WeightedMode = (int)WeightedMode.None;
            keyframe.InWeight?.SetValues(DefaultWeight, DefaultWeight, DefaultWeight);
            keyframe.OutWeight?.SetValues(DefaultWeight, DefaultWeight, DefaultWeight);
        }
    }

    private static void WriteFloat(IAnimationClip clip, ClipFloatTrack track, ReducedCurve reduced, float sampleRate)
    {
        IFloatCurve curve = clip.FloatCurves_C74.AddNew();
        curve.Path = track.Path;
        curve.Attribute = track.Attribute;
        curve.ClassID = (int)track.TargetClass;
        for (int key = 0; key < reduced.Frames.Length; key++)
        {
            int frame = reduced.Frames[key];
            IKeyframe_Single keyframe = curve.Curve.Curve.AddNew();
            keyframe.Time = frame / sampleRate;
            keyframe.Value = track.Values[frame];
            keyframe.InSlope = reduced.InSlopes[0][key];
            keyframe.OutSlope = reduced.OutSlopes[0][key];
            keyframe.TangentMode = TangentMode.FreeFree.ToTangent(clip.Collection.Version);
            keyframe.WeightedMode = (int)WeightedMode.None;
            keyframe.InWeight = DefaultWeight;
            keyframe.OutWeight = DefaultWeight;
        }
    }

    private static void Unit(Quaternion[] rotations)
    {
        for (int frame = 0; frame < rotations.Length; frame++)
        {
            Quaternion rotation = Quaternion.Normalize(rotations[frame]);
            rotations[frame] = frame > 0 && Quaternion.Dot(rotation, rotations[frame - 1]) < 0f ? -rotation : rotation;
        }
    }
}
