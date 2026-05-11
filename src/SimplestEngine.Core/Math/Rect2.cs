namespace SimplestEngine;

/// <summary>Axis-aligned rectangle (position + size). Godot-parity.</summary>
public readonly struct Rect2 : IEquatable<Rect2>
{
    public readonly Vector2 Position;
    public readonly Vector2 Size;

    public Vector2 End => Position + Size;
    public Vector2 Center => Position + Size * 0.5f;
    public float Area => Size.X * Size.Y;

    public Rect2(Vector2 position, Vector2 size) { Position = position; Size = size; }
    public Rect2(float x, float y, float w, float h)
    { Position = new Vector2(x, y); Size = new Vector2(w, h); }

    public bool HasPoint(Vector2 point) =>
        point.X >= Position.X && point.X < Position.X + Size.X &&
        point.Y >= Position.Y && point.Y < Position.Y + Size.Y;

    public bool Intersects(Rect2 other) =>
        Position.X < other.Position.X + other.Size.X &&
        Position.X + Size.X > other.Position.X &&
        Position.Y < other.Position.Y + other.Size.Y &&
        Position.Y + Size.Y > other.Position.Y;

    public Rect2 Merge(Rect2 other)
    {
        var minX = MathF.Min(Position.X, other.Position.X);
        var minY = MathF.Min(Position.Y, other.Position.Y);
        var maxX = MathF.Max(Position.X + Size.X, other.Position.X + other.Size.X);
        var maxY = MathF.Max(Position.Y + Size.Y, other.Position.Y + other.Size.Y);
        return new Rect2(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
    }

    public static bool operator ==(Rect2 a, Rect2 b) => a.Position == b.Position && a.Size == b.Size;
    public static bool operator !=(Rect2 a, Rect2 b) => !(a == b);

    public bool Equals(Rect2 other) => Position == other.Position && Size == other.Size;
    public override bool Equals(object? obj) => obj is Rect2 r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Position, Size);
    public override string ToString() => $"[P{Position} S{Size}]";
}
