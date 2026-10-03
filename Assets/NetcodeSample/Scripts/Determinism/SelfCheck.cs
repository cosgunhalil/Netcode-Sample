using System;
using NetcodeSample.Simulation;
using NetcodeSample.Simulation.Navigation;
using Tickwise;

namespace NetcodeSample.Determinism
{
    /// <summary>The outcome of <see cref="SelfCheck.Run"/>.</summary>
    public readonly struct SelfCheckResult
    {
        public SelfCheckResult(long ticksReplayed, long firstMismatchTick, string replayPath)
        {
            TicksReplayed = ticksReplayed;
            FirstMismatchTick = firstMismatchTick;
            ReplayPath = replayPath;
        }

        public long TicksReplayed { get; }

        /// <summary>The first tick whose hash differs from the recording, or -1 when every tick matched.</summary>
        public long FirstMismatchTick { get; }

        public bool IsDeterministic => FirstMismatchTick < 0;

        /// <summary>The recording of the replay, for <c>tickwise compare</c> and <c>diff</c>; null when not written.</summary>
        public string ReplayPath { get; }
    }

    /// <summary>
    /// Tickwise's self-check: replays a recording's inputs through a fresh simulation and checks every tick's hash
    /// against the recording. Anything but a full match means the simulation isn't deterministic.
    /// </summary>
    public static class SelfCheck
    {
        /// <summary>
        /// Pass 1 verifies hashes and stops at the first mismatch. When <paramref name="replayPath"/> is given,
        /// pass 2 re-records the whole replay there (with dumps on the same interval), so
        /// <c>tickwise compare original replay</c> and <c>tickwise diff original replay --at tick</c> can name the
        /// tick and the field. The rules and level must be the ones the recording was made with.
        /// </summary>
        public static SelfCheckResult Run(string recordingPath, GameRules rules, LevelData level, INavigationWorldFactory navigation, string replayPath = null)
        {
            long ticks = 0;
            long mismatch = -1;
            using (GameSimulation simulation = new(rules, level, navigation))
            using (TickwiseReplayer replayer = TickwiseReplayer.Open(recordingPath, CreateOptions(verifyHashes: true)))
            {
                SimulationProbe probe = new(simulation);
                while (replayer.TryNextStep(out ulong tick, out ReadOnlySpan<byte> inputs))
                {
                    Step(simulation, tick, inputs);
                    ticks++;
                    try
                    {
                        replayer.AfterTick(tick, probe);
                    }
                    catch (TickwiseException exception) when (exception.Status == TickwiseStatus.HashMismatch)
                    {
                        mismatch = (long)tick;
                        break;
                    }
                }
            }

            if (replayPath != null)
            {
                Rerecord(recordingPath, rules, level, navigation, replayPath);
            }

            return new SelfCheckResult(ticks, mismatch, replayPath);
        }

        private static void Rerecord(string recordingPath, GameRules rules, LevelData level, INavigationWorldFactory navigation, string replayPath)
        {
            using GameSimulation simulation = new(rules, level, navigation);
            using TickwiseReplayer replayer = TickwiseReplayer.Open(recordingPath, CreateOptions(verifyHashes: false));
            using MatchRecorder recorder = MatchRecorder.Create(replayPath, simulation, "self-check replay");
            SimulationProbe probe = new(simulation);
            while (replayer.TryNextStep(out ulong tick, out ReadOnlySpan<byte> inputs))
            {
                MatchRecorder.DecodeInputs(inputs, out GameInput red, out GameInput blue);
                Step(simulation, tick, inputs);
                recorder.RecordTick(red, blue);
                replayer.AfterTick(tick, probe);
            }
        }

        private static ReplayOptions CreateOptions(bool verifyHashes)
        {
            return new ReplayOptions
            {
                VerifyHashes = verifyHashes,
                CheckInputFormat = true,
                ExpectedInputFormatId = MatchRecorder.InputFormatId,
            };
        }

        private static void Step(GameSimulation simulation, ulong tick, ReadOnlySpan<byte> inputs)
        {
            if ((ulong)(simulation.Tick + 1) != tick)
            {
                throw new InvalidOperationException(
                    $"The recording continues at tick {tick}, but the simulation is at tick {simulation.Tick}. Recordings must start at tick 1.");
            }

            MatchRecorder.DecodeInputs(inputs, out GameInput red, out GameInput blue);
            simulation.Step(red, blue);
        }
    }
}
