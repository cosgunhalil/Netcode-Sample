using System;
using DPF.Core.Numerics;
using DPF.Unity;
using UnityEngine;

namespace NetcodeSample.Game
{
    /// <summary>
    /// Everything the simulation needs to know about the level, produced once in the editor by
    /// <c>Netcode Sample &gt; Build Level</c>. Positions are stored as fixed-point raw values, so every
    /// peer starts from bit-identical data and no float is converted at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Netcode Sample/Level Definition", fileName = "Level")]
    public sealed class LevelDefinition : ScriptableObject
    {
        [SerializeField]
        private DPFNavMeshAsset _navMesh;

        [SerializeField]
        private FixedPoint3 _redBase;

        [SerializeField]
        private FixedPoint3 _blueBase;

        [SerializeField]
        private int _generationSeed;

        [SerializeField]
        private int _roomCount;

        /// <summary>The deterministic navmesh converted from the level's baked Unity NavMesh.</summary>
        public DPFNavMeshAsset NavMesh => _navMesh;

        /// <summary>The red base position, on the navmesh, in the level's start room.</summary>
        public FP3 RedBase => _redBase.ToFP3();

        /// <summary>The blue base position, on the navmesh, in the room farthest from the start room.</summary>
        public FP3 BlueBase => _blueBase.ToFP3();

        /// <summary>The Connected Rooms Generator seed the level was built from.</summary>
        public int GenerationSeed => _generationSeed;

        public int RoomCount => _roomCount;

        /// <summary>Called by the level builder after a successful build.</summary>
        public void Set(DPFNavMeshAsset navMesh, FP3 redBase, FP3 blueBase, int generationSeed, int roomCount)
        {
            _navMesh = navMesh;
            _redBase = FixedPoint3.From(redBase);
            _blueBase = FixedPoint3.From(blueBase);
            _generationSeed = generationSeed;
            _roomCount = roomCount;
        }

        /// <summary>Serializable raw form of an <see cref="FP3"/>.</summary>
        [Serializable]
        private struct FixedPoint3
        {
            public long X;
            public long Y;
            public long Z;

            public static FixedPoint3 From(FP3 value)
            {
                return new FixedPoint3
                {
                    X = value.X.RawValue,
                    Y = value.Y.RawValue,
                    Z = value.Z.RawValue,
                };
            }

            public FP3 ToFP3()
            {
                return new FP3(FP.FromRaw(X), FP.FromRaw(Y), FP.FromRaw(Z));
            }
        }
    }
}
