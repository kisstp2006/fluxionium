namespace SimplestEngine;

/// <summary>Integer Vector2 (Godot parity for sizes, tile coords, etc.).</summary>
public readonly struct Vector2i : IEquatable<Vector2i>
{
    public readonly int X;
    public readonly int Y;

    public static readonly Vector2i Zero = new(0, 0);
    public static readonly Vector2i One = new(1, 1);

    public Vector2i(int x, int y) { X = x; Y = y; }

    public static Vector2i operator +(Vector2i a, Vector2i b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2i operator -(Vector2i a, Vector2i b) => new(a.X - b.X, a.Y - b.Y);
    public static bool operator ==(Vector2i a, Vector2i b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2i a, Vector2i b) => !(a == b);

    public static implicit operator Vector2(Vector2i v) => new(v.X, v.Y);

    public bool Equals(Vector2i other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Vector2i v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
}
