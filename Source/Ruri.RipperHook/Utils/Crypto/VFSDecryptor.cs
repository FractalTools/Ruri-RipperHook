using AssetRipper.IO.Endian;

namespace Ruri.RipperHook.Crypto;

public record VFSDecryptor : CommonDecryptor
{
    private static readonly byte[] VFSAESSBox = { 0xA2, 0xB7, 0x25, 0x47, 0x4E, 0x82, 0x17, 0xD0, 0xA1, 0x62, 0x86, 0x66, 0xBD, 0x33, 0xA0, 0x3F, 0x91, 0x00, 0x40, 0x54, 0xD4, 0x7D, 0x94, 0xB0, 0x12, 0x58, 0x36, 0x83, 0x93, 0x9A, 0x52, 0x69, 0x08, 0x71, 0x07, 0x0A, 0x39, 0x01, 0xD7, 0x65, 0x6F, 0x27, 0x6C, 0xD8, 0x5F, 0xFC, 0xE2, 0x55, 0x95, 0xA4, 0xC2, 0x8E, 0x5B, 0x72, 0x0E, 0xD3, 0xC6, 0xD9, 0xD2, 0xDE, 0x57, 0xCE, 0xCA, 0x60, 0x19, 0x13, 0x7F, 0x84, 0xB5, 0x5A, 0x56, 0x77, 0xF4, 0x06, 0xE5, 0x2A, 0x37, 0x38, 0x9D, 0x50, 0xE0, 0x5C, 0xA7, 0xDA, 0xF5, 0x99, 0x3A, 0x0D, 0x75, 0x4A, 0x0F, 0x5E, 0xE6, 0xE8, 0x96, 0x20, 0xCF, 0x6E, 0x1B, 0x9C, 0xEF, 0xE9, 0xFD, 0x6A, 0xF6, 0x74, 0xA5, 0x48, 0x85, 0x59, 0x14, 0xFE, 0xF7, 0x9E, 0x73, 0x16, 0x8C, 0x46, 0x8A, 0x21, 0xAC, 0x26, 0x89, 0xBF, 0xBE, 0xCB, 0xFF, 0x05, 0xC9, 0xF3, 0x51, 0x4F, 0xC0, 0xDF, 0x0B, 0xAD, 0x42, 0x6D, 0x92, 0xC8, 0x28, 0x70, 0xEB, 0x0C, 0x67, 0x76, 0x09, 0xC7, 0x34, 0x30, 0x41, 0xDC, 0x45, 0x97, 0x9F, 0xAF, 0xEC, 0xA3, 0x81, 0xF9, 0xE3, 0x4B, 0x1D, 0xB1, 0x7B, 0xFB, 0xAE, 0x7E, 0xC5, 0x24, 0xEA, 0x79, 0x87, 0x8F, 0x35, 0x2D, 0x61, 0x02, 0xDB, 0x98, 0xC1, 0xF8, 0xBC, 0xD6, 0x68, 0xA9, 0xB6, 0x49, 0xFA, 0x32, 0xE1, 0xB2, 0xE4, 0x3C, 0x88, 0xAA, 0x15, 0xF1, 0x1E, 0xB3, 0x29, 0x04, 0x2C, 0xA8, 0x1A, 0x43, 0xE7, 0xCD, 0x3E, 0xBB, 0x22, 0x4C, 0x6B, 0xF0, 0x8D, 0x7A, 0x44, 0x5D, 0x3D, 0xB4, 0xCC, 0x7C, 0x2B, 0x31, 0xC4, 0x90, 0xF2, 0x1C, 0x23, 0x64, 0xB8, 0x3B, 0xD5, 0x9B, 0x10, 0xC3, 0xED, 0xA6, 0x53, 0xAB, 0x4D, 0x78, 0xD1, 0xBA, 0xEE, 0x18, 0x2E, 0x2F, 0x1F, 0xDD, 0x80, 0x8B, 0xB9, 0x03, 0x11, 0x63 };
    private static readonly byte[] VFSAESKey = { 0x8F, 0xC2, 0x22, 0xD0, 0x91, 0xCB, 0xE6, 0x8F, 0xB1, 0xF6, 0x61, 0xCE, 0x91, 0x5C, 0xFF, 0x54 };
    private static readonly byte[] VFSAESIV = { 0x67, 0x23, 0x94, 0xEF, 0x4C, 0xD5, 0x2F, 0x76, 0xFF, 0xDE, 0x7B, 0xB0, 0x6A, 0x86, 0x62, 0x5C };

