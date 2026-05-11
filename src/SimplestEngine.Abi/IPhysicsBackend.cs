namespace SimplestEngine.Abi;

public enum BodyType { Static, Kinematic, Dynamic }

public readonly struct CollisionInfo
{
    public readonly Rid OtherBody;
    public readonly Vector2 Position;
    public readonly Vector2 Normal;
    public CollisionInfo(Rid other, Vector2 pos, Vector2 normal)
    { OtherBody = other; Position = pos; Normal = normal; }
}

/// <summary>Backend-agnostic 2D physics. Wraps Aether / Box2D / custom.</summary>
public interface IPhysicsBackend
{
    void Step(float deltaSeconds);

    // bodies
    Rid BodyCreate(BodyType type);
    void BodyFree(Rid body);
    void BodySetTransform(Rid body, Transform2D xform);
    Transform2D BodyGetTransform(Rid body);
    void BodySetLinearVelocity(Rid body, Vector2 v);
    Vector2 BodyGetLinearVelocity(Rid body);
    void BodySetAngularVelocity(Rid body, float w);
    void BodySetCollisionLayer(Rid body, uint layer);
    void BodySetCollisionMask(Rid body, uint mask);
    void BodySetIsSensor(Rid body, bool isSensor);
    void BodySetUserData(Rid body, object? userData);
    object? BodyGetUserData(Rid body);

    // shapes (attached to a body)
    Rid ShapeCreateCircle(float radius);
    Rid ShapeCreateRectangle(Vector2 halfExtents);
    Rid ShapeCreatePolygon(ReadOnlySpan<Vector2> vertices);
    void ShapeFree(Rid shape);

    Rid BodyAttachShape(Rid body, Rid shape, Transform2D localXform);
    void BodyDetachShape(Rid body, Rid fixture);

    // queries
    bool Raycast(Vector2 from, Vector2 to, uint mask, out Vector2 hit, out Vector2 normal, out Rid hitBody);

    // collision events (drained each frame by SceneTree)
    IReadOnlyList<(Rid a, Rid b, CollisionInfo info)> ConsumeCollisionEnters();
    IReadOnlyList<(Rid a, Rid b)> ConsumeCollisionExits();
}
