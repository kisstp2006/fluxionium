using System.Runtime.CompilerServices;

namespace SimplestEngine;

/// <summary>
/// Resource ID used by servers (RenderingServer, PhysicsServer, ...).
/// 64-bit handle: lower 32 bits = slot index, upper 32 bits = generation.
/// </summary>
public readonly struct Rid : IEquatable<Rid>
{
    public readonly ulong Value;

    public static readonly Rid Invalid = default;

    internal Rid(ulong v) { Value = v; }

    public uint Index { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (uint)Value; }
    public uint Generation { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (uint)(Value >> 32); }

    public bool IsValid => Value != 0;

    public bool Equals(Rid other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Rid r && r.Value == Value;
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(Rid a, Rid b) => a.Value == b.Value;
    public static bool operator !=(Rid a, Rid b) => a.Value != b.Value;
    public override string ToString() => $"RID(idx={Index}, gen={Generation})";
}

/// <summary>
/// Generic slot allocator with generations. Server impls subclass and store payloads.
/// </summary>
public sealed class RidAllocator<T> where T : class
{
    private struct Slot { public uint Generation; public bool InUse; public T? Payload; }

    private readonly List<Slot> _slots = new();
    private readonly Queue<uint> _free = new();
    private readonly object _lock = new();

    public Rid Allocate(T payload)
    {
        lock (_lock)
        {
            uint idx;
            if (_free.Count > 0)
            {
                idx = _free.Dequeue();
                var s = _slots[(int)idx];
                s.InUse = true;
                s.Payload = payload;
                s.Generation++;
                _slots[(int)idx] = s;
            }
            else
            {
                idx = (uint)_slots.Count;
                _slots.Add(new Slot { Generation = 1, InUse = true, Payload = payload });
            }
            var gen = _slots[(int)idx].Generation;
            return new Rid(((ulong)gen << 32) | idx);
        }
    }

    public bool TryGet(Rid rid, out T? payload)
    {
        lock (_lock)
        {
            if (!rid.IsValid || rid.Index >= _slots.Count) { payload = null; return false; }
            var s = _slots[(int)rid.Index];
            if (!s.InUse || s.Generation != rid.Generation) { payload = null; return false; }
            payload = s.Payload;
            return true;
        }
    }

    public T? Get(Rid rid) => TryGet(rid, out var p) ? p : null;

    public bool Free(Rid rid)
    {
        lock (_lock)
        {
            if (!rid.IsValid || rid.Index >= _slots.Count) return false;
            var s = _slots[(int)rid.Index];
            if (!s.InUse || s.Generation != rid.Generation) return false;
            s.InUse = false;
            s.Payload = null;
            _slots[(int)rid.Index] = s;
            _free.Enqueue(rid.Index);
            return true;
        }
    }

    public IEnumerable<(Rid rid, T payload)> Enumerate()
    {
        lock (_lock)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.InUse && s.Payload is not null)
                    yield return (new Rid(((ulong)s.Generation << 32) | (uint)i), s.Payload);
            }
        }
    }
}
