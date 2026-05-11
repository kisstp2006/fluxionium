using SimplestEngine.Abi;
using nkast.Aether.Physics2D.Collision.Shapes;
using nkast.Aether.Physics2D.Dynamics;
using nkast.Aether.Physics2D.Common;
using AVec = nkast.Aether.Physics2D.Common.Vector2;
using SVec = SimplestEngine.Vector2;
using BodyType = SimplestEngine.Abi.BodyType;
using AetherBodyType = nkast.Aether.Physics2D.Dynamics.BodyType;

namespace SimplestEngine.Physics.Aether;

/// <summary>
/// Aether.Physics2D adapter behind <see cref="IPhysicsBackend"/>.
/// Engine API stays backend-agnostic — Aether-specific types never leak out.
/// </summary>
public sealed class AetherBackend : IPhysicsBackend
{
    private readonly World _world = new(new AVec(0, 980f));
    private readonly RidAllocator<BodyHandle> _bodies = new();
    private readonly RidAllocator<ShapeHandle> _shapes = new();
    private readonly RidAllocator<FixtureHandle> _fixtures = new();

    private readonly List<(Rid a, Rid b, CollisionInfo info)> _enters = new();
    private readonly List<(Rid a, Rid b)> _exits = new();

    public AetherBackend()
    {
        _world.ContactManager.BeginContact += OnBegin;
        _world.ContactManager.EndContact += OnEnd;
    }

    private bool OnBegin(nkast.Aether.Physics2D.Dynamics.Contacts.Contact c)
    {
        if (c.FixtureA.Tag is Rid aFx && c.FixtureB.Tag is Rid bFx
            && _fixtures.TryGet(aFx, out var aFh) && _fixtures.TryGet(bFx, out var bFh)
            && aFh is not null && bFh is not null)
        {
            _enters.Add((aFh.Body, bFh.Body, new CollisionInfo(bFh.Body,
                new SVec(c.Manifold.LocalPoint.X, c.Manifold.LocalPoint.Y),
                new SVec(c.Manifold.LocalNormal.X, c.Manifold.LocalNormal.Y))));
        }
        return true;
    }

    private void OnEnd(nkast.Aether.Physics2D.Dynamics.Contacts.Contact c)
    {
        if (c.FixtureA.Tag is Rid aFx && c.FixtureB.Tag is Rid bFx
            && _fixtures.TryGet(aFx, out var aFh) && _fixtures.TryGet(bFx, out var bFh)
            && aFh is not null && bFh is not null)
            _exits.Add((aFh.Body, bFh.Body));
    }

    public void Step(float deltaSeconds) => _world.Step(deltaSeconds);

    public Rid BodyCreate(BodyType type)
    {
        var b = _world.CreateBody();
        b.BodyType = type switch
        {
            BodyType.Static => AetherBodyType.Static,
            BodyType.Kinematic => AetherBodyType.Kinematic,
            BodyType.Dynamic => AetherBodyType.Dynamic,
            _ => AetherBodyType.Static,
        };
        return _bodies.Allocate(new BodyHandle { Body = b });
    }

    public void BodyFree(Rid body)
    {
        if (_bodies.TryGet(body, out var h) && h is not null) _world.Remove(h.Body);
        _bodies.Free(body);
    }

    public void BodySetTransform(Rid body, Transform2D xform)
    {
        if (!_bodies.TryGet(body, out var h) || h is null) return;
        h.Body.Position = new AVec(xform.Origin.X, xform.Origin.Y);
        h.Body.Rotation = xform.Rotation;
    }

    public Transform2D BodyGetTransform(Rid body)
    {
        if (!_bodies.TryGet(body, out var h) || h is null) return Transform2D.Identity;
        return new Transform2D(h.Body.Rotation, SVec.One, new SVec(h.Body.Position.X, h.Body.Position.Y));
    }

    public void BodySetLinearVelocity(Rid body, SVec v)
    {
        if (_bodies.TryGet(body, out var h) && h is not null) h.Body.LinearVelocity = new AVec(v.X, v.Y);
    }

    public SVec BodyGetLinearVelocity(Rid body)
    {
        if (!_bodies.TryGet(body, out var h) || h is null) return SVec.Zero;
        return new SVec(h.Body.LinearVelocity.X, h.Body.LinearVelocity.Y);
    }

    public void BodySetAngularVelocity(Rid body, float w)
    {
        if (_bodies.TryGet(body, out var h) && h is not null) h.Body.AngularVelocity = w;
    }

