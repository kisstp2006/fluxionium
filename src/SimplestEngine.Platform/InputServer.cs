using SimplestEngine.Abi;
using Veldrid;
using KeyCode = SimplestEngine.Abi.KeyCode;
using KeyModifiers = SimplestEngine.Abi.KeyModifiers;
using MouseButton = SimplestEngine.Abi.MouseButton;

namespace SimplestEngine.Platform;

public sealed class InputServer : IInputServer, IInputMap
{
    /// <summary>One binding of an action: a key plus the modifier set that must
    /// be held simultaneously. Godot parity: a binding with shift=true triggers
    /// only while shift is pressed.</summary>
    private readonly record struct Binding(KeyCode Key, KeyModifiers Modifiers);

    private readonly HashSet<KeyCode> _pressed = new();
    private readonly HashSet<KeyCode> _justPressed = new();
    private readonly HashSet<KeyCode> _justReleased = new();
    private readonly HashSet<MouseButton> _mousePressed = new();
    private readonly Dictionary<StringName, List<Binding>> _actions = new();
    private SimplestEngine.Vector2 _mousePos;

    /// <summary>Cached modifier bitset derived from the last input snapshot.</summary>
    private KeyModifiers _activeMods;

    /// <summary>Pandemonium / Godot: <c>InputDefault::is_action_pressed</c> logs once per
    /// unknown action with <c>InputMap::suggest_actions</c> — we mirror that instead of
    /// silently returning false.</summary>
    private readonly HashSet<uint> _warnedMissingActionIds = new();

    public IReadOnlyCollection<StringName> Actions => _actions.Keys;

    public void AddAction(StringName action)
    {
        if (!_actions.ContainsKey(action)) _actions[action] = new List<Binding>();
    }
    public void RemoveAction(StringName action) => _actions.Remove(action);

    public void AddKeyEvent(StringName action, KeyCode key) => AddKeyEvent(action, key, KeyModifiers.None);
    public void AddKeyEvent(StringName action, KeyCode key, KeyModifiers mods)
    {
        AddAction(action);
        var b = new Binding(key, mods);
        if (!_actions[action].Contains(b)) _actions[action].Add(b);
    }
    public bool HasAction(StringName action) => _actions.ContainsKey(action);

    public bool IsKeyPressed(KeyCode key) => _pressed.Contains(key);

    public bool IsActionPressed(StringName action)
    {
        if (!TryGetBindingsOrWarn(action, out var bindings)) return false;
        foreach (var b in bindings)
            if (_pressed.Contains(b.Key) && ModifiersSatisfied(b.Modifiers)) return true;
        return false;
    }

    public bool IsActionJustPressed(StringName action)
    {
        if (!TryGetBindingsOrWarn(action, out var bindings)) return false;
        foreach (var b in bindings)
            if (_justPressed.Contains(b.Key) && ModifiersSatisfied(b.Modifiers)) return true;
        return false;
    }

    public bool IsActionJustReleased(StringName action)
    {
        if (!TryGetBindingsOrWarn(action, out var bindings)) return false;
        foreach (var b in bindings)
            if (_justReleased.Contains(b.Key) && ModifiersSatisfied(b.Modifiers)) return true;
        return false;
    }

    public float GetActionStrength(StringName action) => IsActionPressed(action) ? 1f : 0f;

    public SimplestEngine.Vector2 GetVector(StringName negX, StringName posX, StringName negY, StringName posY)
    {
        var x = (IsActionPressed(posX) ? 1f : 0f) - (IsActionPressed(negX) ? 1f : 0f);
        var y = (IsActionPressed(posY) ? 1f : 0f) - (IsActionPressed(negY) ? 1f : 0f);
        var v = new SimplestEngine.Vector2(x, y);
        return v.Length() > 1f ? v.Normalized() : v;
    }

    /// <summary>
    /// Pandemonium <c>input_default.cpp</c>: <c>ERR_FAIL_COND_V_MSG(!has_action, false, suggest_actions)</c>.
    /// </summary>
    private bool TryGetBindingsOrWarn(StringName action, out List<Binding> bindings)
    {
        if (_actions.TryGetValue(action, out var list))
        {
            bindings = list;
            return true;
        }
        bindings = null!;
        if (_warnedMissingActionIds.Add(action.Id))
            Console.WriteLine("[input] " + InputMapSuggestions.FormatMissingActionMessage(action.ToString(),
                Actions.Select(a => a.ToString())));
        return false;
    }

    public SimplestEngine.Vector2 GetMousePosition() => _mousePos;
    public bool IsMouseButtonPressed(MouseButton b) => _mousePressed.Contains(b);

    /// <summary>Godot semantics: an action fires when every required modifier is
    /// held. Extra modifiers being held is fine (no exact-match constraint).</summary>
    private bool ModifiersSatisfied(KeyModifiers required) =>
        (required & _activeMods) == required;

