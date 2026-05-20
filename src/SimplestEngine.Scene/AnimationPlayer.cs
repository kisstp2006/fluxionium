namespace SimplestEngine;

[GDClass("Animation", "Resource")]
public sealed class Animation : Resource
{
    public float Length { get; set; } = 1f;
    public bool Loop { get; set; }
    public List<ValueTrack> ValueTracks { get; } = new();
}

public sealed class ValueTrack
{
    public NodePath Path { get; set; } = new(".");
    public float[] Times { get; set; } = Array.Empty<float>();
    public Variant[] Values { get; set; } = Array.Empty<Variant>();
}

[GDClass("AnimationPlayer", "Node")]
public sealed class AnimationPlayer : Node
{
    private readonly Dictionary<StringName, Animation> _animations = new();
    private StringName _current = StringName.Empty;
    private float _position;
    private bool _playing;

    public string Autoplay { get; set; } = string.Empty;
    public int PlaybackProcessMode { get; set; }

    public void AddAnimation(StringName name, Animation animation) => _animations[name] = animation;
    public bool HasAnimation(StringName name) => _animations.ContainsKey(name);

    public void Play(StringName name)
    {
        if (!_animations.ContainsKey(name)) return;
        _current = name;
        _position = 0f;
        _playing = true;
        ApplyCurrent();
    }

    public void Stop()
    {
        _playing = false;
        _position = 0f;
    }

    public override void _Ready()
    {
        base._Ready();
        if (!string.IsNullOrEmpty(Autoplay))
            Play(StringName.Get(Autoplay));
    }

    public override void _Process(float delta)
    {
        base._Process(delta);
        if (!_playing || !_animations.TryGetValue(_current, out var animation)) return;

        _position += delta;
        if (animation.Length > 0 && _position > animation.Length)
        {
            if (animation.Loop) _position %= animation.Length;
            else
            {
                _position = animation.Length;
                _playing = false;
            }
        }
        Apply(animation, _position);
    }

    private void ApplyCurrent()
    {
        if (_animations.TryGetValue(_current, out var animation))
            Apply(animation, _position);
    }

    private void Apply(Animation animation, float time)
    {
        foreach (var track in animation.ValueTracks)
        {
            if (track.Times.Length == 0 || track.Values.Length == 0) continue;
            var value = Sample(track, time);
            ApplyTrack(track.Path, value);
        }
    }

    private static Variant Sample(ValueTrack track, float time)
    {
        int count = Math.Min(track.Times.Length, track.Values.Length);
        if (count == 0) return Variant.Nil;
        if (time <= track.Times[0]) return track.Values[0];

        for (int i = 1; i < count; i++)
        {
            if (time > track.Times[i]) continue;
            var a = track.Values[i - 1];
            var b = track.Values[i];
            var t0 = track.Times[i - 1];
            var t1 = track.Times[i];
            var weight = t1 <= t0 ? 0f : Math.Clamp((time - t0) / (t1 - t0), 0f, 1f);
            return LerpVariant(a, b, weight);
        }
        return track.Values[count - 1];
    }

    private static Variant LerpVariant(Variant a, Variant b, float t)
    {
        if (a.Type == VariantType.Vector2 && b.Type == VariantType.Vector2)
            return Variant.From(a.AsVector2() + (b.AsVector2() - a.AsVector2()) * t);
        if ((a.Type == VariantType.Float || a.Type == VariantType.Int) &&
            (b.Type == VariantType.Float || b.Type == VariantType.Int))
            return Variant.From(a.AsFloat() + (b.AsFloat() - a.AsFloat()) * t);
        return t < 1f ? a : b;
    }

    private void ApplyTrack(NodePath path, Variant value)
    {
        var raw = path.ToString();
        var colon = raw.LastIndexOf(':');
        if (colon < 0) return;
        var nodePath = raw[..colon];
        var property = raw[(colon + 1)..];
        var target = Parent?.GetNodeOrNull(new NodePath(string.IsNullOrEmpty(nodePath) ? "." : nodePath));
        target?.Set(StringName.Get(property), value);
    }
}
