using System.Runtime.InteropServices;
using DPF.Core.Numerics;

// Everything in this file is part of a snapshot and of the state hash. The structs are blittable and packed with
// no padding (Pack = 1), so their raw bytes are fully defined and hash identically on every machine.
namespace NetcodeSample.Simulation
{
    public enum UnitState : byte
    {
        /// <summary>The slot is empty.</summary>
        Free = 0,
        Alive = 1,

        /// <summary>Dead or exploded; its navigation agent is removed at the start of the next tick.</summary>
        PendingRemoval = 2,
    }

    public enum UnitActivity : byte
    {
        /// <summary>Following the flow field to the enemy base.</summary>
        Marching = 0,

        /// <summary>Walking towards a target unit.</summary>
        Chasing = 1,

        /// <summary>Standing still and hitting a target unit in range.</summary>
        Attacking = 2,

        /// <summary>Stopped because the round is over.</summary>
        Idle = 3,
    }

    public enum RoundResult : byte
    {
        None = 0,
        RedWon = 1,
        BlueWon = 2,
        Draw = 3,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Unit
    {
        /// <summary>
        /// Stable identity, derived from the spawn tick, team and kind (at most one unit of each kind and team
        /// spawns per tick). A re-simulated spawn gets the same id, so views keep tracking it across rollbacks.
        /// </summary>
        public long Id;

        /// <summary>Copied from the navigation agent after every tick.</summary>
        public FP3 Position;

        /// <summary>Ticks until the unit can hit again.</summary>
        public FP AttackCooldown;

        /// <summary>The target's id, to notice when its slot was reused by another unit.</summary>
        public long TargetId;
        public int Health;

        /// <summary>The target's slot, or -1.</summary>
        public int TargetSlot;

        /// <summary>Ticks until a chasing unit re-targets its path to where its target is now.</summary>
        public int ChaseTimer;
        public UnitState State;
        public Team Team;
        public UnitKind Kind;
        public UnitActivity Activity;

        public bool IsAlive => State == UnitState.Alive;

        public static long MakeId(long tick, Team team, UnitKind kind)
        {
            return (tick * 4) + ((int)team * 2) + (int)kind;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TeamState
    {
        /// <summary>Ticks until the next small cube spawns.</summary>
        public FP SmallSpawnTimer;

        /// <summary>Ticks until the big cube can spawn again.</summary>
        public FP BigCooldown;
        public int Score;
        public int BaseHealth;
        public int UnitCount;

        /// <summary>Counts spawns, to spread new units around the base instead of stacking them.</summary>
        public int SpawnCounter;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MatchState
    {
        /// <summary>The number of ticks simulated so far; the tick being simulated during a step.</summary>
        public long Tick;
        public int Round;

        /// <summary>Ticks left in the pause after a round ends; 0 while a round is being played.</summary>
        public int RoundPauseTicks;
        public RoundResult LastRoundResult;
    }
}