    public override Span<byte> Decrypt(Span<byte> buffer)
    {
        // VFS Stride Logic
        if (buffer.Length <= 256)
        {
            var dec = AESDecrypt(buffer.ToArray());
            dec.CopyTo(buffer);
        }
        else
        {
            var numBlocksFloor = buffer.Length / 16;
            var step = (int)(256 / numBlocksFloor);

            if (numBlocksFloor > 256)
                step = 1;

            Span<byte> decBuffer = new byte[256];

            // Sample bytes
            for (int i = 0; i < Math.Min(numBlocksFloor, 256); i++)
                buffer.Slice(i * 16, step).CopyTo(decBuffer.Slice(i * step, step));

            // Decrypt sampled buffer
            var decrypted = AESDecrypt(decBuffer.ToArray());

            // Write back bytes
            for (int i = 0; i < Math.Min(numBlocksFloor, 256); i++)
                decrypted.AsSpan(i * step, step).CopyTo(buffer.Slice(i * 16, step));
        }

        return buffer;
    }

    public static bool IsValidHeader(EndianReader reader)
    {
        var pos = reader.BaseStream.Position;
        var originalEndian = reader.EndianType;

        try
        {
            reader.EndianType = EndianType.BigEndian;
            if (reader.BaseStream.Length - pos < 8) return false;

            var a = reader.ReadUInt32();
            var b = reader.ReadUInt32();

            var c1 = ((a ^ 0x91A64750) >> 3) ^ ((a ^ 0x91A64750) << 29);
            var c2 = (c1 << 16) ^ 0xD5F9BECC;
            var c3 = (c1 ^ c2) & 0xFFFFFFFF;

            return b == c3;
        }
        catch
        {
            return false;
        }
        finally
        {
            reader.BaseStream.Position = pos;
            reader.EndianType = originalEndian;
        }
    }

    public static uint BitConcat(int bits, params uint[] ns)
    {
        uint mask = (bits == 32) ? 0xFFFFFFFFu : ((1u << bits) - 1u);
        uint res = 0;
        int count = ns.Length;
        for (int i = 0; i < count; i++)
            res |= (ns[i] & mask) << (bits * (count - i - 1));
        return res;
    }

    public static ulong BitConcat64(int bits, params ulong[] ns)
    {
        ulong mask = (bits == 64) ? ulong.MaxValue : ((1UL << bits) - 1UL);
        ulong res = 0;
        int count = ns.Length;
        for (int i = 0; i < count; i++)
            res |= (ns[i] & mask) << (bits * (count - i - 1));
        return res;
    }

    public static ushort RotateLeft16(ushort value, int count) => (ushort)((value << count) | (value >> (16 - count)));
    public static ulong RotateLeft64(ulong value, int count) => (value << count) | (value >> (64 - count));

    #region Internal AES Implementation
    private static byte[] AESDecrypt(byte[] ciphertext)
    {
        byte[] iv = VFSAESIV;
        List<byte[]> blocks = new();
        byte[] previous = iv.ToArray();
        foreach (var ct in SplitBlocks(ciphertext))
        {
            byte[] block = EncryptBlock(previous);
            byte[] pt = new byte[ct.Length];
            for (int i = 0; i < ct.Length; i++) pt[i] = (byte)(ct[i] ^ block[i]);
            blocks.Add(pt);
            byte[] nextIv = new byte[16];
            int count = 0;
            for (int i = 0; i < 16; i++)
            {
                ulong shiftSrc = 0xDEA1BEEF2AF3BA0EUL >> (count & 0x38);
                byte temp = (byte)(block[i] ^ (31 * i) ^ (byte)shiftSrc);
                count += 8;
                temp = (byte)((((temp >> 5) | (8 * temp)) & 0xFF));
                temp = VFSAESSBox[temp];
                nextIv[i] = temp;
            }
            previous = nextIv;
        }
        return blocks.SelectMany(b => b).ToArray();
    }

    private static byte[] EncryptBlock(byte[] plaintext)
    {
        List<byte[,]> keyMats = ExpandKey();
        int nRounds = 10;
        var state = BytesToMatrix(plaintext);
        AddRoundKey(state, keyMats[0]);
        for (int r = 1; r < nRounds; r++)
        {
            SubBytes(state);
            ShiftRows(state);
            MixColumns(state);
            AddRoundKey(state, keyMats[r]);
        }
        SubBytes(state);
        ShiftRows(state);
        AddRoundKey(state, keyMats[^1]);
        return MatrixToBytes(state);
    }

