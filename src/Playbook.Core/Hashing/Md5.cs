using System.Buffers.Binary;
using System.Numerics;

namespace Playbook.Core.Hashing;

/// <summary>
/// MD5 (RFC 1321) in managed code. Playbook derives stable ids from MD5 (player ids from Sleeper ids,
/// news and fact ids), and the browser build's runtime has no MD5, so every caller uses this instead
/// of <c>System.Security.Cryptography.MD5</c>. The output is byte-for-byte the same on every platform.
/// Used for ids only, never for security.
/// </summary>
public static class Md5
{
    private static readonly int[] Shifts =
    [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
    ];

    private static readonly uint[] K = BuildConstants();

    public static byte[] HashData(byte[] data) => HashData(data.AsSpan());

    public static byte[] HashData(ReadOnlySpan<byte> data)
    {
        // Message + 0x80 + zero padding + 64-bit little-endian bit length, to a multiple of 64 bytes.
        var paddedLength = ((data.Length + 8) / 64 + 1) * 64;
        var message = new byte[paddedLength];
        data.CopyTo(message);
        message[data.Length] = 0x80;
        BinaryPrimitives.WriteUInt64LittleEndian(message.AsSpan(paddedLength - 8), (ulong)data.Length * 8);

        uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;
        Span<uint> m = stackalloc uint[16];
        for (var offset = 0; offset < paddedLength; offset += 64)
        {
            for (var i = 0; i < 16; i++)
            {
                m[i] = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(offset + i * 4, 4));
            }

            uint a = a0, b = b0, c = c0, d = d0;
            for (var i = 0; i < 64; i++)
            {
                uint f;
                int g;
                switch (i / 16)
                {
                    case 0:
                        f = (b & c) | (~b & d);
                        g = i;
                        break;
                    case 1:
                        f = (d & b) | (~d & c);
                        g = (5 * i + 1) % 16;
                        break;
                    case 2:
                        f = b ^ c ^ d;
                        g = (3 * i + 5) % 16;
                        break;
                    default:
                        f = c ^ (b | ~d);
                        g = 7 * i % 16;
                        break;
                }

                f = f + a + K[i] + m[g];
                a = d;
                d = c;
                c = b;
                b += BitOperations.RotateLeft(f, Shifts[i]);
            }

            a0 += a;
            b0 += b;
            c0 += c;
            d0 += d;
        }

        var digest = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(0), a0);
        BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(4), b0);
        BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(8), c0);
        BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(12), d0);
        return digest;
    }

    private static uint[] BuildConstants()
    {
        var k = new uint[64];
        for (var i = 0; i < 64; i++)
        {
            k[i] = (uint)(long)Math.Floor(Math.Abs(Math.Sin(i + 1)) * 4294967296.0);
        }

        return k;
    }
}
