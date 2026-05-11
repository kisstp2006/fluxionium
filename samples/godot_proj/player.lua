extends("Node2D")

local speed = 220

function _ready()
    print("[Player] ready at position", self.position)
end

function _process(delta)
    local dx = 0
    local dy = 0
    if Input.is_action_pressed("move_left")  then dx = dx - 1 end
    if Input.is_action_pressed("move_right") then dx = dx + 1 end
    if Input.is_action_pressed("ui_up")      then dy = dy - 1 end
    if Input.is_action_pressed("ui_down")    then dy = dy + 1 end

    local pos = self.position
    pos.x = pos.x + dx * speed * delta
    pos.y = pos.y + dy * speed * delta
    self.position = pos

    if Input.is_action_just_pressed("jump") then
        print("[Player] jump!")
    end
end
