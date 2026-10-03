using System;
using System.Collections.Generic;
using System.Diagnostics;
using DPF.Core.Hashing;
using DPF.Core.Numerics;
using DPF.Core.Simulation;
using NetcodeSample.Simulation.Navigation;

namespace NetcodeSample.Simulation
{
    /// <summary>
    /// The deterministic game: two bases, auto-spawning small cubes, big cubes on input, combat and rounds.
    /// Given the same rules, level and inputs, every instance reaches bit-identical state on every machine.
    /// </summary>
    /// <remarks>
    /// Determinism rules for this class: fixed-point math only, iteration in slot order only, no allocation-order
    /// or hash-order dependent containers, no time or randomness. Each tick runs in a fixed order:
    /// free removed units, round pause/reset, spawns, targeting, navigation, attacks (damage applied together, so
    /// processing order can't favour a team), base explosions, deaths, round check.
    /// </remarks>
    public sealed unsafe class GameSimulation : IDisposable
    {
        private const int ChaseRetargetTicks = 5;
        private const int SpawnOffsetCount = 8;

        // Flow field slot i leads to team i's base.
        private const int FlowFieldCount = TeamExtensions.Count;

        private static readonly FP2[] s_spawnOffsets = CreateSpawnOffsets();

        private readonly GameRules _rules;
        private readonly LevelData _level;
        private readonly INavigationWorld _navigation;
        private readonly TeamState[] _teams = new TeamState[TeamExtensions.Count];
        private readonly Unit[] _units;
        private readonly int[] _pendingDamage;
        private readonly int[] _pendingBaseDamage = new int[TeamExtensions.Count];
        private readonly NavCommand[] _commands;
        private readonly List<GameEvent> _events = new();
        // Durations in ticks (seconds x tick rate), converted once. Timers count down by exactly one per tick:
        // seconds-based countdowns would accumulate the rounding of 1/30 s, which fixed point can't represent.
        private readonly FP _smallSpawnTicks;
        private readonly FP _bigCooldownTicks;
        private readonly FP[] _attackCooldownTicks = new FP[2];
        private readonly long _gameStateSize;
        private MatchState _match;
        private int _commandCount;

        public GameSimulation(GameRules rules, LevelData level, INavigationWorldFactory navigationFactory)
        {
            if (!rules.Validate(out string error))
            {
                throw new ArgumentException(error, nameof(rules));
            }

            _rules = rules;
            _level = level ?? throw new ArgumentNullException(nameof(level));
            _smallSpawnTicks = rules.ToTicks(rules.SmallSpawnInterval);
            _bigCooldownTicks = rules.ToTicks(rules.BigCooldown);
            _attackCooldownTicks[(int)UnitKind.Small] = rules.ToTicks(rules.Small.AttackCooldown);
            _attackCooldownTicks[(int)UnitKind.Big] = rules.ToTicks(rules.Big.AttackCooldown);

            int capacity = rules.MaxUnitsPerTeam * TeamExtensions.Count;
            _units = new Unit[capacity];
            _pendingDamage = new int[capacity];
            _commands = new NavCommand[MaxCommandsPerTick(capacity)];
            _navigation = navigationFactory.Create(level.NavMesh, CreateNavigationConfig(rules));

            _gameStateSize = sizeof(MatchState) + ((long)sizeof(TeamState) * _teams.Length) + ((long)sizeof(Unit) * _units.Length);
            SnapshotSize = checked((int)(_gameStateSize + _navigation.StateSize));

            ResetUnits();
            ResetTeams(keepScores: false);
        }

        public GameRules Rules => _rules;

        public LevelData Level => _level;

        /// <summary>The number of ticks simulated so far.</summary>
        public long Tick => _match.Tick;

        public ref readonly MatchState Match => ref _match;

        public int UnitCapacity => _units.Length;

        /// <summary>The size of a full snapshot (game state plus navigation state) in bytes.</summary>
        public int SnapshotSize { get; }

        public ulong NavMeshChecksum => _navigation.NavMeshChecksum;

        /// <summary>
        /// A deliberately planted determinism bug for demonstrating Tickwise. Not part of the state: a peer (or a
        /// replay) without it diverges from one with it. Never enable outside the demo.
        /// </summary>
        public ChaosSettings Chaos { get; set; }

