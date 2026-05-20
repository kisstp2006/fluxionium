using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// Pandemonium parity base: a 2D node that owns a physics body and any number
/// of CollisionShape2D / CollisionPolygon2D children that configure its fixtures.
/// </summary>
[GDClass("CollisionObject2D", "Node2D")]
public abstract class CollisionObject2D : Node2D
{
    private Rid _body;
    private bool _bodyValid;
    private uint _collisionLayer = 1;
    private uint _collisionMask = 1;

    public Rid PhysicsBodyRid => _body;
    public IPhysicsBackend? Physics => Tree?.PhysicsBackend;

    public uint CollisionLayer
    {
        get => _collisionLayer;
        set { _collisionLayer = value; if (_bodyValid) Physics?.BodySetCollisionLayer(_body, value); }
    }
    public uint CollisionMask
    {
        get => _collisionMask;
        set { _collisionMask = value; if (_bodyValid) Physics?.BodySetCollisionMask(_body, value); }
    }

    protected abstract BodyType GetBodyType();
    protected virtual bool IsSensor() => false;

    public override void _EnterTree()
    {
        base._EnterTree();
        if (Physics is null) return;
        _body = Physics.BodyCreate(GetBodyType());
        _bodyValid = true;
        Physics.BodySetUserData(_body, this);
        Physics.BodySetCollisionLayer(_body, _collisionLayer);
        Physics.BodySetCollisionMask(_body, _collisionMask);
        Physics.BodySetIsSensor(_body, IsSensor());
        Physics.BodySetTransform(_body, GlobalTransform);
        AttachChildShapes();
    }

    public override void _ExitTree()
    {
        if (_bodyValid && Physics is not null) Physics.BodyFree(_body);
        _bodyValid = false;
        base._ExitTree();
    }

    public override void _PhysicsProcess(float delta)
    {
        base._PhysicsProcess(delta);
        if (!_bodyValid || Physics is null) return;
        // Pull simulated transform back to the node (for Dynamic / Kinematic bodies).
        if (GetBodyType() != BodyType.Static)
        {
            var xform = Physics.BodyGetTransform(_body);
            // Convert global -> local (assume direct parent is also Node2D or root)
            if (Parent is Node2D parentN2D)
                Transform = parentN2D.GlobalTransform.AffineInverse() * xform;
            else
                Transform = xform;
        }
        else
        {
            // Static: push our transform to the simulation if it moved by hand
            Physics.BodySetTransform(_body, GlobalTransform);
        }
    }

    internal void AttachChildShapes()
    {
        if (!_bodyValid || Physics is null) return;
        foreach (var c in Children) AttachShapesRec(c);
    }

    private void AttachShapesRec(Node node)
    {
        if (node is CollisionShape2D cs) cs.AttachTo(this);
        if (node is CollisionPolygon2D cp) cp.AttachTo(this);
        foreach (var c in node.Children) AttachShapesRec(c);
    }
}

[GDClass("PhysicsBody2D", "CollisionObject2D")]
public abstract class PhysicsBody2D : CollisionObject2D { }

[GDClass("StaticBody2D", "PhysicsBody2D")]
public class StaticBody2D : PhysicsBody2D
{
    protected override BodyType GetBodyType() => BodyType.Static;
}

[GDClass("RigidBody2D", "PhysicsBody2D")]
[GDSignal("body_entered")]
[GDSignal("body_exited")]
public class RigidBody2D : PhysicsBody2D
{
    protected override BodyType GetBodyType() => BodyType.Dynamic;

    public Vector2 LinearVelocity
    {
        get => Physics?.BodyGetLinearVelocity(PhysicsBodyRid) ?? Vector2.Zero;
        set => Physics?.BodySetLinearVelocity(PhysicsBodyRid, value);
    }
}

[GDClass("KinematicBody2D", "PhysicsBody2D")]
public class KinematicBody2D : PhysicsBody2D
{
    private Vector2 _lastVelocity;
    private Vector2 _floorNormal = new(0, -1);
    private bool _onFloor;
    private bool _onWall;
    private bool _onCeiling;
    private float _safeMargin = 0.08f;

