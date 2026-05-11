namespace SimplestEngine;

/// <summary>Pandemonium parity: 2D scene node with transform.</summary>
[GDClass("Node2D", "CanvasItem")]
public class Node2D : CanvasItem
{
    private Vector2 _position = Vector2.Zero;
    private float _rotation = 0f;
    private Vector2 _scale = Vector2.One;
    private bool _xformDirty = true;
    private Transform2D _localTransform = Transform2D.Identity;

    public Vector2 Position
    {
        get => _position;
        set { _position = value; _xformDirty = true; OnXformChanged(); }
    }

    public float Rotation
    {
        get => _rotation;
        set { _rotation = value; _xformDirty = true; OnXformChanged(); }
    }

    public float RotationDegrees
    {
        get => _rotation * 180f / MathF.PI;
        set => Rotation = value * MathF.PI / 180f;
    }

    public Vector2 Scale
    {
        get => _scale;
        set { _scale = value; _xformDirty = true; OnXformChanged(); }
    }

    public Transform2D Transform
    {
        get
        {
            if (_xformDirty)
            {
                _localTransform = new Transform2D(_rotation, _scale, _position);
                _xformDirty = false;
            }
            return _localTransform;
        }
        set
        {
            _localTransform = value;
            _position = value.Origin;
            _rotation = value.Rotation;
            _scale = value.Scale;
            _xformDirty = false;
            OnXformChanged();
        }
    }

    public Transform2D GlobalTransform
    {
        get
        {
            if (Parent is Node2D p) return p.GlobalTransform * Transform;
            return Transform;
        }
    }

    public Vector2 GlobalPosition
    {
        get => GlobalTransform.Origin;
        set
        {
            if (Parent is Node2D p) Position = p.GlobalTransform.AffineInverse().Xform(value);
            else Position = value;
        }
    }

    public void Translate(Vector2 amount) => Position += amount;
    public void Rotate(float radians) => Rotation += radians;
    public void ApplyScale(Vector2 ratio) => Scale = new Vector2(Scale.X * ratio.X, Scale.Y * ratio.Y);

    public Vector2 ToLocal(Vector2 globalPoint) => GlobalTransform.AffineInverse().Xform(globalPoint);
    public Vector2 ToGlobal(Vector2 localPoint) => GlobalTransform.Xform(localPoint);

    private void OnXformChanged()
    {
        if (CanvasItemRid.IsValid)
            RenderingServer?.CanvasItemSetTransform(CanvasItemRid, Transform);
        Notification(Notifications.NotificationTransformChanged);
        Notification(Notifications.NotificationLocalTransformChanged);
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        if (CanvasItemRid.IsValid)
            RenderingServer?.CanvasItemSetTransform(CanvasItemRid, Transform);
    }
}
