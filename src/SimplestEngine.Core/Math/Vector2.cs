using System.Runtime.CompilerServices;

namespace SimplestEngine;

/// <summary>
/// 2D vector with Godot-parity API. Inline storable in <see cref="Variant"/>.
/// </summary>
public readonly struct Vector2 : IEquatable<Vector2>
{
    public readonly float X;
    public readonly float Y;

    public static readonly Vector2 Zero = new(0f, 0f);
    public static readonly Vector2 One = new(1f, 1f);
    public static readonly Vector2 Up = new(0f, -1f);
    public static readonly Vector2 Down = new(0f, 1f);
    public static readonly Vector2 Left = new(-1f, 0f);
    public static readonly Vector2 Right = new(1f, 0f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2(float x, float y) { X = x; Y = y; }

    public float Length() => MathF.Sqrt(X * X + Y * Y);
    public float LengthSquared() => X * X + Y * Y;
    public float DistanceTo(Vector2 other) => (this - other).Length();
    public float DistanceSquaredTo(Vector2 other) => (this - other).LengthSquared();
    public float AngleTo(Vector2 other) => MathF.Atan2(Cross(other), Dot(other));
    public float Angle() => MathF.Atan2(Y, X);
    public float Dot(Vector2 other) => X * other.X + Y * other.Y;
    public float Cross(Vector2 other) => X * other.Y - Y * other.X;
    public Vector2 Abs() => new(MathF.Abs(X), MathF.Abs(Y));
    public Vector2 Normalized()
    {
        var len = Length();
        return len > 0f ? new Vector2(X / len, Y / len) : Zero;
    }
    public Vector2 Rotated(float angle)
    {
        var c = MathF.Cos(angle); var s = MathF.Sin(angle);
        return new Vector2(X * c - Y * s, X * s + Y * c);
    }
    public Vector2 Lerp(Vector2 to, float weight) =>
        new(X + (to.X - X) * weight, Y + (to.Y - Y) * weight);

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);
    public static Vector2 operator *(Vector2 a, float s) => new(a.X * s, a.Y * s);
    public static Vector2 operator *(float s, Vector2 a) => new(a.X * s, a.Y * s);
    public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);
    public static Vector2 operator /(Vector2 a, float s) => new(a.X / s, a.Y / s);
    public static Vector2 operator /(Vector2 a, Vector2 b) => new(a.X / b.X, a.Y / b.Y);

    public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

    public bool Equals(Vector2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Vector2 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