    /// <summary>Update internal state from one frame of Veldrid input events.</summary>
    public void UpdateFromVeldrid(InputSnapshot snap)
    {
        _justPressed.Clear();
        _justReleased.Clear();
        foreach (var ke in snap.KeyEvents)
        {
            var code = TranslateKey(ke.Key);
            if (code == KeyCode.None) continue;
            if (ke.Down)
            {
                if (_pressed.Add(code)) _justPressed.Add(code);
            }
            else
            {
                if (_pressed.Remove(code)) _justReleased.Add(code);
            }
        }
        foreach (var me in snap.MouseEvents)
        {
            var b = TranslateButton(me.MouseButton);
            if (me.Down) _mousePressed.Add(b);
            else _mousePressed.Remove(b);
        }
        _mousePos = new SimplestEngine.Vector2(snap.MousePosition.X, snap.MousePosition.Y);

        // Recompute the modifier bitset after the press/release set has settled.
        _activeMods = ComputeActiveModifiers();
    }

    private KeyModifiers ComputeActiveModifiers()
    {
        var m = KeyModifiers.None;
        if (_pressed.Contains(KeyCode.Shift)) m |= KeyModifiers.Shift;
        if (_pressed.Contains(KeyCode.Ctrl))  m |= KeyModifiers.Ctrl;
        if (_pressed.Contains(KeyCode.Alt))   m |= KeyModifiers.Alt;
        if (_pressed.Contains(KeyCode.Meta))  m |= KeyModifiers.Meta;
        return m;
    }

    private static KeyCode TranslateKey(Key k) => k switch
    {
        Key.A => KeyCode.A,  Key.B => KeyCode.B,  Key.C => KeyCode.C,
        Key.D => KeyCode.D,  Key.E => KeyCode.E,  Key.F => KeyCode.F,
        Key.G => KeyCode.G,  Key.H => KeyCode.H,  Key.I => KeyCode.I,
        Key.J => KeyCode.J,  Key.K => KeyCode.K,  Key.L => KeyCode.L,
        Key.M => KeyCode.M,  Key.N => KeyCode.N,  Key.O => KeyCode.O,
        Key.P => KeyCode.P,  Key.Q => KeyCode.Q,  Key.R => KeyCode.R,
        Key.S => KeyCode.S,  Key.T => KeyCode.T,  Key.U => KeyCode.U,
        Key.V => KeyCode.V,  Key.W => KeyCode.W,  Key.X => KeyCode.X,
        Key.Y => KeyCode.Y,  Key.Z => KeyCode.Z,

        Key.Number0 => KeyCode.Num0, Key.Number1 => KeyCode.Num1,
        Key.Number2 => KeyCode.Num2, Key.Number3 => KeyCode.Num3,
        Key.Number4 => KeyCode.Num4, Key.Number5 => KeyCode.Num5,
        Key.Number6 => KeyCode.Num6, Key.Number7 => KeyCode.Num7,
        Key.Number8 => KeyCode.Num8, Key.Number9 => KeyCode.Num9,

        Key.Space => KeyCode.Space,
        Key.Comma => KeyCode.Comma, Key.Period => KeyCode.Period,
        Key.Minus => KeyCode.Minus, Key.Slash => KeyCode.Slash,
        Key.Semicolon => KeyCode.Semicolon, Key.BracketLeft => KeyCode.LeftBracket,
        Key.BracketRight => KeyCode.RightBracket, Key.BackSlash => KeyCode.Backslash,
        Key.Grave => KeyCode.GraveAccent,

        Key.Left => KeyCode.Left, Key.Right => KeyCode.Right,
        Key.Up => KeyCode.Up, Key.Down => KeyCode.Down,
        Key.PageUp => KeyCode.PageUp, Key.PageDown => KeyCode.PageDown,
        Key.Home => KeyCode.Home, Key.End => KeyCode.End,

        Key.Escape => KeyCode.Escape, Key.Enter => KeyCode.Enter,
        Key.Tab => KeyCode.Tab, Key.BackSpace => KeyCode.Backspace,
        Key.Insert => KeyCode.Insert, Key.Delete => KeyCode.Delete,
        Key.CapsLock => KeyCode.CapsLock, Key.ScrollLock => KeyCode.ScrollLock,
        Key.NumLock => KeyCode.NumLock, Key.PrintScreen => KeyCode.PrintScreen,
        Key.Pause => KeyCode.Pause,

        Key.ShiftLeft or Key.ShiftRight => KeyCode.Shift,
        Key.ControlLeft or Key.ControlRight => KeyCode.Ctrl,
        Key.AltLeft or Key.AltRight => KeyCode.Alt,
        Key.WinLeft or Key.WinRight => KeyCode.Meta,

        Key.F1 => KeyCode.F1, Key.F2 => KeyCode.F2, Key.F3 => KeyCode.F3,
        Key.F4 => KeyCode.F4, Key.F5 => KeyCode.F5, Key.F6 => KeyCode.F6,
        Key.F7 => KeyCode.F7, Key.F8 => KeyCode.F8, Key.F9 => KeyCode.F9,
        Key.F10 => KeyCode.F10, Key.F11 => KeyCode.F11, Key.F12 => KeyCode.F12,

        _ => KeyCode.None,
    };

    private static MouseButton TranslateButton(Veldrid.MouseButton b) => b switch
    {
        Veldrid.MouseButton.Left => MouseButton.Left,
        Veldrid.MouseButton.Right => MouseButton.Right,
        Veldrid.MouseButton.Middle => MouseButton.Middle,
        _ => MouseButton.Left,
    };
}
