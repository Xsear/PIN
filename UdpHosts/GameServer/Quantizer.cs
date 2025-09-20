using System;
using System.Numerics;

namespace GameServer;

public static class Quantizer
{
    public static byte[] PackVector3(Vector3 vec)
    {
        byte[] packed = new byte[3];
        packed[0] = PackComponent(vec.X);
        packed[1] = PackComponent(vec.Y);
        packed[2] = PackComponent(vec.Z);
        return packed;
    }

    private static byte PackComponent(float value)
    {
        // Clamp to [-1, 1]
        float clamped = MathF.Max(-1.0f, MathF.Min(1.0f, value));
        float abs = MathF.Abs(clamped);

        // Quantize to 0–127
        int quantized = (int)(abs * 127.0f + 0.5f);
        byte magnitude = (byte)(quantized & 0x7F);

        // Extract sign bit
        byte sign = (byte)((BitConverter.SingleToInt32Bits(value) >> 31) & 0x1);

        // Store sign in MSB
        return (byte)((sign << 7) | magnitude);
    }

    public static Vector3 UnpackVector3(byte[] packed)
    {
        if (packed.Length < 3)
            throw new ArgumentException("Packed vector must be 3 bytes.");

        float x = UnpackComponent(packed[0]);
        float y = UnpackComponent(packed[1]);
        float z = UnpackComponent(packed[2]);

        return new Vector3(x, y, z);
    }

    private static float UnpackComponent(byte b)
    {
        int sign = (b >> 7) & 0x1;
        int magnitude = b & 0x7F;

        float value = magnitude / 127.0f;
        return sign == 1 ? -value : value;
    }

    public static sbyte[] PackVector3ToSBytes(Vector3 vec)
    {
        return new sbyte[]
        {
        (sbyte)PackComponent(vec.X),
        (sbyte)PackComponent(vec.Y),
        (sbyte)PackComponent(vec.Z)
        };
    }

    public static Vector3 UnpackSBytesToVector3(sbyte b1, sbyte b2, sbyte b3)
    {
        return new Vector3(
            UnpackComponent(b1),
            UnpackComponent(b2),
            UnpackComponent(b3)
        );
    }

    private static float UnpackComponent(sbyte value)
    {
        byte b = (byte)value; // Avoid sign extension
        int sign = (b >> 7) & 0x1;
        int magnitude = b & 0x7F;

        float result = magnitude / 127.0f;
        return sign == 1 ? -result : result;
    }
}