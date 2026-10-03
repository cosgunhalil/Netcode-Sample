using DPF.Core.Numerics;
using NetcodeSample.Simulation;
using Tickwise;

namespace NetcodeSample.Determinism
{
    /// <summary>
    /// Tells Tickwise what to hash and how to name the state: the light hash every tick, the full hash on the
    /// recorder's interval, and a field-by-field dump (same coverage as the full hash) for <c>tickwise diff</c>.
    /// </summary>
    public sealed class SimulationProbe : IDeterminismProbe, ITickwiseStateWriter
    {
        private readonly GameSimulation _simulation;

        public SimulationProbe(GameSimulation simulation)
        {
            _simulation = simulation;
        }

        public ulong LightHash() => _simulation.ComputeLightHash();

        public ulong FullHash() => _simulation.ComputeFullHash();

        public void WriteState(TickwiseDump dump)
        {
            ref readonly MatchState match = ref _simulation.Match;
            dump.SetInt64("match.tick", match.Tick);
            dump.SetInt64("match.round", match.Round);
            dump.SetInt64("match.roundPauseTicks", match.RoundPauseTicks);
            dump.SetString("match.lastRoundResult", match.LastRoundResult.ToString());

            dump.SetLength("teams", TeamExtensions.Count);
            for (int team = 0; team < TeamExtensions.Count; team++)
            {
                ref readonly TeamState state = ref _simulation.GetTeam((Team)team);
                string prefix = $"teams[{team}].";
                dump.SetInt64(prefix + "score", state.Score);
                dump.SetInt64(prefix + "baseHealth", state.BaseHealth);
                dump.SetInt64(prefix + "unitCount", state.UnitCount);
                dump.SetInt64(prefix + "spawnCounter", state.SpawnCounter);
                dump.SetInt64(prefix + "smallSpawnTimer", state.SmallSpawnTimer.RawValue);
                dump.SetInt64(prefix + "bigCooldown", state.BigCooldown.RawValue);
            }

            dump.SetLength("units", (ulong)_simulation.UnitCapacity);
            for (int slot = 0; slot < _simulation.UnitCapacity; slot++)
            {
                ref readonly Unit unit = ref _simulation.GetUnit(slot);
                string prefix = $"units[{slot}].";
                dump.SetString(prefix + "state", unit.State.ToString());
                if (unit.State == UnitState.Free)
                {
                    continue;
                }

                dump.SetInt64(prefix + "id", unit.Id);
                dump.SetString(prefix + "team", unit.Team.ToString());
                dump.SetString(prefix + "kind", unit.Kind.ToString());
                dump.SetString(prefix + "activity", unit.Activity.ToString());
                dump.SetInt64(prefix + "health", unit.Health);
                dump.SetInt64(prefix + "targetSlot", unit.TargetSlot);
                dump.SetInt64(prefix + "targetId", unit.TargetId);
                dump.SetInt64(prefix + "chaseTimer", unit.ChaseTimer);
                dump.SetInt64(prefix + "attackCooldown", unit.AttackCooldown.RawValue);
                WritePosition(dump, prefix + "position", unit.Position);
            }

            // The navigation state is megabytes of agents, paths and flow fields; its hash shows whether it diverged.
            dump.SetUInt64("navigation.hash", _simulation.ComputeNavigationHash());
        }

        private static void WritePosition(TickwiseDump dump, string path, FP3 position)
        {
            // Raw fixed-point values are what the hash sees; metres are for reading the diff.
            dump.SetInt64(path + ".x", position.X.RawValue);
            dump.SetInt64(path + ".y", position.Y.RawValue);
            dump.SetInt64(path + ".z", position.Z.RawValue);
            dump.SetDouble(path + ".xMetres", position.X.RawValue / 65536.0);
            dump.SetDouble(path + ".zMetres", position.Z.RawValue / 65536.0);
        }
    }
}
