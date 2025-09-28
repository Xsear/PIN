using System;
using BepuUtilities.Collections;
using BepuUtilities.Memory;
using Demos.UI;

namespace Demos;

public class TimingsRingBuffer : IDataSeries, IDisposable
{
    private QuickQueue<double> _queue;
    private BufferPool _pool;

    /// <summary>
    /// Gets or sets the maximum number of time measurements that can be held by the ring buffer.
    /// </summary>
    public int Capacity
    {
        get { return _queue.Span.Length; }
        set
        {
            if (value <= 0)
            {
                throw new ArgumentException("Capacity must be positive.");
            }

            if (Capacity != value)
            {
                _queue.Resize(value, _pool);
            }
        }
    }

    public TimingsRingBuffer(int maximumCapacity, BufferPool pool)
    {
        if (maximumCapacity <= 0)
        {
            throw new ArgumentException("Capacity must be positive.");
        }

        _pool = pool;
        _queue = new QuickQueue<double>(maximumCapacity, pool);
    }

    public void Add(double time)
    {
        if (_queue.Count == Capacity)
        {
            _queue.Dequeue();
        }

        _queue.EnqueueUnsafely(time);
    }

    public double this[int index] => _queue[index];

    public int Start => 0;

    public int End => _queue.Count;

    public TimelineStats ComputeStats()
    {
        TimelineStats stats;
        stats.Total = 0.0;
        var sumOfSquares = 0.0;
        stats.Min = double.MaxValue;
        stats.Max = double.MinValue;
        for (int i = 0; i < _queue.Count; ++i)
        {
            var time = _queue[i];
            stats.Total += time;
            sumOfSquares += time * time;
            if (time < stats.Min)
                stats.Min = time;
            if (time > stats.Max)
                stats.Max = time;
        }

        stats.Average = stats.Total / _queue.Count;
        stats.StdDev = Math.Sqrt(Math.Max(0, sumOfSquares / _queue.Count - stats.Average * stats.Average));
        return stats;
    }

    public void Dispose()
    {
        _queue.Dispose(_pool);
    }
}
