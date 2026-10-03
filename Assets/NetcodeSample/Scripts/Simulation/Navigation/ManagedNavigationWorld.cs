using System;
using System.Runtime.InteropServices;
using DPF.Core.Navigation;
using DPF.Core.Simulation;

namespace NetcodeSample.Simulation.Navigation
{
    /// <summary>
    /// A DPF navigation world in unmanaged memory, ticked single-threaded in managed code. Needs no Unity, so the
    /// simulation runs in plain .NET: EditMode tests, Tickwise replays and validators.
    /// </summary>
    public sealed unsafe class ManagedNavigationWorld : INavigationWorld
    {
        private const int Alignment = 16;

        private IntPtr _meshAllocation;
        private IntPtr _worldAllocation;
        private NavWorld* _world;

        private ManagedNavigationWorld()
        {
        }

        ~ManagedNavigationWorld()
        {
            Free();
        }

        public long StateSize => _world->StateSize;

        public ulong NavMeshChecksum => _world->Mesh->Checksum;

        public static ManagedNavigationWorld Create(byte[] navMesh, NavWorldConfig config)
        {
            if (navMesh == null || navMesh.Length == 0)
            {
                throw new ArgumentException("The navmesh data is empty.", nameof(navMesh));
            }

            if (!config.Validate(out string error))
            {
                throw new ArgumentException(error, nameof(config));
            }

            ManagedNavigationWorld world = new();
            try
            {
                // The view must stay at a fixed address next to its blob for as long as the world lives.
                long viewSize = Align(sizeof(NavMeshView));
                byte* meshMemory = Allocate(viewSize + navMesh.Length, out world._meshAllocation);
                byte* blob = meshMemory + viewSize;
                Marshal.Copy(navMesh, 0, (IntPtr)blob, navMesh.Length);
                if (!NavMeshView.TryCreate(blob, navMesh.Length, out NavMeshView view, out error))
                {
                    throw new ArgumentException($"The navmesh data is invalid: {error}", nameof(navMesh));
                }

                NavMeshView* mesh = (NavMeshView*)meshMemory;
                *mesh = view;

                const int ChunkCount = 1;
                long worldSize = Align(sizeof(NavWorld));
                long stateSize = Align(NavWorld.StateSizeInBytes(config, mesh));
                long derivedSize = Align(NavWorld.DerivedSizeInBytes(config, mesh, ChunkCount));
                byte* worldMemory = Allocate(worldSize + stateSize + derivedSize, out world._worldAllocation);
                world._world = (NavWorld*)worldMemory;
                if (!NavWorld.Create(config, mesh, worldMemory + worldSize, worldMemory + worldSize + stateSize, ChunkCount, out *world._world, out error))
                {
                    throw new ArgumentException($"Could not create the navigation world: {error}", nameof(config));
                }

                return world;
            }
            catch
            {
                world.Dispose();
                throw;
            }
        }

        public void Tick(NavCommand* commands, int count)
        {
            _world->Tick(commands, count);
        }

        public NavAgentInfo GetAgent(int agent)
        {
            return _world->GetAgent(agent);
        }

        public void SaveSnapshot(byte* destination)
        {
            _world->SaveSnapshot(destination);
        }

        public bool LoadSnapshot(byte* source, long size)
        {
            return _world->LoadSnapshot(source, size);
        }

        public ulong ComputeHash()
        {
            return _world->ComputeHash();
        }

        public void Dispose()
        {
            Free();
            GC.SuppressFinalize(this);
        }

        private static long Align(long size)
        {
            return (size + Alignment - 1) & ~(long)(Alignment - 1);
        }

        // Returns zeroed memory aligned to 16 bytes; allocation receives the pointer to free.
        private static byte* Allocate(long size, out IntPtr allocation)
        {
            allocation = Marshal.AllocHGlobal((IntPtr)(size + Alignment));
            byte* aligned = (byte*)Align((long)allocation);
            for (long offset = 0; offset < size; offset += int.MaxValue)
            {
                new Span<byte>(aligned + offset, (int)Math.Min(int.MaxValue, size - offset)).Clear();
            }

            return aligned;
        }

        private void Free()
        {
            _world = null;
            if (_worldAllocation != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_worldAllocation);
                _worldAllocation = IntPtr.Zero;
            }

            if (_meshAllocation != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_meshAllocation);
                _meshAllocation = IntPtr.Zero;
            }
        }
    }

    public sealed class ManagedNavigationWorldFactory : INavigationWorldFactory
    {
        public INavigationWorld Create(byte[] navMesh, NavWorldConfig config)
        {
            return ManagedNavigationWorld.Create(navMesh, config);
        }
    }
}
