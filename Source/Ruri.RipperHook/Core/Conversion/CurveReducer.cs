namespace Ruri.RipperHook.Conversion;

/// <summary>
/// The keys a dense sampling needs to reproduce itself within a tolerance on the curve Unity
/// will play: a cubic Hermite through the kept keys, each key's slope taken from its dense
/// neighbours (exactly what the clip builder writes). Refined where the played curve strays
/// furthest from a sample, segment by segment, until no sample strays beyond the tolerance;
/// the first and last samples always stay. A motion sampled at a high rate by its source keeps
/// only the keys that carry it.
/// </summary>
public static class CurveReducer
{
    /// <summary>The error of the sample at <paramref name="sample"/> when the curve runs straight from key <paramref name="first"/> to key <paramref name="last"/>.</summary>
    public delegate float SegmentError(int first, int last, int sample);

    public static int[] Keep(int count, float tolerance, SegmentError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (count <= 2)
        {
            return All(count);
        }
        List<int> kept = new() { 0, count - 1 };
        Stack<(int First, int Last)> pending = new();
        pending.Push((0, count - 1));
        while (pending.Count > 0)
        {
            (int first, int last) = pending.Pop();
            if (last - first < 2)
            {
                continue;
            }
            int worst = -1;
            float worstError = tolerance;
            for (int sample = first + 1; sample < last; sample++)
            {
                float current = error(first, last, sample);
                if (current > worstError)
                {
                    worstError = current;
                    worst = sample;
                }
            }
            if (worst < 0)
            {
                continue;
            }
            kept.Add(worst);
            pending.Push((first, worst));
            pending.Push((worst, last));
        }
        kept.Sort();
        return kept.ToArray();
    }

    public static int[] All(int count)
    {
        int[] all = new int[count];
        for (int index = 0; index < count; index++)
        {
            all[index] = index;
        }
        return all;
    }

    /// <summary>Cubic Hermite between two keys <paramref name="span"/> seconds apart, at fraction <paramref name="fraction"/> of the way, with the first key's outgoing slope and the second key's incoming slope.</summary>
    public static float Hermite(float first, float firstSlope, float last, float lastSlope, float span, float fraction)
    {
        float squared = fraction * fraction;
        float cubed = squared * fraction;
        float h00 = 2f * cubed - 3f * squared + 1f;
        float h10 = cubed - 2f * squared + fraction;
        float h01 = -2f * cubed + 3f * squared;
        float h11 = cubed - squared;
        return h00 * first + h10 * span * firstSlope + h01 * last + h11 * span * lastSlope;
    }
}
