using DPF.Core.Hashing;
using DPF.Core.Numerics;

namespace NetcodeSample.Simulation
{
    public enum UnitKind : byte
    {
        Small = 0,
        Big = 1,
    }

    /// <summary>Combat and movement stats of one unit kind. Times are in seconds, distances in metres.</summary>
    public struct UnitStats
    {
        public int Health;
        public int Damage;

        /// <summary>Edge-to-edge distance within which the unit can hit its target.</summary>
        public FP AttackRange;
        public FP AttackCooldown;
        public FP Radius;
        public FP Speed;
    }

    /// <summary>
    /// The game's tunable numbers. Copied into the simulation when it's created; every peer must use identical
    /// values, which <see cref="ComputeHash"/> lets them check.
    /// </summary>
    public struct GameRules
    {
        public int TickRate;
        public int BaseHealth;
        public int MaxUnitsPerTeam;
        public FP SmallSpawnInterval;
        public FP BigCooldown;

        /// <summary>Edge-to-edge distance within which a unit turns to fight an enemy unit.</summary>
        public FP AcquireRadius;

        /// <summary>A unit whose edge comes within this distance of the enemy base centre explodes on it.</summary>
        public FP BaseRadius;
        public FP RoundRestartPause;

        /// <summary>
        /// A round that lasts this long ends: the healthier base wins, then the side with more cubes; otherwise a draw.
        /// Zero means no limit.
        /// </summary>
        public FP RoundTimeLimit;

        public UnitStats Small;
        public UnitStats Big;

        public static GameRules Default => new()
        {
            TickRate = 30,
            BaseHealth = 1000,
            MaxUnitsPerTeam = 256,
            SmallSpawnInterval = FP.One,
            BigCooldown = FP.FromInt(5),
            AcquireRadius = FP.Two,
            BaseRadius = FP.FromRatio(3, 2),
            RoundRestartPause = FP.FromInt(3),
            RoundTimeLimit = FP.FromInt(120),
            Small = new UnitStats
            {
                Health = 50,
                Damage = 8,
                AttackRange = FP.One,
                AttackCooldown = FP.Half,
                Radius = FP.FromRatio(35, 100),
                Speed = FP.FromInt(3),
            },
            Big = new UnitStats
            {
                Health = 100,
                Damage = 25,
                AttackRange = FP.FromRatio(3, 2),
                AttackCooldown = FP.FromRatio(1, 4),
                Radius = FP.FromRatio(6, 10),
                Speed = FP.FromRatio(22, 10),
            },
        };

        public FP TickDuration => FP.FromRatio(1, TickRate);

        public int RoundRestartPauseTicks => ToTicks(RoundRestartPause).RoundToInt();

        public int RoundTimeLimitTicks => ToTicks(RoundTimeLimit).RoundToInt();

        /// <summary>A duration in ticks. Exact: multiplying by the integer tick rate never rounds (0.25 s = 7.5 ticks at 30 Hz).</summary>
        public FP ToTicks(FP seconds) => seconds * FP.FromInt(TickRate);

        public UnitStats GetStats(UnitKind kind) => kind == UnitKind.Big ? Big : Small;

        public bool Validate(out string error)
        {
            error = null;
            if (TickRate < 1 || TickRate > 120)
            {
                error = "TickRate must be 1 to 120.";
            }
            else if (BaseHealth < 1)
            {
                error = "BaseHealth must be positive.";
            }
            else if (MaxUnitsPerTeam < 1 || MaxUnitsPerTeam > 2048)
            {
                error = "MaxUnitsPerTeam must be 1 to 2048.";
            }
            else if (SmallSpawnInterval <= FP.Zero || BigCooldown < FP.Zero || RoundRestartPause < FP.Zero || RoundTimeLimit < FP.Zero)
            {
                error = "SmallSpawnInterval must be positive; BigCooldown, RoundRestartPause and RoundTimeLimit can't be negative.";
            }
            else if (!IsValid(Small) || !IsValid(Big))
            {
                error = "Unit stats need positive health, radius, speed and attack cooldown, and non-negative damage and range.";
            }

            return error == null;
        }

        /// <summary>A hash of every rule, so peers can refuse to play with different rules.</summary>
        public unsafe ulong ComputeHash()
        {
            const int FieldCount = 21;
            long* values = stackalloc long[FieldCount];
            int index = 0;
            values[index++] = TickRate;
            values[index++] = BaseHealth;
            values[index++] = MaxUnitsPerTeam;
            values[index++] = SmallSpawnInterval.RawValue;
            values[index++] = BigCooldown.RawValue;
            values[index++] = AcquireRadius.RawValue;
            values[index++] = BaseRadius.RawValue;
            values[index++] = RoundRestartPause.RawValue;
            values[index++] = RoundTimeLimit.RawValue;
            Write(Small, values, ref index);
            Write(Big, values, ref index);
            return XxHash64.Hash((byte*)values, FieldCount * sizeof(long), 0);
        }

        private static bool IsValid(UnitStats stats)
        {
            return stats.Health > 0 && stats.Damage >= 0 && stats.AttackRange >= FP.Zero && stats.AttackCooldown > FP.Zero
                && stats.Radius > FP.Zero && stats.Speed > FP.Zero;
        }

        private static unsafe void Write(UnitStats stats, long* values, ref int index)
        {
            values[index++] = stats.Health;
            values[index++] = stats.Damage;
            values[index++] = stats.AttackRange.RawValue;
            values[index++] = stats.AttackCooldown.RawValue;
            values[index++] = stats.Radius.RawValue;
            values[index++] = stats.Speed.RawValue;
        }
    }
}
