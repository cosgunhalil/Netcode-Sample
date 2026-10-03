using System.Collections.Generic;
using DPF.Core.Numerics;
using NetcodeSample.Game;
using NetcodeSample.Simulation;
using NetcodeSample.Simulation.Navigation;
using NUnit.Framework;
using UnityEditor;

namespace NetcodeSample.Tests.EditMode
{
    public sealed class GameSimulationTests
    {
        private const string LevelDefinitionPath = "Assets/NetcodeSample/Level/Level.asset";

        private LevelData _level;

        [OneTimeSetUp]
        public void LoadLevel()
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionPath);
            Assert.That(definition, Is.Not.Null, $"{LevelDefinitionPath} is missing; run Netcode Sample > Build Level.");
            _level = definition.CreateLevelData();
        }

        [Test]
        public void SameInputs_ProduceSameHashesEveryTick()
        {
            using GameSimulation first = Create(new ManagedNavigationWorldFactory());
            using GameSimulation second = Create(new ManagedNavigationWorldFactory());

            for (int i = 0; i < 600; i++)
            {
                StepScripted(first);
                StepScripted(second);
                Assert.That(second.ComputeLightHash(), Is.EqualTo(first.ComputeLightHash()), $"Light hash differs at tick {first.Tick}.");
                if (first.Tick % 60 == 0)
                {
                    Assert.That(second.ComputeFullHash(), Is.EqualTo(first.ComputeFullHash()), $"Full hash differs at tick {first.Tick}.");
                }
            }
        }

        [Test]
        public void LoadingASnapshot_ReproducesTheSameFuture()
        {
            using GameSimulation simulation = Create(new ManagedNavigationWorldFactory());
            for (int i = 0; i < 200; i++)
            {
                StepScripted(simulation);
            }

            byte[] snapshot = new byte[simulation.SnapshotSize];
            simulation.SaveSnapshot(snapshot);

            List<ulong> expected = new();
            for (int i = 0; i < 150; i++)
            {
                StepScripted(simulation);
                expected.Add(simulation.ComputeFullHash());
            }

            simulation.LoadSnapshot(snapshot);
            Assert.That(simulation.Tick, Is.EqualTo(200));
            for (int i = 0; i < expected.Count; i++)
            {
                StepScripted(simulation);
                Assert.That(simulation.ComputeFullHash(), Is.EqualTo(expected[i]), $"Re-simulated tick {simulation.Tick} differs.");
            }
        }

        [Test]
        public void ManagedAndBurstNavigation_ProduceSameHashes()
        {
            using GameSimulation managed = Create(new ManagedNavigationWorldFactory());
            using GameSimulation burst = Create(new BurstNavigationWorldFactory());

            for (int i = 0; i < 300; i++)
            {
                StepScripted(managed);
                StepScripted(burst);
                Assert.That(burst.ComputeLightHash(), Is.EqualTo(managed.ComputeLightHash()), $"Light hash differs at tick {managed.Tick}.");
            }

            Assert.That(burst.ComputeFullHash(), Is.EqualTo(managed.ComputeFullHash()));
        }

        [Test]
        public void Spawning_FollowsIntervalAndCooldown()
        {
            using GameSimulation simulation = Create(new ManagedNavigationWorldFactory());
            int[] small = new int[2];
            int[] big = new int[2];

            // 10 seconds with both big-cube buttons held: small cubes every second, big ones every 5 seconds.
            for (int i = 0; i < 300; i++)
            {
                simulation.Step(GameInput.SpawnBig, GameInput.SpawnBig);
                foreach (GameEvent gameEvent in simulation.Events)
                {
                    if (gameEvent.Type == GameEventType.UnitSpawned)
                    {
                        bool isBig = (gameEvent.UnitId & 1) == (int)UnitKind.Big;
                        (isBig ? big : small)[(int)gameEvent.Team]++;
                    }
                }
            }

            Assert.That(small, Is.EqualTo(new[] { 10, 10 }), "Small cubes per team");
            Assert.That(big, Is.EqualTo(new[] { 2, 2 }), "Big cubes per team");
        }

        [Test]
        public void DestroyedBase_EndsRoundScoresAndRestarts()
        {
            GameRules rules = GameRules.Default;

            // The first cube to reach a base ends the round; with no fighting both teams march straight through.
            rules.BaseHealth = 1;
            rules.AcquireRadius = FP.Zero;
            using GameSimulation simulation = new(rules, _level, new BurstNavigationWorldFactory());

            RoundResult result = RoundResult.None;
            for (int i = 0; i < 6000 && result == RoundResult.None; i++)
            {
                simulation.Step(GameInput.None, GameInput.None);
                foreach (GameEvent gameEvent in simulation.Events)
                {
                    if (gameEvent.Type == GameEventType.RoundEnded)
                    {
                        result = (RoundResult)gameEvent.Amount;
                    }
                }
            }

            Assert.That(result, Is.Not.EqualTo(RoundResult.None), "No round ended within 200 seconds.");
            int redScore = simulation.GetTeam(Team.Red).Score;
            int blueScore = simulation.GetTeam(Team.Blue).Score;
            Assert.That(redScore, Is.EqualTo(result == RoundResult.RedWon ? 1 : 0));
            Assert.That(blueScore, Is.EqualTo(result == RoundResult.BlueWon ? 1 : 0));

            int pauseTicks = simulation.Match.RoundPauseTicks;
            Assert.That(pauseTicks, Is.EqualTo(rules.RoundRestartPauseTicks));
            for (int i = 0; i < pauseTicks; i++)
            {
                simulation.Step(GameInput.None, GameInput.None);
            }

            Assert.That(simulation.Match.Round, Is.EqualTo(1));
            Assert.That(simulation.Match.RoundPauseTicks, Is.Zero);
            foreach (Team team in new[] { Team.Red, Team.Blue })
            {
                Assert.That(simulation.GetTeam(team).BaseHealth, Is.EqualTo(rules.BaseHealth), $"{team} base health after reset");
                Assert.That(simulation.GetTeam(team).UnitCount, Is.Zero, $"{team} units after reset");
            }

            Assert.That(simulation.GetTeam(Team.Red).Score, Is.EqualTo(redScore), "Scores survive the reset.");
            Assert.That(simulation.GetTeam(Team.Blue).Score, Is.EqualTo(blueScore), "Scores survive the reset.");
        }

        [Test]
        public void TimeLimit_EndsTheRoundOnTheTiebreak()
        {
            GameRules rules = GameRules.Default;
            rules.RoundTimeLimit = FP.FromInt(5);
            using GameSimulation simulation = new(rules, _level, new ManagedNavigationWorldFactory());

            // After 5 s both bases are untouched and both sides have spawned the same cubes: a draw, at the limit.
            RoundResult result = RoundResult.None;
            long endTick = 0;
            for (int i = 0; i < 300 && result == RoundResult.None; i++)
            {
                simulation.Step(GameInput.None, GameInput.None);
                foreach (GameEvent gameEvent in simulation.Events)
                {
                    if (gameEvent.Type == GameEventType.RoundEnded)
                    {
                        result = (RoundResult)gameEvent.Amount;
                        endTick = simulation.Tick;
                    }
                }
            }

            Assert.That(endTick, Is.EqualTo(rules.RoundTimeLimitTicks));
            Assert.That(result, Is.EqualTo(RoundResult.Draw));
            Assert.That(simulation.GetTeam(Team.Red).Score + simulation.GetTeam(Team.Blue).Score, Is.Zero);
        }

        private static void StepScripted(GameSimulation simulation)
        {
            // Both sides press at different, fixed rhythms so the inputs aren't symmetric.
            long tick = simulation.Tick + 1;
            GameInput red = tick % 90 == 5 ? GameInput.SpawnBig : GameInput.None;
            GameInput blue = tick % 150 == 20 ? GameInput.SpawnBig : GameInput.None;
            simulation.Step(red, blue);
        }

        private GameSimulation Create(INavigationWorldFactory navigation)
        {
            return new GameSimulation(GameRules.Default, _level, navigation);
        }
    }
}
