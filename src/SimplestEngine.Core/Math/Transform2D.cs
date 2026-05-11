namespace SimplestEngine;

/// <summary>
/// 2D affine transform (Godot-parity): 2x3 matrix as 3 Vector2 columns (X, Y, Origin).
/// </summary>
public readonly struct Transform2D : IEquatable<Transform2D>
{
    public readonly Vector2 X;
    public readonly Vector2 Y;
    public readonly Vector2 Origin;

    public static readonly Transform2D Identity = new(new Vector2(1, 0), new Vector2(0, 1), Vector2.Zero);

    public Transform2D(Vector2 x, Vector2 y, Vector2 origin) { X = x; Y = y; Origin = origin; }

    public Transform2D(float rotation, Vector2 position)
    {
        var c = MathF.Cos(rotation);
        var s = MathF.Sin(rotation);
        X = new Vector2(c, s);
        Y = new Vector2(-s, c);
        Origin = position;
    }

    public Transform2D(float rotation, Vector2 scale, Vector2 position)
    {
        var c = MathF.Cos(rotation);
        var s = MathF.Sin(rotation);
        X = new Vector2(c * scale.X, s * scale.X);
        Y = new Vector2(-s * scale.Y, c * scale.Y);
        Origin = position;
    }

    public float Rotation => MathF.Atan2(X.Y, X.X);

    public Vector2 Scale
    {
        get
        {
            var detSign = (X.X * Y.Y - X.Y * Y.X) < 0 ? -1f : 1f;
            return new Vector2(X.Length(), detSign * Y.Length());
        }
    }

    public Vector2 BasisXform(Vector2 v) => new(X.X * v.X + Y.X * v.Y, X.Y * v.X + Y.Y * v.Y);
    public Vector2 Xform(Vector2 v) => BasisXform(v) + Origin;

    public static Transform2D operator *(Transform2D a, Transform2D b) =>
        new(a.BasisXform(b.X),
            a.BasisXform(b.Y),
            a.Xform(b.Origin));

    public Transform2D AffineInverse()
    {
        var det = X.X * Y.Y - X.Y * Y.X;
        if (det == 0f) return Identity;
        var invDet = 1f / det;
        var ix = new Vector2(Y.Y * invDet, -X.Y * invDet);
        var iy = new Vector2(-Y.X * invDet, X.X * invDet);
        var io = new Vector2(-(ix.X * Origin.X + iy.X * Origin.Y),
                             -(ix.Y * Origin.X + iy.Y * Origin.Y));
        return new Transform2D(ix, iy, io);
    }

    public bool Equals(Transform2D other) => X == other.X && Y == other.Y && Origin == other.Origin;
    public override bool Equals(object? obj) => obj is Transform2D t && Equals(t);
    public override int GetHashCode() => HashCode.Combine(X, Y, Origin);
    public override string ToString() => $"[X{X} Y{Y} O{Origin}]";

    public static bool operator ==(Transform2D a, Transform2D b) => a.Equals(b);
    public static bool operator !=(Transform2D a, Transform2D b) => !a.Equals(b);
}