        /// <summary>What happened during the last <see cref="Step"/>.</summary>
        public IReadOnlyList<GameEvent> Events => _events;

        public ref readonly Unit GetUnit(int slot) => ref _units[slot];

        public ref readonly TeamState GetTeam(Team team) => ref _teams[(int)team];

        public static NavWorldConfig CreateNavigationConfig(GameRules rules)
        {
            int maxAgents = rules.MaxUnitsPerTeam * TeamExtensions.Count;
            NavWorldConfig config = NavWorldConfig.Default;
            config.MaxAgents = maxAgents;

            // Agent i owns request slot i.
            config.MaxRequests = maxAgents;
            config.MaxPathLength = 128;
            config.MaxCommandsPerTick = MaxCommandsPerTick(maxAgents);
            config.MaxFlowFields = FlowFieldCount;
            config.TickDuration = rules.TickDuration;
            config.Seed = 1;

            NavAvoidanceConfig avoidance = NavAvoidanceConfig.Default;
            avoidance.Mode = NavAvoidanceMode.Orca;
            config.Avoidance = avoidance;
            return config;
        }

        /// <summary>Simulates one tick with both players' inputs for it.</summary>
        public void Step(GameInput red, GameInput blue)
        {
            _events.Clear();
            _commandCount = 0;
            _match.Tick++;

            if (_match.Tick == 1)
            {
                for (int field = 0; field < FlowFieldCount; field++)
                {
                    AddCommand(NavCommand.CreateFlowField(field, _level.GetBasePosition((Team)field)));
                }
            }

            FreeRemovedUnits();

            if (_match.RoundPauseTicks > 0)
            {
                _match.RoundPauseTicks--;
                if (_match.RoundPauseTicks == 0)
                {
                    StartNextRound();
                }
                else
                {
                    StopIdleUnits();
                }

                TickNavigation();
                return;
            }

            UpdateSpawning(Team.Red, red);
            UpdateSpawning(Team.Blue, blue);
            UpdateTargets();
            TickNavigation();
            ResolveAttacks();
            ResolveBaseExplosions();
            ResolveDeaths();
            CheckRoundEnd();
        }

        /// <summary>Copies the full state into <paramref name="buffer"/>, which needs <see cref="SnapshotSize"/> bytes.</summary>
        public void SaveSnapshot(byte[] buffer)
        {
            CheckSnapshotBuffer(buffer);
            fixed (byte* destination = buffer)
            {
                byte* cursor = destination;
                cursor = Copy(ref _match, cursor);
                cursor = CopyArray(_teams, cursor, toBuffer: true);
                cursor = CopyArray(_units, cursor, toBuffer: true);
                _navigation.SaveSnapshot(cursor);
            }
        }

        /// <summary>Restores a snapshot taken from a simulation with the same rules and level.</summary>
        public void LoadSnapshot(byte[] buffer)
        {
            CheckSnapshotBuffer(buffer);
            fixed (byte* source = buffer)
            {
                byte* cursor = source;
                _match = *(MatchState*)cursor;
                cursor += sizeof(MatchState);
                cursor = CopyArray(_teams, cursor, toBuffer: false);
                cursor = CopyArray(_units, cursor, toBuffer: false);
                if (!_navigation.LoadSnapshot(cursor, _navigation.StateSize))
                {
                    throw new InvalidOperationException("The navigation snapshot doesn't match this simulation's navmesh or configuration.");
                }
            }

            _events.Clear();
        }

        /// <summary>
        /// A cheap hash of the game state (match, teams, units including their positions), for every tick.
        /// Unit positions come from navigation, so navigation divergence shows up here within a tick.
        /// </summary>
        public ulong ComputeLightHash()
        {
            ulong hash = (ulong)_match.Tick;
            fixed (MatchState* match = &_match)
            {
                hash = XxHash64.Hash((byte*)match, sizeof(MatchState), hash);
            }

            fixed (TeamState* teams = _teams)
            {
                hash = XxHash64.Hash((byte*)teams, (long)sizeof(TeamState) * _teams.Length, hash);
            }

            fixed (Unit* units = _units)
            {
                hash = XxHash64.Hash((byte*)units, (long)sizeof(Unit) * _units.Length, hash);
            }

            return hash;
        }