    protected override BodyType GetBodyType() => BodyType.Kinematic;

    /// <summary>Godot parity: move_and_slide. Returns the remaining velocity.</summary>
    public Vector2 MoveAndSlide(
        Vector2 velocity,
        Vector2? upDirection = null,
        bool stopOnSlope = false,
        int maxSlides = 4,
        float floorMaxAngle = 0.7853982f,
        bool infiniteInertia = true)
    {
        _lastVelocity = velocity;
        Physics?.BodySetLinearVelocity(PhysicsBodyRid, velocity);
        RefreshContactState(upDirection ?? new Vector2(0, -1), floorMaxAngle);
        return velocity;
    }

    /// <summary>Godot 3 parity: move_and_slide_with_snap.</summary>
    public Vector2 MoveAndSlideWithSnap(
        Vector2 velocity,
        Vector2 snap,
        Vector2? upDirection = null,
        bool stopOnSlope = false,
        int maxSlides = 4,
        float floorMaxAngle = 0.7853982f,
        bool infiniteInertia = true)
    {
        var up = upDirection ?? new Vector2(0, -1);
        var result = MoveAndSlide(velocity, up, stopOnSlope, maxSlides, floorMaxAngle, infiniteInertia);

        if (snap != Vector2.Zero && Physics is not null)
        {
            var target = GlobalPosition + snap;
            if (Physics.Raycast(GlobalPosition, target, CollisionMask, out _, out var normal, out _))
            {
                _onFloor = IsFloorNormal(normal, up, floorMaxAngle);
                if (_onFloor) _floorNormal = normal == Vector2.Zero ? up : normal;
            }
        }

        return result;
    }

    public bool IsOnFloor() => _onFloor;
    public bool IsOnWall() => _onWall;
    public bool IsOnCeiling() => _onCeiling;
    public Vector2 GetFloorNormal() => _floorNormal;
    public float SafeMargin { get => _safeMargin; set => _safeMargin = MathF.Max(0f, value); }

    public override void _PhysicsProcess(float delta)
    {
        base._PhysicsProcess(delta);
        RefreshContactState(new Vector2(0, -1), 0.7853982f);
    }

    private void RefreshContactState(Vector2 upDirection, float floorMaxAngle)
    {
        _onFloor = _onWall = _onCeiling = false;
        _floorNormal = upDirection;
        if (Physics is null) return;

        var pos = GlobalPosition;
        var probe = MathF.Max(2f, _safeMargin * 16f);
        if (Physics.Raycast(pos, pos - upDirection * probe, CollisionMask, out _, out var floorNormal, out _))
        {
            _onFloor = IsFloorNormal(floorNormal, upDirection, floorMaxAngle);
            _floorNormal = floorNormal == Vector2.Zero ? upDirection : floorNormal;
        }
        if (Physics.Raycast(pos, pos + upDirection * probe, CollisionMask, out _, out _, out _))
            _onCeiling = true;

        var right = new Vector2(-upDirection.Y, upDirection.X);
        _onWall =
            Physics.Raycast(pos, pos + right * probe, CollisionMask, out _, out _, out _) ||
            Physics.Raycast(pos, pos - right * probe, CollisionMask, out _, out _, out _);
    }

    private static bool IsFloorNormal(Vector2 normal, Vector2 upDirection, float floorMaxAngle)
    {
        if (normal == Vector2.Zero) return true;
        var n = normal.Normalized();
        var up = upDirection == Vector2.Zero ? new Vector2(0, -1) : upDirection.Normalized();
        var dot = Math.Clamp(n.Dot(up), -1f, 1f);
        return MathF.Acos(dot) <= floorMaxAngle;
    }
}

/// <summary>Godot 4 alias for KinematicBody2D.</summary>
[GDClass("CharacterBody2D", "KinematicBody2D")]
public class CharacterBody2D : KinematicBody2D
{
    public Vector2 Velocity { get; set; }
    public Vector2 MoveAndSlide() => base.MoveAndSlide(Velocity);
}

