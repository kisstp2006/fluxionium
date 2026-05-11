namespace SimplestEngine.Abi;

/// <summary>
/// Engine-level key identifier. Numeric values are deliberately spaced into
/// regions that keep Godot 3 parity legible: ASCII (0x20..0x7E) maps 1:1,
/// "system" keys (arrows, function keys, navigation) get GLFW-style high IDs
/// matching the values <see cref="SimplestEngine.Platform.InputServer"/> emits
/// from Veldrid's <c>Key</c> enum.
/// </summary>
public enum KeyCode
{
    None = 0,

    // ASCII printable range (32..126) — matches Godot 3's plain ASCII keycodes.
    Space = 32,
    Apostrophe = 39,
    Comma = 44, Minus = 45, Period = 46, Slash = 47,
    Num0 = 48, Num1, Num2, Num3, Num4, Num5, Num6, Num7, Num8, Num9,
    Semicolon = 59,
    Equal = 61,
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    LeftBracket = 91, Backslash = 92, RightBracket = 93, GraveAccent = 96,

    // High-tier engine keys (mirrors GLFW). Veldrid's TranslateKey already maps
    // its enum onto this range, and we re-export it to script land verbatim.
    Escape = 256, Enter = 257, Tab = 258, Backspace = 259, Insert = 260, Delete = 261,
    Right = 262, Left = 263, Down = 264, Up = 265,
    PageUp = 266, PageDown = 267, Home = 268, End = 269,
    CapsLock = 280, ScrollLock = 281, NumLock = 282, PrintScreen = 283, Pause = 284,

    F1 = 290, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    // Modifier keys exist as both KeyCode (for raw key-pressed queries)
    // and as bit flags in KeyModifiers (for action bindings).
    Shift = 340, Ctrl = 341, Alt = 342, Meta = 343,
}

/// <summary>
/// Modifier bits attached to a Godot InputEventKey action binding. Godot
/// parity: an action with shift=true triggers only while shift is held.
/// Stored as flags so a single binding can require multiple modifiers.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Meta = 1 << 3,
}

public enum MouseButton { Left = 1, Right = 2, Middle = 3 }

public interface IInputServer
{
    bool IsKeyPressed(KeyCode key);
    /// <summary>True when the action exists on the InputMap (even with zero key bindings).</summary>
    bool HasAction(StringName action);
    bool IsActionPressed(StringName action);
    bool IsActionJustPressed(StringName action);
    bool IsActionJustReleased(StringName action);
    float GetActionStrength(StringName action);
    Vector2 GetVector(StringName negX, StringName posX, StringName negY, StringName posY);
    Vector2 GetMousePosition();
    bool IsMouseButtonPressed(MouseButton b);
}

public interface IInputMap
{
    void AddAction(StringName action);
    void RemoveAction(StringName action);
    /// <summary>Adds a plain key event (no modifier requirements).</summary>
    void AddKeyEvent(StringName action, KeyCode key);
    /// <summary>Adds a key event that only triggers while the listed modifiers are pressed.</summary>
    void AddKeyEvent(StringName action, KeyCode key, KeyModifiers mods);
    bool HasAction(StringName action);
    IReadOnlyCollection<StringName> Actions { get; }
}
