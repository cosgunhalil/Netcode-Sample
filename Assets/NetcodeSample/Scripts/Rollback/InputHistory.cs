using System;
using NetcodeSample.Simulation;

namespace NetcodeSample.Rollback
{
    /// <summary>One player's inputs by tick, in a ring buffer that keeps the most recent <see cref="Capacity"/> ticks.</summary>
    internal sealed class InputHistory
    {
        private readonly long[] _ticks;
        private readonly byte[] _inputs;

        public InputHistory(int capacity)
        {
            Capacity = capacity;
            _ticks = new long[capacity];
            _inputs = new byte[capacity];
            Array.Fill(_ticks, -1L);
        }

        public int Capacity { get; }

        /// <summary>Every input up to and including this tick is known.</summary>
        public long ContiguousTick { get; private set; }

        /// <summary>The highest tick with a known input.</summary>
        public long LatestTick { get; private set; }

        public bool Contains(long tick)
        {
            return tick > 0 && _ticks[Index(tick)] == tick;
        }

        public GameInput Get(long tick)
        {
            if (!Contains(tick))
            {
                throw new InvalidOperationException($"No input for tick {tick}.");
            }

            return new GameInput(_inputs[Index(tick)]);
        }

        /// <summary>Stores an input; returns false when it was already known.</summary>
        public bool Set(long tick, GameInput input)
        {
            if (tick <= 0 || Contains(tick))
            {
                return false;
            }

            if (tick <= LatestTick - Capacity)
            {
                // Too old to keep; it's already confirmed or will never be needed.
                return false;
            }

            int index = Index(tick);
            _ticks[index] = tick;
            _inputs[index] = input.Buttons;
            LatestTick = Math.Max(LatestTick, tick);
            while (Contains(ContiguousTick + 1))
            {
                ContiguousTick++;
            }

            return true;
        }

        private int Index(long tick)
        {
            return (int)(tick % Capacity);
        }
    }
}
