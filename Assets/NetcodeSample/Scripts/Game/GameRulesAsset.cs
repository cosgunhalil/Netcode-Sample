using System;
using DPF.Unity;
using NetcodeSample.Simulation;
using UnityEngine;

namespace NetcodeSample.Game
{
    /// <summary>
    /// The game's tunable numbers, edited in the Inspector. Converted once to the fixed-point
    /// <see cref="GameRules"/> when a match starts; every peer must use the same asset values.
    /// </summary>
    [CreateAssetMenu(menuName = "Netcode Sample/Game Rules", fileName = "GameRules")]
    public sealed class GameRulesAsset : ScriptableObject
    {
        [SerializeField]
        [Range(10, 60)]
        private int _tickRate = 30;

        [SerializeField]
        [Min(1)]
        private int _baseHealth = 1000;

        [SerializeField]
        [Min(1)]
        private int _maxUnitsPerTeam = 256;

        [SerializeField]
        [Min(0.05f)]
        [Tooltip("Seconds between automatic small-cube spawns.")]
        private float _smallSpawnInterval = 1f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds before the big cube can be spawned again.")]
        private float _bigCooldown = 5f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Edge-to-edge distance (m) within which a cube turns to fight an enemy cube.")]
        private float _acquireRadius = 2f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("A cube whose edge comes within this distance (m) of the enemy base centre explodes on it.")]
        private float _baseRadius = 1.5f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds between a round ending and the next one starting.")]
        private float _roundRestartPause = 3f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Seconds a round may last (0 = no limit). At the limit the healthier base wins, then the side with more cubes; otherwise it's a draw.")]
        private float _roundTimeLimit = 120f;

        [SerializeField]
        private UnitStatsSettings _small = new()
        {
            Health = 50,
            Damage = 8,
            AttackRange = 1f,
            AttackCooldown = 0.5f,
            Radius = 0.35f,
            Speed = 3f,
        };

        [SerializeField]
        private UnitStatsSettings _big = new()
        {
            Health = 100,
            Damage = 25,
            AttackRange = 1.5f,
            AttackCooldown = 0.25f,
            Radius = 0.6f,
            Speed = 2.2f,
        };

        /// <summary>
        /// Converts to fixed point. Each float is converted exactly once, from the same serialized bits on every
        /// peer, so the result is identical everywhere.
        /// </summary>
        public GameRules ToRules()
        {
            return new GameRules
            {
                TickRate = _tickRate,
                BaseHealth = _baseHealth,
                MaxUnitsPerTeam = _maxUnitsPerTeam,
                SmallSpawnInterval = _smallSpawnInterval.ToFP(),
                BigCooldown = _bigCooldown.ToFP(),
                AcquireRadius = _acquireRadius.ToFP(),
                BaseRadius = _baseRadius.ToFP(),
                RoundRestartPause = _roundRestartPause.ToFP(),
                RoundTimeLimit = _roundTimeLimit.ToFP(),
                Small = _small.ToStats(),
                Big = _big.ToStats(),
            };
        }

        [Serializable]
        private struct UnitStatsSettings
        {
            [Min(1)]
            public int Health;

            [Min(0)]
            public int Damage;

            [Min(0f)]
            [Tooltip("Edge-to-edge distance (m) within which the cube can hit its target.")]
            public float AttackRange;

            [Min(0.01f)]
            [Tooltip("Seconds between hits.")]
            public float AttackCooldown;

            [Min(0.05f)]
            public float Radius;

            [Min(0.1f)]
            [Tooltip("Metres per second.")]
            public float Speed;

            public UnitStats ToStats()
            {
                return new UnitStats
                {
                    Health = Health,
                    Damage = Damage,
                    AttackRange = AttackRange.ToFP(),
                    AttackCooldown = AttackCooldown.ToFP(),
                    Radius = Radius.ToFP(),
                    Speed = Speed.ToFP(),
                };
            }
        }
    }
}
