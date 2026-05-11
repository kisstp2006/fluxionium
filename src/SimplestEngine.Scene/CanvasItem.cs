using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// Pandemonium parity: base of all 2D drawable nodes (Node2D, Control).
/// Holds visibility, modulate, z-index, canvas-item RID, and emits the
/// draw command stream through the rendering server.
/// </summary>
[GDClass("CanvasItem", "Node")]
public class CanvasItem : Node
{
    private bool _visible = true;
    private Color _modulate = Color.White;
    private Color _selfModulate = Color.White;
    private int _zIndex;
    private bool _zRelative = true;
    private bool _updatePending;
    internal Rid _canvasItemRid;

    public IRenderingServer? RenderingServer { get; set; }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            if (_canvasItemRid.IsValid)
                RenderingServer?.CanvasItemSetVisible(_canvasItemRid, value);
            Notification(Notifications.NotificationVisibilityChanged);
        }
    }

    public Color Modulate
    {
        get => _modulate;
        set { _modulate = value; QueueRedraw(); }
    }

    public Color SelfModulate
    {
        get => _selfModulate;
        set { _selfModulate = value; QueueRedraw(); }
    }

    public int ZIndex
    {
        get => _zIndex;
        set
        {
            _zIndex = value;
            if (_canvasItemRid.IsValid)
                RenderingServer?.CanvasItemSetZIndex(_canvasItemRid, value);
        }
    }

    public bool ZAsRelative { get => _zRelative; set => _zRelative = value; }

    public Rid CanvasItemRid => _canvasItemRid;

    public override void _EnterTree()
    {
        base._EnterTree();
        if (RenderingServer is not null && !_canvasItemRid.IsValid)
        {
            _canvasItemRid = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetVisible(_canvasItemRid, _visible);
            if (_zIndex != 0) RenderingServer.CanvasItemSetZIndex(_canvasItemRid, _zIndex);
        }
        Notification(Notifications.NotificationEnterCanvas);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (_canvasItemRid.IsValid)
        {
            RenderingServer?.CanvasItemFree(_canvasItemRid);
            _canvasItemRid = default;
        }
        Notification(Notifications.NotificationExitCanvas);
    }

    /// <summary>Marks the item for re-recording its draw command stream this frame.</summary>
    public void QueueRedraw()
    {
        _updatePending = true;
    }

    internal void RecordDraw()
    {
        if (!_updatePending || !_canvasItemRid.IsValid || RenderingServer is null) return;
        RenderingServer.CanvasItemClear(_canvasItemRid);
        Notification(Notifications.NotificationDraw);
        _Draw();
        _updatePending = false;
    }
}