[GDClass("Area2D", "CollisionObject2D")]
[GDSignal("body_entered")]
[GDSignal("body_exited")]
[GDSignal("area_entered")]
[GDSignal("area_exited")]
public class Area2D : CollisionObject2D
{
    protected override BodyType GetBodyType() => BodyType.Static;
    protected override bool IsSensor() => true;
}

[GDClass("Shape2D", "Resource")]
public abstract class Shape2D : Resource
{
    public abstract Rid CreateInBackend(IPhysicsBackend backend);
}

[GDClass("CircleShape2D", "Shape2D")]
public sealed class CircleShape2D : Shape2D
{
    public float Radius { get; set; } = 10f;
    public override Rid CreateInBackend(IPhysicsBackend backend) => backend.ShapeCreateCircle(Radius);
}

[GDClass("RectangleShape2D", "Shape2D")]
public sealed class RectangleShape2D : Shape2D
{
    public Vector2 Extents { get; set; } = new(10f, 10f);
    public override Rid CreateInBackend(IPhysicsBackend backend) => backend.ShapeCreateRectangle(Extents);
}

/// <summary>Pandemonium parity: child of CollisionObject2D that adds a single shape.</summary>
[GDClass("CollisionShape2D", "Node2D")]
public sealed class CollisionShape2D : Node2D
{
    public Shape2D? Shape { get; set; }
    public bool Disabled { get; set; }
    public bool OneWayCollision { get; set; }
    public float OneWayCollisionMargin { get; set; } = 1f;
    private Rid _shapeRid;
    private Rid _fixture;
    private CollisionObject2D? _attachedTo;

    public override void _EnterTree()
    {
        base._EnterTree();
        if (Parent is CollisionObject2D co) AttachTo(co);
    }

    public override void _ExitTree()
    {
        if (_attachedTo is not null && _fixture.IsValid && _attachedTo.Physics is not null)
        {
            _attachedTo.Physics.BodyDetachShape(_attachedTo.PhysicsBodyRid, _fixture);
            _attachedTo.Physics.ShapeFree(_shapeRid);
        }
        _attachedTo = null;
        base._ExitTree();
    }

    internal void AttachTo(CollisionObject2D parent)
    {
        if (_attachedTo == parent || Shape is null || Disabled) return;
        if (parent.Physics is null || !parent.PhysicsBodyRid.IsValid) return;
        _attachedTo = parent;
        _shapeRid = Shape.CreateInBackend(parent.Physics);
        _fixture = parent.Physics.BodyAttachShape(parent.PhysicsBodyRid, _shapeRid, Transform);
    }
}

[GDClass("CollisionPolygon2D", "Node2D")]
public sealed class CollisionPolygon2D : Node2D
{
    public Vector2[] Polygon { get; set; } = Array.Empty<Vector2>();
    public bool Disabled { get; set; }
    private Rid _shapeRid;
    private Rid _fixture;
    private CollisionObject2D? _attachedTo;

    internal void AttachTo(CollisionObject2D parent)
    {
        if (_attachedTo == parent || Disabled || Polygon.Length < 3) return;
        if (parent.Physics is null || !parent.PhysicsBodyRid.IsValid) return;
        _attachedTo = parent;
        _shapeRid = parent.Physics.ShapeCreatePolygon(Polygon);
        _fixture = parent.Physics.BodyAttachShape(parent.PhysicsBodyRid, _shapeRid, Transform);
    }

    public override void _ExitTree()
    {
        if (_attachedTo is not null && _fixture.IsValid && _attachedTo.Physics is not null)
        {
            _attachedTo.Physics.BodyDetachShape(_attachedTo.PhysicsBodyRid, _fixture);
            _attachedTo.Physics.ShapeFree(_shapeRid);
        }
        _attachedTo = null;
        base._ExitTree();
    }
}