        /// <summary>The light hash combined with the hash of the whole navigation state.</summary>
        public ulong ComputeFullHash()
        {
            ulong* values = stackalloc ulong[2];
            values[0] = ComputeLightHash();
            values[1] = ComputeNavigationHash();
            return XxHash64.Hash((byte*)values, 2 * sizeof(ulong), 0);
        }

        /// <summary>The hash of the whole navigation state (agents, paths, flow fields).</summary>
        public ulong ComputeNavigationHash()
        {
            return _navigation.ComputeHash();
        }

        public void Dispose()
        {
            _navigation.Dispose();
        }

        private static int MaxCommandsPerTick(int maxAgents)
        {
            // Worst case: every agent removed, re-targeted and spawned in one tick, plus flow fields.
            return (maxAgents * 3) + FlowFieldCount + 16;
        }

        private static FP2[] CreateSpawnOffsets()
        {
            FP2[] offsets = new FP2[SpawnOffsetCount];
            FP distance = FP.One;
            for (int i = 0; i < SpawnOffsetCount; i++)
            {
                FPMath.SinCos(FP.TwoPi * FP.FromRatio(i, SpawnOffsetCount), out FP sin, out FP cos);
                offsets[i] = new FP2(cos * distance, sin * distance);
            }

            return offsets;
        }

        private static FP HorizontalDistanceSquared(FP3 a, FP3 b)
        {
            FP dx = a.X - b.X;
            FP dz = a.Z - b.Z;
            return (dx * dx) + (dz * dz);
        }

        // True when the gap between the two circles' edges is at most range.
        private static bool IsWithinEdgeDistance(FP3 a, FP radiusA, FP3 b, FP radiusB, FP range)
        {
            FP reach = range + radiusA + radiusB;
            return HorizontalDistanceSquared(a, b) <= reach * reach;
        }

        private void UpdateSpawning(Team team, GameInput input)
        {
            ref TeamState state = ref _teams[(int)team];

            if (state.BigCooldown > FP.Zero)
            {
                state.BigCooldown = FPMath.Max(FP.Zero, state.BigCooldown - FP.One);
            }

            if (input.WantsSpawnBig && state.BigCooldown <= FP.Zero && TrySpawn(team, UnitKind.Big))
            {
                state.BigCooldown = _bigCooldownTicks;
            }

            // Check before counting down: a spawn on tick t is followed by the next one exactly one interval later.
            if (state.SmallSpawnTimer <= FP.Zero)
            {
                // A full team skips this spawn; the timer keeps its rhythm either way.
                TrySpawn(team, UnitKind.Small);
                state.SmallSpawnTimer += _smallSpawnTicks;
            }

            state.SmallSpawnTimer -= FP.One;
        }

        private bool TrySpawn(Team team, UnitKind kind)
        {
            ref TeamState teamState = ref _teams[(int)team];
            if (teamState.UnitCount >= _rules.MaxUnitsPerTeam)
            {
                return false;
            }

            int slot = FindFreeSlot();
            if (slot < 0)
            {
                return false;
            }

            UnitStats stats = _rules.GetStats(kind);
            FP3 basePosition = _level.GetBasePosition(team);
            int offsetIndex = teamState.SpawnCounter % SpawnOffsetCount;
            if (Chaos.Enabled && _match.Tick >= Chaos.FromTick)
            {
                // THE PLANTED BUG: "variety" taken from the wall clock. Two machines (or a recording and its
                // replay) read different clock values, so the same spawn lands in a different place.
                offsetIndex = (int)((ulong)Stopwatch.GetTimestamp() % SpawnOffsetCount);
            }

            FP2 offset = s_spawnOffsets[offsetIndex];
            FP3 position = new(basePosition.X + offset.X, basePosition.Y, basePosition.Z + offset.Y);
            teamState.SpawnCounter++;
            teamState.UnitCount++;

            _units[slot] = new Unit
            {
                Id = Unit.MakeId(_match.Tick, team, kind),
                Position = position,
                AttackCooldown = FP.Zero,
                TargetId = 0,
                Health = stats.Health,
                TargetSlot = -1,
                ChaseTimer = 0,
                State = UnitState.Alive,
                Team = team,
                Kind = kind,
                Activity = UnitActivity.Marching,
            };

            AddCommand(NavCommand.AddAgent(slot, position, CreateAgentParams(stats)));
            AddCommand(NavCommand.SetAgentFlowField(slot, (int)team.Opponent()));
            AddEvent(GameEventType.UnitSpawned, team, _units[slot].Id, position, 0);
            return true;
        }

