namespace SimplestEngine;

/// <summary>Pandemonium parity: simple Timer node emitting `timeout`.</summary>
[GDClass("Timer", "Node")]
[GDSignal("timeout")]
public class Timer : Node
{
    public float WaitTime { get; set; } = 1f;
    public bool OneShot { get; set; }
    public bool Autostart { get; set; }
    public bool Paused { get; set; }
    public float TimeLeft { get; private set; }

    public bool IsStopped => TimeLeft <= 0f;

    public void Start(float seconds = -1f)
    {
        TimeLeft = seconds > 0 ? seconds : WaitTime;
        Paused = false;
    }

    public void Stop() { TimeLeft = 0f; }

    public override void _Ready()
    {
        if (Autostart) Start();
    }

    public override void _Process(float delta)
    {
        if (Paused || TimeLeft <= 0f) return;
        TimeLeft -= delta;
        if (TimeLeft <= 0f)
        {
            EmitSignal(StringName.Get("timeout"));
            if (OneShot) TimeLeft = 0f;
            else TimeLeft = WaitTime;
        }
    }
}
