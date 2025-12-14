namespace Ruri.RipperHook.Crypto;

public class ExAstris_LZ4 : LZ4
{
    public new static ExAstris_LZ4 Instance { get; } = new();
    protected override (int encCount, int litCount) GetLiteralToken(ReadOnlySpan<byte> cmp, ref int cmpPos) => ((cmp[cmpPos] >> 4) & 0xf, (cmp[cmpPos++] >> 0) & 0xf);
}