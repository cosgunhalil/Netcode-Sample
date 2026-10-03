using System;
using System.IO;
using NetcodeSample.Determinism;
using NetcodeSample.Game;
using NetcodeSample.Simulation;
using NUnit.Framework;
using UnityEditor;

namespace NetcodeSample.Tests.EditMode
{
    /// <summary>Records sessions with Tickwise and self-checks them. Needs the locally built tickwise_ffi.dll.</summary>
    public sealed class TickwiseSelfCheckTests
    {
        private const string LevelDefinitionPath = "Assets/NetcodeSample/Level/Level.asset";
        private const int TickCount = 400;
        private const long ChaosFromTick = 100;

        // Small cubes spawn on ticks 1, 31, 61, ...; the first spawn the planted bug can move is the first at or
        // after ChaosFromTick.
        private const long FirstChaoticSpawnTick = 121;

        private LevelData _level;
        private string _folder;

        [OneTimeSetUp]
        public void SetUp()
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelDefinitionPath);
            Assert.That(definition, Is.Not.Null, $"{LevelDefinitionPath} is missing; run Netcode Sample > Build Level.");
            _level = definition.CreateLevelData();
            _folder = Path.Combine(Path.GetTempPath(), "netcode-sample-tickwise-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [Test]
        public void CleanRecording_ReplaysIdentically()
        {
            string path = Record("clean.rec", chaos: false);

            SelfCheckResult result = SelfCheck.Run(path, GameRules.Default, _level, new BurstNavigationWorldFactory());

            Assert.That(result.IsDeterministic, Is.True, $"Diverged at tick {result.FirstMismatchTick}.");
            Assert.That(result.TicksReplayed, Is.EqualTo(TickCount));
        }

        [Test]
        public void ChaosRecording_DivergesAtTheFirstChaoticSpawn()
        {
            string path = Record("chaos.rec", chaos: true);

            SelfCheckResult result = SelfCheck.Run(path, GameRules.Default, _level, new BurstNavigationWorldFactory(), Path.Combine(_folder, "chaos.replay.rec"));

            // Each chaotic spawn has a 1 in 8 chance of landing where the replay puts it; two per spawn tick and
            // several spawn ticks make missing all of them practically impossible.
            Assert.That(result.IsDeterministic, Is.False, "The planted bug went unnoticed.");
            Assert.That(result.FirstMismatchTick, Is.GreaterThanOrEqualTo(FirstChaoticSpawnTick));
            Assert.That(File.Exists(result.ReplayPath), Is.True);
        }

        private string Record(string fileName, bool chaos)
        {
            string path = Path.Combine(_folder, fileName);
            using GameSimulation simulation = new(GameRules.Default, _level, new BurstNavigationWorldFactory());
            simulation.Chaos = new ChaosSettings { Enabled = chaos, FromTick = ChaosFromTick };
            using MatchRecorder recorder = MatchRecorder.Create(path, simulation, "EditMode test");
            for (int i = 0; i < TickCount; i++)
            {
                long tick = simulation.Tick + 1;
                GameInput red = tick % 90 == 5 ? GameInput.SpawnBig : GameInput.None;
                GameInput blue = tick % 150 == 20 ? GameInput.SpawnBig : GameInput.None;
                simulation.Step(red, blue);
                recorder.RecordTick(red, blue);
            }

            return path;
        }
    }
}
