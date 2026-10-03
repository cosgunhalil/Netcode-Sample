using System.Collections.Generic;
using NetcodeSample.Game;
using NetcodeSample.Rollback;
using NetcodeSample.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace NetcodeSample.Tests.EditMode
{
    public sealed class RollbackSessionTests
    {
        private const string LevelDefinitionPath = "Assets/NetcodeSample/Level/Level.asset";
        private const double TickSeconds = 1.0 / 30.0;

        private LevelData _level;

        [OneTimeSetUp]
        public void LoadLevel()
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionPath);
            Assert.That(definition, Is.Not.Null, $"{LevelDefinitionPath} is missing; run Netcode Sample > Build Level.");
            _level = definition.CreateLevelData();
        }

        [Test]
        public void PeersOverABadNetwork_ConfirmIdenticalTicks_MatchingAPlainSimulation()
        {
            NetworkConditions conditions = new() { LatencyMs = 80, JitterMs = 40, PacketLoss = 0.1 };
            using Match match = new(_level, conditions);
            match.Run(frames: 900, slowBlue: false);

            long common = System.Math.Min(match.Red.Session.ConfirmedTick, match.Blue.Session.ConfirmedTick);
            Assert.That(common, Is.GreaterThan(850), "Peers fell behind confirming ticks.");
            Assert.That(match.Red.Session.Stats.Rollbacks + match.Blue.Session.Stats.Rollbacks, Is.GreaterThan(0), "Big-cube presses should have caused rollbacks.");

            using GameSimulation reference = new(GameRules.Default, _level, new BurstNavigationWorldFactory());
            for (long tick = 1; tick <= common; tick++)
            {
                Assert.That(match.Blue.ConfirmedHashes[tick], Is.EqualTo(match.Red.ConfirmedHashes[tick]), $"Peers disagree at tick {tick}.");
                reference.Step(match.Red.ConfirmedRed[tick], match.Red.ConfirmedBlue[tick]);
                Assert.That(reference.ComputeLightHash(), Is.EqualTo(match.Red.ConfirmedHashes[tick]), $"Rollback result differs from a plain simulation at tick {tick}.");
            }
        }

        [Test]
        public void SlowPeer_IsWaitedFor_WithoutStallingOrDesync()
        {
            using Match match = new(_level, new NetworkConditions { LatencyMs = 50 });
            match.Run(frames: 900, slowBlue: true);

            Assert.That(match.Red.Session.Stats.SkippedTicks, Is.GreaterThan(50), "The faster peer should skip ticks to let the slower one catch up.");
            Assert.That(System.Math.Abs(match.Red.Session.CurrentTick - match.Blue.Session.CurrentTick), Is.LessThanOrEqualTo(3));
            long common = System.Math.Min(match.Red.Session.ConfirmedTick, match.Blue.Session.ConfirmedTick);
            for (long tick = 1; tick <= common; tick++)
            {
                Assert.That(match.Blue.ConfirmedHashes[tick], Is.EqualTo(match.Red.ConfirmedHashes[tick]), $"Peers disagree at tick {tick}.");
            }
        }

        [Test]
        public void LostConnection_StallsAtTheRollbackWindow()
        {
            using Match match = new(_level, new NetworkConditions { PacketLoss = 1.0 });
            match.Run(frames: 60, slowBlue: false);

            RollbackSettings settings = RollbackSettings.Default;
            Assert.That(match.Red.Session.CurrentTick, Is.EqualTo(settings.InputDelayTicks + settings.MaxRollbackTicks));
            Assert.That(match.Red.Session.ConfirmedTick, Is.EqualTo(settings.InputDelayTicks));
            Assert.That(match.Red.Session.Stats.StalledTicks, Is.GreaterThan(0));
        }

        [Test]
        public void SyncTest_ResimulatesIdentically()
        {
            using GameSimulation simulation = new(GameRules.Default, _level, new BurstNavigationWorldFactory());
            SyncTestSession syncTest = new(simulation, checkDistance: 7);
            for (int i = 0; i < 300; i++)
            {
                long tick = simulation.Tick + 1;
                syncTest.Advance(tick % 90 == 5 ? GameInput.SpawnBig : GameInput.None, tick % 150 == 20 ? GameInput.SpawnBig : GameInput.None);
            }

            Assert.That(syncTest.FirstMismatchTick, Is.EqualTo(-1));
        }

        private sealed class Match : System.IDisposable
        {
            private readonly LoopbackNetwork _network;

            public Match(LevelData level, NetworkConditions conditions)
            {
                _network = new LoopbackNetwork(seed: 7) { Conditions = conditions };
                Red = new Peer(level, Team.Red, _network.First);
                Blue = new Peer(level, Team.Blue, _network.Second);
            }

            public Peer Red { get; }

            public Peer Blue { get; }

            // Both peers press the big-cube button at their own rhythm; a slow blue misses every tenth frame.
            public void Run(int frames, bool slowBlue)
            {
                double now = 0;
                for (int frame = 0; frame < frames; frame++)
                {
                    now += TickSeconds;
                    _network.Update(now);
                    Red.Pending |= frame % 70 == 3;
                    Blue.Pending |= frame % 110 == 50;
                    Red.Advance();
                    if (!slowBlue || frame % 10 != 0)
                    {
                        Blue.Advance();
                    }
                }

                // Let in-flight inputs arrive so the last ticks confirm.
                for (int frame = 0; frame < 30; frame++)
                {
                    now += TickSeconds;
                    _network.Update(now);
                    Red.Session.SendInputs();
                    Blue.Session.SendInputs();
                    Red.Advance();
                    Blue.Advance();
                }
            }

            public void Dispose()
            {
                Red.Simulation.Dispose();
                Blue.Simulation.Dispose();
            }
        }

        private sealed class Peer : ITickObserver
        {
            private readonly ulong[] _latestHashes = new ulong[64];

            public Peer(LevelData level, Team team, IInputTransport transport)
            {
                Simulation = new GameSimulation(GameRules.Default, level, new BurstNavigationWorldFactory());
                Session = new RollbackSession(Simulation, team, transport, RollbackSettings.Default, this);
            }

            public GameSimulation Simulation { get; }

            public RollbackSession Session { get; }

            public bool Pending { get; set; }

            public Dictionary<long, ulong> ConfirmedHashes { get; } = new();

            public Dictionary<long, GameInput> ConfirmedRed { get; } = new();

            public Dictionary<long, GameInput> ConfirmedBlue { get; } = new();

            public void Advance()
            {
                if (Session.Advance(Pending ? GameInput.SpawnBig : GameInput.None) == AdvanceResult.Simulated)
                {
                    Pending = false;
                }
            }

            public void OnTickSimulated(long tick)
            {
                _latestHashes[tick % _latestHashes.Length] = Simulation.ComputeLightHash();
            }

            public void OnTickConfirmed(long tick, GameInput red, GameInput blue)
            {
                ConfirmedHashes[tick] = _latestHashes[tick % _latestHashes.Length];
                ConfirmedRed[tick] = red;
                ConfirmedBlue[tick] = blue;
            }
        }
    }
}
