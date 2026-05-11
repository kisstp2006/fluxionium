namespace SimplestEngine;

/// <summary>RGBA float color (0..1). Godot parity.</summary>
public readonly struct Color : IEquatable<Color>
{
    public readonly float R;
    public readonly float G;
    public readonly float B;
    public readonly float A;

    public static readonly Color White = new(1f, 1f, 1f, 1f);
    public static readonly Color Black = new(0f, 0f, 0f, 1f);
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);
    public static readonly Color Red = new(1f, 0f, 0f, 1f);
    public static readonly Color Green = new(0f, 1f, 0f, 1f);
    public static readonly Color Blue = new(0f, 0f, 1f, 1f);

    public Color(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }

    public static Color FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    public static Color FromHtml(string code)
    {
        var s = code.StartsWith('#') ? code[1..] : code;
        if (s.Length == 6)
            return FromBytes(Convert.ToByte(s[..2], 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16));
        if (s.Length == 8)
            return FromBytes(Convert.ToByte(s[..2], 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16), Convert.ToByte(s.Substring(6, 2), 16));
        return Black;
    }

    public Color WithAlpha(float a) => new(R, G, B, a);
    public Color Lerp(Color to, float weight) =>
        new(R + (to.R - R) * weight,
            G + (to.G - G) * weight,
            B + (to.B - B) * weight,
            A + (to.A - A) * weight);

    public uint ToRgba32()
    {
        byte r = (byte)Math.Clamp(R * 255f, 0f, 255f);
        byte g = (byte)Math.Clamp(G * 255f, 0f, 255f);
        byte b = (byte)Math.Clamp(B * 255f, 0f, 255f);
        byte a = (byte)Math.Clamp(A * 255f, 0f, 255f);
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    public static bool operator ==(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    public static bool operator !=(Color a, Color b) => !(a == b);
    public static Color operator *(Color a, float s) => new(a.R * s, a.G * s, a.B * s, a.A * s);
    public static Color operator *(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);

    public bool Equals(Color other) => this == other;
    public override bool Equals(object? obj) => obj is Color c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public override string ToString() => $"({R}, {G}, {B}, {A})";
}
