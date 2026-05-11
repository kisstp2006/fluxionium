namespace SimplestEngine;

/// <summary>
/// Engine notification constants. Values match Pandemonium / Godot 3.x parity
/// so original .tscn files referencing them via index still work.
/// </summary>
public static class Notifications
{
    // MainLoop notifications (Pandemonium scene/main/main_loop.h)
    public const int NotificationWmMouseEnter = 1002;
    public const int NotificationWmMouseExit = 1003;
    public const int NotificationWmFocusIn = 1004;
    public const int NotificationWmFocusOut = 1005;
    public const int NotificationWmQuitRequest = 1006;
    public const int NotificationOsMemoryWarning = 1009;
    public const int NotificationCrash = 1012;

    // Object notifications
    public const int NotificationPostinitialize = 0;
    public const int NotificationPredelete = 1;

    // Node notifications (Pandemonium scene/main/node.h)
    public const int NotificationEnterTree = 10;
    public const int NotificationExitTree = 11;
    public const int NotificationChildOrderChanged = 12;
    public const int NotificationReady = 13;
    public const int NotificationPaused = 14;
    public const int NotificationUnpaused = 15;
    public const int NotificationPhysicsProcess = 16;
    public const int NotificationProcess = 17;
    public const int NotificationParented = 18;
    public const int NotificationUnparented = 19;
    public const int NotificationInstanced = 20;
    public const int NotificationDragBegin = 21;
    public const int NotificationDragEnd = 22;
    public const int NotificationPathChanged = 23;
    public const int NotificationInternalProcess = 25;
    public const int NotificationInternalPhysicsProcess = 26;
    public const int NotificationPostEnterTree = 27;
    public const int NotificationResetPhysicsInterpolation = 28;

    // CanvasItem notifications
    public const int NotificationTransformChanged = 2000;
    public const int NotificationDraw = 30;
    public const int NotificationVisibilityChanged = 31;
    public const int NotificationEnterCanvas = 32;
    public const int NotificationExitCanvas = 33;
    public const int NotificationLocalTransformChanged = 35;
    public const int NotificationWorldChanged = 36;
}