    private static IEnumerable<byte[]> SplitBlocks(byte[] msg, int blockSize = 16)
    {
        for (int i = 0; i < msg.Length; i += blockSize)
            yield return msg.Skip(i).Take(blockSize).ToArray();
    }

    private static void SubBytes(List<byte[]> s)
    {
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                s[i][j] = VFSAESSBox[s[i][j]];
    }

    private static void ShiftRows(List<byte[]> s)
    {
        (s[0][1], s[1][1], s[2][1], s[3][1]) = (s[1][1], s[2][1], s[3][1], s[0][1]);
        (s[0][2], s[1][2], s[2][2], s[3][2]) = (s[2][2], s[3][2], s[0][2], s[1][2]);
        (s[0][3], s[1][3], s[2][3], s[3][3]) = (s[3][3], s[0][3], s[1][3], s[2][3]);
    }

    private static void AddRoundKey(List<byte[]> s, byte[,] k)
    {
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                s[i][j] ^= k[i, j];
    }

    private static byte XTime(byte a) => (byte)(((a & 0x80) != 0) ? ((a << 1) ^ 0x1B) & 0xFF : (a << 1));

    private static void MixSingleColumn(byte[] a)
    {
        byte t = (byte)(a[0] ^ a[1] ^ a[2] ^ a[3]);
        byte u = a[0];
        a[0] ^= (byte)(t ^ XTime((byte)(a[0] ^ a[1])));
        a[1] ^= (byte)(t ^ XTime((byte)(a[1] ^ a[2])));
        a[2] ^= (byte)(t ^ XTime((byte)(a[2] ^ a[3])));
        a[3] ^= (byte)(t ^ XTime((byte)(a[3] ^ u)));
    }

    private static void MixColumns(List<byte[]> s)
    {
        for (int i = 0; i < 4; i++)
            MixSingleColumn(s[i]);
    }

    private static List<byte[,]> ExpandKey()
    {
        int nRounds = 10;
        byte[] masterKey = VFSAESKey;
        List<byte[]> keyCols = BytesToMatrix(masterKey);
        int iterationSize = masterKey.Length / 4;
        int i = 1;
        byte[] rCon = { 0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80, 0x1B, 0x36, 0x6C, 0xD8, 0xAB, 0x4D, 0x9A, 0x2F, 0x5E, 0xBC, 0x63, 0xC6, 0x97, 0x35, 0x6A, 0xD4, 0xB3, 0x7D, 0xFA, 0xEF, 0xC5, 0x91, 0x39 };
        while (keyCols.Count < (nRounds + 1) * 4)
        {
            byte[] word = keyCols[^1].ToArray();
            if (keyCols.Count % iterationSize == 0)
            {
                var first = word[0];
                Array.Copy(word, 1, word, 0, word.Length - 1);
                word[^1] = first;
                for (int k = 0; k < 4; k++) word[k] = VFSAESSBox[word[k]];
                word[0] ^= rCon[i];
                i++;
            }
            else if (masterKey.Length == 32 && keyCols.Count % iterationSize == 4)
            {
                for (int k = 0; k < 4; k++) word[k] = VFSAESSBox[word[k]];
            }
            byte[] prev = keyCols[keyCols.Count - iterationSize];
            for (int k = 0; k < 4; k++) word[k] ^= prev[k];
            keyCols.Add(word);
        }
        var res = new List<byte[,]>();
        for (int x = 0; x < keyCols.Count / 4; x++)
        {
            byte[,] m = new byte[4, 4];
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    m[c, r] = keyCols[x * 4 + c][r];
            res.Add(m);
        }
        return res;
    }

    private static List<byte[]> BytesToMatrix(byte[] text)
    {
        var rows = new List<byte[]>();
        for (int i = 0; i < text.Length; i += 4)
            rows.Add(new byte[] { text[i], text[i + 1], text[i + 2], text[i + 3] });
        return rows;
    }

    private static byte[] MatrixToBytes(List<byte[]> m)
    {
        byte[] r = new byte[16];
        int idx = 0;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                r[idx++] = m[i][j];
        return r;
    }
    #endregion
}