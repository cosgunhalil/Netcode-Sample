using System;
using System.Collections.Generic;

namespace NetcodeSample.Rollback
{
    public struct NetworkConditions
    {
        /// <summary>One-way delay in milliseconds.</summary>
        public double LatencyMs;

        /// <summary>Extra random one-way delay, 0 to this many milliseconds; reorders messages.</summary>
        public double JitterMs;

        /// <summary>The chance (0 to 1) that a message is dropped.</summary>
        public double PacketLoss;
    }

    /// <summary>
    /// Two peers connected in-process through a simulated network with latency, jitter and packet loss.
    /// Time is whatever the caller passes to <see cref="Update"/>, so tests can run it on a virtual clock.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        private readonly Random _random;
        private double _now;

        public LoopbackNetwork(int seed)
        {
            _random = new Random(seed);
            First = new Endpoint(this);
            Second = new Endpoint(this);
            First.Peer = Second;
            Second.Peer = First;
        }

        public NetworkConditions Conditions { get; set; }

        public Endpoint First { get; }

        public Endpoint Second { get; }

        /// <summary>Advances the network clock to <paramref name="nowSeconds"/>; messages due by then become receivable.</summary>
        public void Update(double nowSeconds)
        {
            _now = nowSeconds;
        }

        private bool TrySchedule(out double deliverAt)
        {
            NetworkConditions conditions = Conditions;
            deliverAt = 0;
            if (_random.NextDouble() < conditions.PacketLoss)
            {
                return false;
            }

            double delayMs = conditions.LatencyMs + (_random.NextDouble() * conditions.JitterMs);
            deliverAt = _now + (delayMs / 1000.0);
            return true;
        }

        public sealed class Endpoint : IInputTransport
        {
            private readonly LoopbackNetwork _network;
            private readonly List<(double DeliverAt, InputMessage Message)> _inbox = new();

            internal Endpoint(LoopbackNetwork network)
            {
                _network = network;
            }

            internal Endpoint Peer { get; set; }

            public void Send(in InputMessage message)
            {
                if (_network.TrySchedule(out double deliverAt))
                {
                    Peer._inbox.Add((deliverAt, message));
                }
            }

            // Delivers the earliest due message first; jitter can deliver them out of send order.
            public bool TryReceive(out InputMessage message)
            {
                int best = -1;
                for (int i = 0; i < _inbox.Count; i++)
                {
                    if (_inbox[i].DeliverAt <= _network._now && (best < 0 || _inbox[i].DeliverAt < _inbox[best].DeliverAt))
                    {
                        best = i;
                    }
                }

                if (best < 0)
                {
                    message = default;
                    return false;
                }

                message = _inbox[best].Message;
                _inbox.RemoveAt(best);
                return true;
            }
        }
    }
}
