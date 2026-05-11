using System.Runtime.InteropServices;

namespace SimplestEngine;

/// <summary>
/// Per-frame transient memory. Reset at frame start, allocations are zero-cost.
/// Used by RenderCommand stream, deferred call arg buffers, temp NodePath parse, etc.
/// </summary>
public sealed class FrameAllocator
{
    private readonly List<byte[]> _buffers = new();
    private int _currentBuffer;
    private int _currentOffset;
    private readonly int _bufferSize;

    public FrameAllocator(int bufferSize = 64 * 1024)
    {
        _bufferSize = bufferSize;
        _buffers.Add(new byte[bufferSize]);
    }

    public Span<T> Allocate<T>(int count) where T : unmanaged
    {
        int sizeBytes;
        unsafe { sizeBytes = sizeof(T) * count; }
        if (sizeBytes > _bufferSize)
        {
            var big = new byte[sizeBytes];
            _buffers.Add(big);
            _currentBuffer = _buffers.Count - 1;
            _currentOffset = sizeBytes;
            return MemoryMarshal.Cast<byte, T>(big.AsSpan(0, sizeBytes));
        }

        if (_currentOffset + sizeBytes > _bufferSize)
        {
            _currentBuffer++;
            _currentOffset = 0;
            if (_currentBuffer >= _buffers.Count)
                _buffers.Add(new byte[_bufferSize]);
        }

        var buf = _buffers[_currentBuffer];
        var span = MemoryMarshal.Cast<byte, T>(buf.AsSpan(_currentOffset, sizeBytes));
        _currentOffset += sizeBytes;
        return span;
    }

    public void Reset()
    {
        _currentBuffer = 0;
        _currentOffset = 0;
    }

    public int BytesUsedInFrame => _currentBuffer * _bufferSize + _currentOffset;
}
