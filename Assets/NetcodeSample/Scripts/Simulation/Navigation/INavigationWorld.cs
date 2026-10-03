using System;
using DPF.Core.Simulation;

namespace NetcodeSample.Simulation.Navigation
{
    /// <summary>
    /// A DPF navigation world, however it executes. Implementations must produce bit-identical state for the same
    /// commands: the managed one runs single-threaded with no Unity dependency (tests, replays), the Burst one in the
    /// game runs on Unity's job system.
    /// </summary>
    public unsafe interface INavigationWorld : IDisposable
    {
        /// <summary>The size of a navigation snapshot in bytes.</summary>
        long StateSize { get; }

        /// <summary>The navmesh checksum, equal on every peer that loaded the same navmesh.</summary>
        ulong NavMeshChecksum { get; }

        void Tick(NavCommand* commands, int count);

        NavAgentInfo GetAgent(int agent);

        void SaveSnapshot(byte* destination);

        bool LoadSnapshot(byte* source, long size);

        ulong ComputeHash();
    }

    public interface INavigationWorldFactory
    {
        /// <summary>Loads <paramref name="navMesh"/> and creates a world on it. Throws when either is invalid.</summary>
        INavigationWorld Create(byte[] navMesh, NavWorldConfig config);
    }
}
