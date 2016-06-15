using System;
using System.Collections.Generic;

namespace BosunReporter.Infrastructure
{
    internal enum QueueType
    {
        Local,
        ExternalCounters,
    }

    internal class PayloadQueue
    {
        private int _cacheLimit = 1;
        private readonly object _warmLock = new object();
        private readonly Stack<Payload> _warmCache = new Stack<Payload>();
        private readonly object _pendingLock = new object();
        private readonly DoubleEndedQueue<Payload> _pendingPayloads = new DoubleEndedQueue<Payload>();
        
        internal int PayloadSize { get; set; }
        internal int MaxPendingPayloads { get; set; }
        internal int LastBatchPayloadCount { get; private set; }
        internal int DroppedPayloads { get; private set; }
        internal QueueType Type { get; }

        internal int PendingPayloadsCount => _pendingPayloads.Count;
        internal bool IsFull => PendingPayloadsCount >= MaxPendingPayloads;

        internal event Action<BosunQueueFullException> PayloadDropped;

        internal PayloadQueue(QueueType type)
        {
            Type = type;
        }

        internal MetricWriter GetWriter()
        {
            // sure, we could keep a cache of writers, but seriously, it's one object per serialization interval
            return new MetricWriter(this);
        }

        internal void Clear()
        {
            Payload p;
            while ((p = DequeuePendingPayload()) != null)
            {
                ReleasePayload(p);
            }
        }

        internal Payload DequeuePendingPayload()
        {
            lock (_pendingLock)
            {
                Payload p;
                if (_pendingPayloads.TryPopOldest(out p))
                    return p;

                return null;
            }
        }

        internal void AddPendingPayload(Payload payload)
        {
            if (payload.Used == 0)
                return;

            BosunQueueFullException ex = null;
            lock (_pendingLock)
            {
                if (!IsFull)
                {
                    _pendingPayloads.Push(payload);
                }
                else
                {
                    ex = new BosunQueueFullException(Type, payload.MetricsCount, payload.Used);
                    DroppedPayloads++;
                    ReleasePayload(payload);
                }
            }

            if (ex != null)
                PayloadDropped?.Invoke(ex);
        }

        internal void SetBatchPayloadCount(int count)
        {
            _cacheLimit = count; // todo - might eventually set the cache limit more intelligently
            LastBatchPayloadCount = count;
        }

        internal Payload GetPayloadForMetricWriter()
        {
            Payload p = null;

            if (_pendingPayloads.Count > 0)
            {
                lock (_pendingLock)
                {
                    // Check if we want to keep writing to an existing pending payload.
                    // If the data has at least 600 bytes available, let's go ahead and reuse it (no metric should ever come close to 600 bytes).
                    // This is mostly an optimzation for the metrics which serialize independently on itialization.
                    if (_pendingPayloads.TryPopNewest(out p))
                    {
                        if (p.Data.Length - p.Used > 600)
                            return p;

                        _pendingPayloads.Push(p);
                        p = null;
                    }
                }
            }

            // check if we have a "warm" payload (so we don't always have to allocate a new byte array)
            lock (_warmLock)
            {
                if (_warmCache.Count > 0)
                {
                    p = _warmCache.Pop();

                    if (p.Data.Length != PayloadSize)
                    {
                        p = null;
                        while (_warmCache.Count > 0 && _warmCache.Peek().Data.Length != PayloadSize)
                            _warmCache.Pop();
                    }
                }
            }

            return p ?? new Payload(PayloadSize);
        }

        internal void ReleasePayload(Payload payload)
        {
            if (payload.Data.Length != PayloadSize)
                return;

            lock (_warmLock)
            {
                if (_warmCache.Count < _cacheLimit)
                {
                    payload.Used = 0;
                    payload.MetricsCount = 0;
                    _warmCache.Push(payload);
                }
            }
        }
    }
}