    public void BodySetCollisionLayer(Rid body, uint layer)
    {
        if (!_bodies.TryGet(body, out var h) || h is null) return;
        foreach (var f in h.Body.FixtureList) f.CollisionCategories = (Category)layer;
    }
    public void BodySetCollisionMask(Rid body, uint mask)
    {
        if (!_bodies.TryGet(body, out var h) || h is null) return;
        foreach (var f in h.Body.FixtureList) f.CollidesWith = (Category)mask;
    }
    public void BodySetIsSensor(Rid body, bool isSensor)
    {
        if (!_bodies.TryGet(body, out var h) || h is null) return;
        foreach (var f in h.Body.FixtureList) f.IsSensor = isSensor;
    }
    public void BodySetUserData(Rid body, object? userData)
    {
        if (_bodies.TryGet(body, out var h) && h is not null) h.UserData = userData;
    }
    public object? BodyGetUserData(Rid body) =>
        _bodies.TryGet(body, out var h) ? h?.UserData : null;

    public Rid ShapeCreateCircle(float radius) =>
        _shapes.Allocate(new ShapeHandle { Shape = new CircleShape(radius, 1f) });

    public Rid ShapeCreateRectangle(SVec halfExtents)
    {
        var verts = new Vertices(4)
        {
            new AVec(-halfExtents.X, -halfExtents.Y),
            new AVec( halfExtents.X, -halfExtents.Y),
            new AVec( halfExtents.X,  halfExtents.Y),
            new AVec(-halfExtents.X,  halfExtents.Y),
        };
        return _shapes.Allocate(new ShapeHandle { Shape = new PolygonShape(verts, 1f) });
    }

    public Rid ShapeCreatePolygon(ReadOnlySpan<SVec> vertices)
    {
        var verts = new Vertices(vertices.Length);
        foreach (var v in vertices) verts.Add(new AVec(v.X, v.Y));
        return _shapes.Allocate(new ShapeHandle { Shape = new PolygonShape(verts, 1f) });
    }

    public void ShapeFree(Rid shape) => _shapes.Free(shape);

    public Rid BodyAttachShape(Rid body, Rid shape, Transform2D localXform)
    {
        if (!_bodies.TryGet(body, out var bh) || bh is null) return default;
        if (!_shapes.TryGet(shape, out var sh) || sh is null) return default;
        var fx = bh.Body.CreateFixture(sh.Shape);
        var fixRid = _fixtures.Allocate(new FixtureHandle { Body = body, Fixture = fx });
        fx.Tag = fixRid;
        return fixRid;
    }

    public void BodyDetachShape(Rid body, Rid fixture)
    {
        if (_fixtures.TryGet(fixture, out var fh) && fh is not null
            && _bodies.TryGet(body, out var bh) && bh is not null)
            bh.Body.Remove(fh.Fixture);
        _fixtures.Free(fixture);
    }

    public bool Raycast(SVec from, SVec to, uint mask, out SVec hit, out SVec normal, out Rid hitBody)
    {
        var resultHit = false;
        var localHit = default(AVec);
        var localNorm = default(AVec);
        Rid local = default;
        _world.RayCast((Fixture fx, AVec p, AVec n, float fr) =>
        {
            if (fx.Tag is Rid fr2 && _fixtures.TryGet(fr2, out var fh) && fh is not null)
            {
                resultHit = true;
                localHit = p; localNorm = n; local = fh.Body;
            }
            return fr;
        }, new AVec(from.X, from.Y), new AVec(to.X, to.Y));
        hit = new SVec(localHit.X, localHit.Y);
        normal = new SVec(localNorm.X, localNorm.Y);
        hitBody = local;
        return resultHit;
    }

    public IReadOnlyList<(Rid a, Rid b, CollisionInfo info)> ConsumeCollisionEnters()
    {
        var copy = _enters.ToArray();
        _enters.Clear();
        return copy;
    }

    public IReadOnlyList<(Rid a, Rid b)> ConsumeCollisionExits()
    {
        var copy = _exits.ToArray();
        _exits.Clear();
        return copy;
    }
}

internal sealed class BodyHandle { public Body Body = null!; public object? UserData; }
internal sealed class ShapeHandle { public Shape Shape = null!; }
internal sealed class FixtureHandle { public Rid Body; public Fixture Fixture = null!; }
