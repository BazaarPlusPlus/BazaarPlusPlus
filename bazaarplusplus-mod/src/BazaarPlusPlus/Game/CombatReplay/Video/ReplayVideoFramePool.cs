#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Fixed-size, exact-length (<c>width*height*4</c>) <see cref="byte"/> array free-list used to
/// eliminate the per-frame ~14MB allocation in the capture pipeline. Single-producer /
/// single-consumer: <see cref="Rent"/> is called on the main (capture) thread and
/// <see cref="Return"/> on the encoder writer thread. The backing
/// <see cref="ConcurrentQueue{T}"/> provides lock-free correctness; no exceptions ever escape.
/// </summary>
internal sealed class ReplayVideoFramePool
{
    private readonly int _frameByteLength;
    private readonly int _maxBuffers;
    private readonly ConcurrentQueue<byte[]> _free = new();
    private int _liveCount;

    /// <param name="frameByteLength">
    /// Exact byte length of every pooled buffer (<c>width*height*4</c>); must be &gt; 0.
    /// </param>
    /// <param name="maxBuffers">
    /// Upper bound on simultaneously live buffers; clamped to a minimum of 1.
    /// </param>
    public ReplayVideoFramePool(int frameByteLength, int maxBuffers)
    {
        if (frameByteLength <= 0)
            throw new ArgumentException(
                "Frame byte length must be positive.",
                nameof(frameByteLength)
            );

        _frameByteLength = frameByteLength;
        _maxBuffers = Math.Max(1, maxBuffers);
    }

    /// <summary>Exact byte length guaranteed for every buffer returned by <see cref="Rent"/>.</summary>
    public int FrameByteLength => _frameByteLength;

    /// <summary>
    /// Rents a buffer of exactly <see cref="FrameByteLength"/> bytes. MAIN thread only. Reuses a
    /// free buffer when available, otherwise allocates a new one while under the cap. Returns
    /// <c>null</c> when the live-buffer cap is hit (the caller drops the frame). Never blocks,
    /// never throws.
    /// </summary>
    public byte[]? Rent()
    {
        if (_free.TryDequeue(out var buffer))
            return buffer;

        if (Interlocked.Increment(ref _liveCount) <= _maxBuffers)
            return new byte[_frameByteLength];

        Interlocked.Decrement(ref _liveCount);
        return null;
    }

    /// <summary>
    /// Returns a buffer to the pool. WRITER thread only. Null buffers are ignored. A buffer whose
    /// length does not match <see cref="FrameByteLength"/> is dropped (never requeued) to protect
    /// the encoder's exact-width contract, decrementing the live count. Never throws.
    /// </summary>
    public void Return(byte[] buffer)
    {
        if (buffer == null)
            return;

        if (buffer.Length != _frameByteLength)
        {
            Interlocked.Decrement(ref _liveCount);
            return;
        }

        _free.Enqueue(buffer);
    }
}
