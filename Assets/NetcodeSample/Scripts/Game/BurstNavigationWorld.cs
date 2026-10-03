using System;
using DPF.Core.Simulation;
using DPF.Unity;
using NetcodeSample.Simulation.Navigation;

namespace NetcodeSample.Game
{
    /// <summary>
    /// The navigation world the game runs on: DPF's Burst-compiled world on Unity's job system. Produces the same
    /// state as <see cref="ManagedNavigationWorld"/>, only faster, which matters when rollback re-simulates
    /// several ticks per frame.
    /// </summary>
    public sealed unsafe class BurstNavigationWorld : INavigationWorld
    {
        private readonly DPFLoadedNavMesh _navMesh;
        private readonly DPFNavWorld _world;

        private BurstNavigationWorld(DPFLoadedNavMesh navMesh, DPFNavWorld world)
        {
            _navMesh = navMesh;
            _world = world;
        }

        public long StateSize => _world.StateSize;

        public ulong NavMeshChecksum => _navMesh.Checksum;

        public static BurstNavigationWorld Create(byte[] navMesh, NavWorldConfig config)
        {
            if (!DPFLoadedNavMesh.TryLoad(navMesh, out DPFLoadedNavMesh loaded, out string error))
            {
                throw new ArgumentException($"The navmesh data is invalid: {error}", nameof(navMesh));
            }

            if (!DPFNavWorld.TryCreate(loaded, config, out DPFNavWorld world, out error))
            {
                loaded.Dispose();
                throw new ArgumentException($"Could not create the navigation world: {error}", nameof(config));
            }

            return new BurstNavigationWorld(loaded, world);
        }

        public void Tick(NavCommand* commands, int count)
        {
            _world.Tick(commands, count);
        }

        public NavAgentInfo GetAgent(int agent)
        {
            return _world.World->GetAgent(agent);
        }

        public void SaveSnapshot(byte* destination)
        {
            _world.World->SaveSnapshot(destination);
        }

        public bool LoadSnapshot(byte* source, long size)
        {
            return _world.World->LoadSnapshot(source, size);
        }

        public ulong ComputeHash()
        {
            return _world.ComputeHash();
        }

        public void Dispose()
        {
            // The world before the navmesh it points into.
            _world.Dispose();
            _navMesh.Dispose();
        }
    }

    public sealed class BurstNavigationWorldFactory : INavigationWorldFactory
    {
        public INavigationWorld Create(byte[] navMesh, NavWorldConfig config)
        {
            return BurstNavigationWorld.Create(navMesh, config);
        }
    }
}
