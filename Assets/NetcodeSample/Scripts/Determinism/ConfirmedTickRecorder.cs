using System;
using System.Collections.Generic;
using NetcodeSample.Rollback;
using NetcodeSample.Simulation;
using Tickwise;

namespace NetcodeSample.Determinism
{
    /// <summary>
    /// Records a rollback peer's confirmed ticks with Tickwise. Hashes (and dumps) are taken every time a tick is
    /// simulated, overwriting earlier simulations of the same tick, and written when the tick is confirmed: by then
    /// the simulation has moved on, but the last simulation of a confirmed tick is the final one. Predicted ticks
    /// are never recorded, so two peers' recordings compare tick for tick.
    /// </summary>
    public sealed class ConfirmedTickRecorder : ITickObserver, IDisposable
    {
        private readonly MatchRecorder _recorder;
        private readonly GameSimulation _simulation;
        private readonly SimulationProbe _probe;
        private readonly Entry[] _entries;

        public ConfirmedTickRecorder(MatchRecorder recorder, GameSimulation simulation, int maxUnconfirmedTicks)
        {
            _recorder = recorder;
            _simulation = simulation;
            _probe = new SimulationProbe(simulation);
            _entries = new Entry[maxUnconfirmedTicks + 2];
            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i] = new Entry();
            }
        }

        public MatchRecorder Recorder => _recorder;

        public void OnTickSimulated(long tick)
        {
            Entry entry = _entries[Slot(tick)];
            entry.Tick = tick;
            entry.LightHash = _simulation.ComputeLightHash();
            entry.FullHash = _recorder.WantsFullHash(tick) ? _simulation.ComputeFullHash() : 0;
            entry.HasDump = _recorder.WantsDump(tick);
            if (entry.HasDump)
            {
                entry.Dump ??= new TickwiseDump();
                entry.Dump.Clear();
                _probe.WriteState(entry.Dump);
            }

            entry.Markers.Clear();
            foreach (GameEvent gameEvent in _simulation.Events)
            {
                if (gameEvent.Type == GameEventType.RoundEnded)
                {
                    entry.Markers.Add($"round-ended:{(RoundResult)gameEvent.Amount}");
                }
                else if (gameEvent.Type == GameEventType.RoundStarted)
                {
                    entry.Markers.Add($"round-started:{gameEvent.Amount}");
                }
            }
        }

        public void OnTickConfirmed(long tick, GameInput red, GameInput blue)
        {
            Entry entry = _entries[Slot(tick)];
            if (entry.Tick != tick)
            {
                throw new InvalidOperationException($"Tick {tick} was confirmed but its hashes were overwritten; the observer window is too small.");
            }

            _recorder.RecordComputedTick(tick, red, blue, entry.LightHash, entry.FullHash, entry.HasDump ? entry.Dump : null);
            foreach (string marker in entry.Markers)
            {
                _recorder.RecordMarker(tick, marker);
            }
        }

        public void Dispose()
        {
            _recorder.Dispose();
            foreach (Entry entry in _entries)
            {
                entry.Dump?.Dispose();
            }
        }

        private int Slot(long tick)
        {
            return (int)(tick % _entries.Length);
        }

        private sealed class Entry
        {
            public long Tick = -1;
            public ulong LightHash;
            public ulong FullHash;
            public bool HasDump;
            public TickwiseDump Dump;
            public readonly List<string> Markers = new();
        }
    }
}
