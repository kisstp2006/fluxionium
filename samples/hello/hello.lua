-- Godot-style Lua script attached to Root (Node2D)
-- Demonstrates: globals, self proxy, _ready/_process, Vector2 math, prints.

extends("Node2D")

local t = 0

function _ready()
    print("[hello.lua] _ready: Hello from SimplestEngine + MoonSharp!")
    print("[hello.lua] self.modulate =", tostring(self.modulate))
end

function _process(delta)
    t = t + delta
    -- Pulse modulate color so the children (rectangles) animate.
    local intensity = 0.5 + 0.5 * math.sin(t * 2)
    self.modulate = Color(intensity, 0.6, 1.0 - intensity, 1.0)
end
