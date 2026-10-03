using System;
using DPF.Core.Numerics;

namespace NetcodeSample.Simulation
{
    /// <summary>The engine-free description of a level: a DPF navmesh blob and the two base positions.</summary>
    public sealed class LevelData
    {
        private readonly FP3 _redBase;
        private readonly FP3 _blueBase;

        public LevelData(byte[] navMesh, FP3 redBase, FP3 blueBase)
        {
            NavMesh = navMesh ?? throw new ArgumentNullException(nameof(navMesh));
            _redBase = redBase;
            _blueBase = blueBase;
        }

        public byte[] NavMesh { get; }

        public FP3 GetBasePosition(Team team) => team == Team.Red ? _redBase : _blueBase;
    }
}
