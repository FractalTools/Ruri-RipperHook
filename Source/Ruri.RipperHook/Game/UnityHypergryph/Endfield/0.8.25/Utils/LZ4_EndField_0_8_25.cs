namespace Ruri.RipperHook.Crypto;

public class LZ4_EndField_0_8_25 : LZ4
{
    public new static LZ4_EndField_0_8_25 Instance => new();

    /// <summary>
    /// CB3 uses LZ4Inv logic:
    /// Token: (enc & 0b11) | enc >> 2, (lit & 0b11) | lit >> 2
    /// </summary>
    protected override (int encCount, int litCount) GetLiteralToken(ReadOnlySpan<byte> cmp, ref int cmpPos)
    {
        var val = cmp[cmpPos++];
        var lit = val & 0b00110011;
        var enc = val & 0b11001100;
        enc >>= 2;

        return ((enc & 0b11) | enc >> 2, (lit & 0b11) | lit >> 2);
    }

    /// <summary>
    /// LZ4Inv uses Big Endian (or swapped) chunk end reading
    /// </summary>
    protected override int GetChunkEnd(ReadOnlySpan<byte> cmp, ref int cmpPos) => cmp[cmpPos++] << 8 | cmp[cmpPos++] << 0;
}