        private NavAgentParams CreateAgentParams(UnitStats stats)
        {
            NavAgentParams parameters = NavAgentParams.Default;
            parameters.Radius = stats.Radius;
            parameters.Height = FP.One;
            parameters.MaxSpeed = stats.Speed;
            parameters.MaxAcceleration = FP.FromInt(8);
            parameters.StoppingDistance = FP.FromRatio(1, 10);
            parameters.SlowingDistance = FP.Half;
            return parameters;
        }

        private int FindFreeSlot()
        {
            for (int slot = 0; slot < _units.Length; slot++)
            {
                if (_units[slot].State == UnitState.Free)
                {
                    return slot;
                }
            }

            return -1;
        }

        private void UpdateTargets()
        {
            for (int slot = 0; slot < _units.Length; slot++)
            {
                ref Unit unit = ref _units[slot];
                if (!unit.IsAlive)
                {
                    continue;
                }

                UnitStats stats = _rules.GetStats(unit.Kind);
                if (unit.TargetSlot >= 0 && !IsValidTarget(ref unit, stats))
                {
                    Resume(slot, ref unit);
                }

                if (unit.TargetSlot < 0)
                {
                    int target = FindNearestEnemy(ref unit, stats);
                    if (target < 0)
                    {
                        continue;
                    }

                    unit.TargetSlot = target;
                    unit.TargetId = _units[target].Id;
                    unit.Activity = UnitActivity.Chasing;
                    unit.ChaseTimer = 0;
                }

                ref Unit enemy = ref _units[unit.TargetSlot];
                UnitStats enemyStats = _rules.GetStats(enemy.Kind);
                bool inRange = IsWithinEdgeDistance(unit.Position, stats.Radius, enemy.Position, enemyStats.Radius, stats.AttackRange);
                if (inRange)
                {
                    if (unit.Activity != UnitActivity.Attacking)
                    {
                        unit.Activity = UnitActivity.Attacking;
                        AddCommand(NavCommand.StopAgent(slot));
                    }

                    continue;
                }

                if (unit.Activity == UnitActivity.Attacking)
                {
                    unit.Activity = UnitActivity.Chasing;
                    unit.ChaseTimer = 0;
                }

                if (unit.ChaseTimer <= 0)
                {
                    AddCommand(NavCommand.SetAgentTarget(slot, enemy.Position));
                    unit.ChaseTimer = ChaseRetargetTicks;
                }
                else
                {
                    unit.ChaseTimer--;
                }
            }
        }

        private bool IsValidTarget(ref Unit unit, UnitStats stats)
        {
            ref Unit target = ref _units[unit.TargetSlot];
            if (!target.IsAlive || target.Id != unit.TargetId)
            {
                return false;
            }

            // A target that got away beyond the acquire radius is let go.
            return IsWithinEdgeDistance(unit.Position, stats.Radius, target.Position, _rules.GetStats(target.Kind).Radius, _rules.AcquireRadius);
        }

        private void Resume(int slot, ref Unit unit)
        {
            unit.TargetSlot = -1;
            unit.TargetId = 0;
            unit.Activity = UnitActivity.Marching;
            AddCommand(NavCommand.SetAgentFlowField(slot, (int)unit.Team.Opponent()));
        }

