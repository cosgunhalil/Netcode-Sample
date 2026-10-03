using System;
using NetcodeSample.Simulation;

namespace NetcodeSample.Rollback
{
    /// <summary>
    /// Rollback's own determinism check, with no network: every tick, after simulating it, rolls back
    /// <see cref="CheckDistance"/> ticks and re-simulates them with the same inputs. Any tick whose hash differs from
    /// its first simulation means saving or loading a snapshot loses or corrupts state.
    /// </summary>
    public sealed class SyncTestSession
    {
        private readonly GameSimulation _simulation;
        private readonly byte[][] _snapshots;
        private readonly GameInput[] _red;
        private readonly GameInput[] _blue;
        private readonly ulong[] _hashes;

        public SyncTestSession(GameSimulation simulation, int checkDistance)
        {
            if (checkDistance < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(checkDistance), "Must be at least 1.");
            }

            _simulation = simulation;
            CheckDistance = checkDistance;
            int capacity = checkDistance + 1;
            _snapshots = new byte[capacity][];
            for (int i = 0; i < capacity; i++)
            {
                _snapshots[i] = new byte[simulation.SnapshotSize];
            }

            _red = new GameInput[capacity];
            _blue = new GameInput[capacity];
            _hashes = new ulong[capacity];
            FirstMismatchTick = -1;
        }

        public int CheckDistance { get; }

        public GameSimulation Simulation => _simulation;

        /// <summary>The first tick that re-simulated differently, or -1.</summary>
        public long FirstMismatchTick { get; private set; }

        public long ResimulatedTicks { get; private set; }

        /// <summary>Simulates the next tick, then re-simulates the last <see cref="CheckDistance"/> ticks and compares.</summary>
        public void Advance(GameInput red, GameInput blue)
        {
            long tick = _simulation.Tick + 1;
            Simulate(tick, red, blue, firstTime: true);

            long from = Math.Max(1, tick - CheckDistance + 1);
            _simulation.LoadSnapshot(_snapshots[Slot(from)]);
            for (long resimulated = from; resimulated <= tick; resimulated++)
            {
                Simulate(resimulated, _red[Slot(resimulated)], _blue[Slot(resimulated)], firstTime: false);
                ResimulatedTicks++;
            }
        }

        private void Simulate(long tick, GameInput red, GameInput blue, bool firstTime)
        {
            int slot = Slot(tick);
            _simulation.SaveSnapshot(_snapshots[slot]);
            _simulation.Step(red, blue);
            ulong hash = _simulation.ComputeLightHash();
            if (firstTime)
            {
                _red[slot] = red;
                _blue[slot] = blue;
                _hashes[slot] = hash;
            }
            else if (hash != _hashes[slot] && FirstMismatchTick < 0)
            {
                FirstMismatchTick = tick;
            }
        }

        private int Slot(long tick)
        {
            return (int)(tick % _snapshots.Length);
        }
    }
}
