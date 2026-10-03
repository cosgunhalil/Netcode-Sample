using System;
using System.IO;
using NetcodeSample.Simulation;
using Tickwise;

namespace NetcodeSample.Determinism
{
    /// <summary>
    /// Records a match into a Tickwise <c>.rec</c> file: both players' inputs and the state hashes for every tick,
    /// state dumps on an interval, and markers at round boundaries. Record only ticks that will never be
    /// re-simulated (all of them in hot-seat; confirmed ticks under rollback).
    /// </summary>
    public sealed class MatchRecorder : IDisposable
    {
        /// <summary>The layout of the input bytes: red buttons, then blue buttons. Bump when it changes.</summary>
        public const ulong InputFormatId = 1;

        private const string GameId = "netcode-sample";
        private const uint FullHashInterval = 30;
        private const uint DumpInterval = 150;

        private readonly TickwiseRecorder _recorder;
        private readonly SimulationProbe _probe;
        private readonly GameSimulation _simulation;

        private MatchRecorder(string path, TickwiseRecorder recorder, GameSimulation simulation)
        {
            Path = path;
            _recorder = recorder;
            _simulation = simulation;
            _probe = new SimulationProbe(simulation);
        }

        public string Path { get; }

        public long TicksRecorded { get; private set; }

        /// <summary>Creates the file (replacing one at the same path) and its folder.</summary>
        public static MatchRecorder Create(string path, GameSimulation simulation, string platform)
        {
            string folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            TickwiseRecorder recorder = TickwiseRecorder.Create(path, CreateConfig(simulation, platform));
            return new MatchRecorder(path, recorder, simulation);
        }

        /// <summary>
        /// The recorder settings. Recordings meant to be compared must use the same settings, so the replay in
        /// <see cref="SelfCheck"/> uses this too.
        /// </summary>
        public static RecorderConfig CreateConfig(GameSimulation simulation, string platform)
        {
            return new RecorderConfig
            {
                GameId = GameId,
                BuildHash = DescribeBuild(simulation),
                Platform = platform ?? string.Empty,
                TickRate = (uint)simulation.Rules.TickRate,
                FullHashInterval = FullHashInterval,
                DumpInterval = DumpInterval,
                HashAlgoId = HashAlgo.UserDefined,
                InputFormatId = InputFormatId,
            }.StampCreatedAt();
        }

        /// <summary>What both peers (or a recording and its replay) must share to be comparable.</summary>
        public static string DescribeBuild(GameSimulation simulation)
        {
            return $"rules:{simulation.Rules.ComputeHash():X16};navmesh:{simulation.NavMeshChecksum:X16}";
        }

        /// <summary>Records the tick the simulation just stepped, with the inputs it consumed.</summary>
        public void RecordTick(GameInput red, GameInput blue)
        {
            Span<byte> inputs = stackalloc byte[2];
            inputs[0] = red.Buttons;
            inputs[1] = blue.Buttons;

            ulong tick = (ulong)_simulation.Tick;
            _recorder.RecordTick(tick, inputs, _probe);
            TicksRecorded++;

            foreach (GameEvent gameEvent in _simulation.Events)
            {
                if (gameEvent.Type == GameEventType.RoundEnded)
                {
                    _recorder.RecordMarker(tick, $"round-ended:{(RoundResult)gameEvent.Amount}");
                }
                else if (gameEvent.Type == GameEventType.RoundStarted)
                {
                    _recorder.RecordMarker(tick, $"round-started:{gameEvent.Amount}");
                }
            }
        }

        public void RecordMarker(string label)
        {
            _recorder.RecordMarker((ulong)_simulation.Tick, label);
        }

        /// <summary>Writes the index and trailer; a file without them can't be read.</summary>
        public void Dispose()
        {
            _recorder.Dispose();
        }

        /// <summary>Splits recorded input bytes back into both players' inputs.</summary>
        public static void DecodeInputs(ReadOnlySpan<byte> inputs, out GameInput red, out GameInput blue)
        {
            if (inputs.Length != 2)
            {
                throw new ArgumentException($"Expected 2 input bytes, got {inputs.Length}.", nameof(inputs));
            }

            red = new GameInput(inputs[0]);
            blue = new GameInput(inputs[1]);
        }
    }
}
