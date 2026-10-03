using DPF.Core.Numerics;

namespace NetcodeSample.Simulation
{
    public enum GameEventType : byte
    {
        UnitSpawned = 0,
        UnitAttacked = 1,
        UnitDied = 2,

        /// <summary>A unit reached the enemy base and exploded; <see cref="GameEvent.Amount"/> is the damage dealt.</summary>
        UnitExploded = 3,
        RoundEnded = 4,
        RoundStarted = 5,
    }

    /// <summary>
    /// Something that happened during the last step, for presentation (effects, sounds). Events aren't part of the
    /// state: a re-simulated tick produces its events again.
    /// </summary>
    public struct GameEvent
    {
        public GameEventType Type;
        public Team Team;
        public long UnitId;
        public FP3 Position;
        public int Amount;
    }
}