        // The closest enemy within the acquire radius; ties go to the lower slot, so the choice is deterministic.
        private int FindNearestEnemy(ref Unit unit, UnitStats stats)
        {
            int best = -1;
            FP bestDistance = FP.Zero;
            for (int slot = 0; slot < _units.Length; slot++)
            {
                ref Unit other = ref _units[slot];
                if (!other.IsAlive || other.Team == unit.Team)
                {
                    continue;
                }

                FP otherRadius = _rules.GetStats(other.Kind).Radius;
                if (!IsWithinEdgeDistance(unit.Position, stats.Radius, other.Position, otherRadius, _rules.AcquireRadius))
                {
                    continue;
                }

                FP distance = HorizontalDistanceSquared(unit.Position, other.Position);
                if (best < 0 || distance < bestDistance)
                {
                    best = slot;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private void TickNavigation()
        {
            fixed (NavCommand* commands = _commands)
            {
                _navigation.Tick(commands, _commandCount);
            }

            _commandCount = 0;
            for (int slot = 0; slot < _units.Length; slot++)
            {
                if (_units[slot].State != UnitState.Free)
                {
                    _units[slot].Position = _navigation.GetAgent(slot).Position;
                }
            }
        }

        private void ResolveAttacks()
        {
            Array.Clear(_pendingDamage, 0, _pendingDamage.Length);
            for (int slot = 0; slot < _units.Length; slot++)
            {
                ref Unit unit = ref _units[slot];
                if (!unit.IsAlive)
                {
                    continue;
                }

                UnitStats stats = _rules.GetStats(unit.Kind);
                if (unit.AttackCooldown > FP.Zero)
                {
                    unit.AttackCooldown -= FP.One;
                }

                bool canHit = unit.Activity == UnitActivity.Attacking && unit.TargetSlot >= 0
                    && _units[unit.TargetSlot].IsAlive && _units[unit.TargetSlot].Id == unit.TargetId
                    && IsWithinEdgeDistance(unit.Position, stats.Radius, _units[unit.TargetSlot].Position,
                        _rules.GetStats(_units[unit.TargetSlot].Kind).Radius, stats.AttackRange);

                if (!canHit)
                {
                    // Cooldown doesn't bank up while there is nothing to hit.
                    unit.AttackCooldown = FPMath.Max(FP.Zero, unit.AttackCooldown);
                    continue;
                }

                if (unit.AttackCooldown <= FP.Zero)
                {
                    _pendingDamage[unit.TargetSlot] += stats.Damage;
                    unit.AttackCooldown += _attackCooldownTicks[(int)unit.Kind];
                    AddEvent(GameEventType.UnitAttacked, unit.Team, unit.Id, _units[unit.TargetSlot].Position, stats.Damage);
                }
            }

            // Applied together, after every unit has attacked, so slot order can't favour a team.
            for (int slot = 0; slot < _units.Length; slot++)
            {
                if (_pendingDamage[slot] > 0)
                {
                    _units[slot].Health -= _pendingDamage[slot];
                }
            }
        }

        private void ResolveBaseExplosions()
        {
            Array.Clear(_pendingBaseDamage, 0, _pendingBaseDamage.Length);
            for (int slot = 0; slot < _units.Length; slot++)
            {
                ref Unit unit = ref _units[slot];
                if (!unit.IsAlive || unit.Health <= 0)
                {
                    continue;
                }

                Team enemy = unit.Team.Opponent();
                FP radius = _rules.GetStats(unit.Kind).Radius;
                if (!IsWithinEdgeDistance(unit.Position, radius, _level.GetBasePosition(enemy), FP.Zero, _rules.BaseRadius))
                {
                    continue;
                }

                _pendingBaseDamage[(int)enemy] += unit.Health;
                AddEvent(GameEventType.UnitExploded, unit.Team, unit.Id, unit.Position, unit.Health);
                Remove(ref unit);
            }

            for (int team = 0; team < TeamExtensions.Count; team++)
            {
                _teams[team].BaseHealth = Math.Max(0, _teams[team].BaseHealth - _pendingBaseDamage[team]);
            }
        }

        private void ResolveDeaths()
        {
            for (int slot = 0; slot < _units.Length; slot++)
            {
                ref Unit unit = ref _units[slot];
                if (unit.IsAlive && unit.Health <= 0)
                {
                    AddEvent(GameEventType.UnitDied, unit.Team, unit.Id, unit.Position, 0);
                    Remove(ref unit);
                }
            }
        }

        private void Remove(ref Unit unit)
        {
            unit.State = UnitState.PendingRemoval;
            unit.TargetSlot = -1;
            unit.TargetId = 0;
            _teams[(int)unit.Team].UnitCount--;
        }

        private void FreeRemovedUnits()
        {
            for (int slot = 0; slot < _units.Length; slot++)
            {
                if (_units[slot].State == UnitState.PendingRemoval)
                {
                    AddCommand(NavCommand.RemoveAgent(slot));
                    _units[slot] = default;
                    _units[slot].TargetSlot = -1;
                }
            }
        }

        private void CheckRoundEnd()
        {
            bool redDestroyed = _teams[(int)Team.Red].BaseHealth <= 0;
            bool blueDestroyed = _teams[(int)Team.Blue].BaseHealth <= 0;
            if (!redDestroyed && !blueDestroyed)
            {
                return;
            }

            if (redDestroyed && blueDestroyed)
            {
                _match.LastRoundResult = RoundResult.Draw;
            }
            else if (blueDestroyed)
            {
                _match.LastRoundResult = RoundResult.RedWon;
                _teams[(int)Team.Red].Score++;
            }
            else
            {
                _match.LastRoundResult = RoundResult.BlueWon;
                _teams[(int)Team.Blue].Score++;
            }

            // The units stop moving from the next tick on (see StopIdleUnits): commands issued after this tick's
            // navigation step would have to wait between ticks, outside the snapshot.
            _match.RoundPauseTicks = Math.Max(1, _rules.RoundRestartPauseTicks);
            for (int slot = 0; slot < _units.Length; slot++)
            {
                ref Unit unit = ref _units[slot];
                if (unit.IsAlive)
                {
                    unit.Activity = UnitActivity.Idle;
                    unit.TargetSlot = -1;
                    unit.TargetId = 0;
                }
            }

            AddEvent(GameEventType.RoundEnded, Team.Red, 0, FP3.Zero, (int)_match.LastRoundResult);
        }

        private void StopIdleUnits()
        {
            for (int slot = 0; slot < _units.Length; slot++)
            {
                if (_units[slot].IsAlive && _units[slot].Activity == UnitActivity.Idle)
                {
                    AddCommand(NavCommand.StopAgent(slot));
                }
            }
        }

        private void StartNextRound()
        {
            for (int slot = 0; slot < _units.Length; slot++)
            {
                if (_units[slot].State != UnitState.Free)
                {
                    AddCommand(NavCommand.RemoveAgent(slot));
                }
            }

            ResetUnits();
            ResetTeams(keepScores: true);
            _match.Round++;
            AddEvent(GameEventType.RoundStarted, Team.Red, 0, FP3.Zero, _match.Round);
        }

        private void ResetUnits()
        {
            for (int slot = 0; slot < _units.Length; slot++)
            {
                _units[slot] = default;
                _units[slot].TargetSlot = -1;
            }
        }

        private void ResetTeams(bool keepScores)
        {
            for (int team = 0; team < TeamExtensions.Count; team++)
            {
                int score = keepScores ? _teams[team].Score : 0;
                _teams[team] = new TeamState
                {
                    SmallSpawnTimer = FP.Zero,
                    BigCooldown = FP.Zero,
                    Score = score,
                    BaseHealth = _rules.BaseHealth,
                    UnitCount = 0,
                    SpawnCounter = 0,
                };
            }
        }

        private void AddCommand(NavCommand command)
        {
            _commands[_commandCount++] = command;
        }

        private void AddEvent(GameEventType type, Team team, long unitId, FP3 position, int amount)
        {
            _events.Add(new GameEvent
            {
                Type = type,
                Team = team,
                UnitId = unitId,
                Position = position,
                Amount = amount,
            });
        }

        private void CheckSnapshotBuffer(byte[] buffer)
        {
            if (buffer == null || buffer.Length != SnapshotSize)
            {
                throw new ArgumentException($"A snapshot needs exactly {SnapshotSize} bytes.", nameof(buffer));
            }
        }

        private static byte* Copy(ref MatchState match, byte* destination)
        {
            *(MatchState*)destination = match;
            return destination + sizeof(MatchState);
        }

        private static byte* CopyArray<T>(T[] array, byte* cursor, bool toBuffer)
            where T : unmanaged
        {
            long bytes = (long)sizeof(T) * array.Length;
            fixed (T* elements = array)
            {
                if (toBuffer)
                {
                    Buffer.MemoryCopy(elements, cursor, bytes, bytes);
                }
                else
                {
                    Buffer.MemoryCopy(cursor, elements, bytes, bytes);
                }
            }

            return cursor + bytes;
        }
    }
}
