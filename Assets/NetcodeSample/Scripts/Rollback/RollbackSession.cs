using System;
using NetcodeSample.Simulation;

namespace NetcodeSample.Rollback
{
    public struct RollbackSettings
    {
        /// <summary>Local inputs apply this many ticks after they're sampled, hiding that much latency.</summary>
        public int InputDelayTicks;

        /// <summary>How far the simulation may run ahead of the last known remote input before it stalls.</summary>
        public int MaxRollbackTicks;

        public static RollbackSettings Default => new() { InputDelayTicks = 2, MaxRollbackTicks = 8 };

        public bool Validate(out string error)
        {
            error = null;
            if (InputDelayTicks < 0 || MaxRollbackTicks < 1)
            {
                error = "InputDelayTicks can't be negative and MaxRollbackTicks must be at least 1.";
            }
            else if (InputDelayTicks + MaxRollbackTicks + 2 > InputMessage.MaxInputs)
            {
                error = $"InputDelayTicks + MaxRollbackTicks must stay below {InputMessage.MaxInputs - 2}, the inputs one message carries.";
            }

            return error == null;
        }
    }

    public enum AdvanceResult
    {
        /// <summary>A new tick was simulated; the local input passed in was consumed.</summary>
        Simulated,

        /// <summary>Too far ahead of the remote peer's inputs to predict further; nothing was simulated.</summary>
        Stalled,

        /// <summary>Waited a tick so the other peer can catch up (time sync); nothing was simulated.</summary>
        Skipped,
    }

    /// <summary>Notified as ticks are simulated and confirmed, e.g. to record confirmed ticks with Tickwise.</summary>
    public interface ITickObserver
    {
        /// <summary>The simulation just stepped this tick, for the first time or in a re-simulation.</summary>
        void OnTickSimulated(long tick);

        /// <summary>This tick is final: both inputs are known and it will never be re-simulated. Called in tick order.</summary>
        void OnTickConfirmed(long tick, GameInput red, GameInput blue);
    }

    public struct RollbackStats
    {
        public int Rollbacks;
        public long ResimulatedTicks;
        public int MaxRollbackDepth;
        public int StalledTicks;
        public int SkippedTicks;
        public long MessagesReceived;
    }

    /// <summary>
    /// One peer of a two-player rollback match. Each fixed tick, <see cref="Advance"/> takes the local input,
    /// applies remote inputs that arrived, re-simulates from the first tick whose prediction was wrong, and
    /// simulates the next tick with the remote input predicted as "nothing pressed" when it isn't known yet.
    /// </summary>
    public sealed class RollbackSession
    {
        private const int HistoryCapacity = 256;
        private const int AdvantageSamples = 32;
        private const int MinTicksBetweenSkips = 10;

        private readonly GameSimulation _simulation;
        private readonly IInputTransport _transport;
        private readonly ITickObserver _observer;
        private readonly RollbackSettings _settings;
        private readonly InputHistory _localInputs = new(HistoryCapacity);
        private readonly InputHistory _remoteInputs = new(HistoryCapacity);

        // The remote input each simulated tick used (known or predicted), to detect mispredictions.
        private readonly byte[] _usedRemoteInputs;
        private readonly byte[][] _snapshots;
        private readonly long[] _snapshotTicks;
        private readonly float[] _advantageSamples = new float[AdvantageSamples];
        private int _advantageSampleCount;
        private int _ticksSinceSkip;
        private long _remoteSenderTick;
        private int _remoteAdvantage;
        private long _remoteAck;
        private RollbackStats _stats;

        public RollbackSession(GameSimulation simulation, Team localTeam, IInputTransport transport, RollbackSettings settings, ITickObserver observer = null)
        {
            if (!settings.Validate(out string error))
            {
                throw new ArgumentException(error, nameof(settings));
            }

            if (simulation.Tick != 0)
            {
                throw new ArgumentException("The simulation must not have been stepped yet.", nameof(simulation));
            }

            _simulation = simulation;
            _transport = transport;
            _settings = settings;
            _observer = observer;
            LocalTeam = localTeam;

            // A tick's snapshot is the state before it; rolling back to the oldest unconfirmed tick needs
            // MaxRollbackTicks of them, plus the one being simulated.
            int snapshotCount = settings.MaxRollbackTicks + 2;
            _snapshots = new byte[snapshotCount][];
            _snapshotTicks = new long[snapshotCount];
            for (int i = 0; i < snapshotCount; i++)
            {
                _snapshots[i] = new byte[simulation.SnapshotSize];
                _snapshotTicks[i] = -1;
            }

            _usedRemoteInputs = new byte[HistoryCapacity];

            // Both peers agree that nobody pressed anything during the first InputDelayTicks ticks.
            for (long tick = 1; tick <= settings.InputDelayTicks; tick++)
            {
                _localInputs.Set(tick, GameInput.None);
                _remoteInputs.Set(tick, GameInput.None);
            }
        }

        public Team LocalTeam { get; }

        public GameSimulation Simulation => _simulation;

        public RollbackSettings Settings => _settings;

        /// <summary>The last simulated tick (possibly with predicted remote inputs).</summary>
        public long CurrentTick => _simulation.Tick;

        /// <summary>The last tick that is final on this peer.</summary>
        public long ConfirmedTick { get; private set; }

        /// <summary>Every remote input up to this tick has arrived.</summary>
        public long RemoteInputTick => _remoteInputs.ContiguousTick;

        /// <summary>The averaged frame advantage over the other peer; positive means this peer is ahead.</summary>
        public float FrameAdvantage { get; private set; }

        public RollbackStats Stats => _stats;

        /// <summary>
        /// Runs one fixed tick. <paramref name="localInput"/> is this peer's input sampled now; it applies
        /// <see cref="RollbackSettings.InputDelayTicks"/> ticks later. When the result isn't
        /// <see cref="AdvanceResult.Simulated"/> the input wasn't consumed: keep it for the next call.
        /// </summary>
        public AdvanceResult Advance(GameInput localInput)
        {
            ReceiveRemoteInputs();
            ConfirmTicks();

            long nextTick = CurrentTick + 1;
            if (nextTick - RemoteInputTick > _settings.MaxRollbackTicks)
            {
                _stats.StalledTicks++;
                SendInputs();
                return AdvanceResult.Stalled;
            }

            _ticksSinceSkip++;
            if (FrameAdvantage >= 1f && _ticksSinceSkip >= MinTicksBetweenSkips)
            {
                _ticksSinceSkip = 0;
                _stats.SkippedTicks++;

                // The skip changes the advantage; measure it afresh instead of skipping again on old samples.
                _advantageSampleCount = 0;
                SendInputs();
                return AdvanceResult.Skipped;
            }

            _localInputs.Set(nextTick + _settings.InputDelayTicks, localInput);
            SimulateTick(nextTick);
            ConfirmTicks();
            SendInputs();
            return AdvanceResult.Simulated;
        }

        /// <summary>Sends this peer's unacknowledged inputs; call when not advancing (e.g. paused) to keep acks flowing.</summary>
        public void SendInputs()
        {
            // Measured when sending, the same moment the other peer measures the advantage it sends us.
            UpdateTimeSync();

            long latest = _localInputs.ContiguousTick;
            long first = Math.Max(_remoteAck + 1, latest - InputMessage.MaxInputs + 1);
            InputMessage message = new()
            {
                StartTick = first,
                Count = (int)Math.Max(0, latest - first + 1),
                AckTick = _remoteInputs.ContiguousTick,
                SenderTick = CurrentTick,
                SenderAdvantage = (int)(CurrentTick - _remoteSenderTick),
            };

            unsafe
            {
                for (int i = 0; i < message.Count; i++)
                {
                    message.Inputs[i] = _localInputs.Get(first + i).Buttons;
                }
            }

            _transport.Send(message);
        }

        private void ReceiveRemoteInputs()
        {
            long firstMisprediction = long.MaxValue;
            while (_transport.TryReceive(out InputMessage message))
            {
                _stats.MessagesReceived++;
                _remoteAck = Math.Max(_remoteAck, message.AckTick);
                if (message.SenderTick >= _remoteSenderTick)
                {
                    _remoteSenderTick = message.SenderTick;
                    _remoteAdvantage = message.SenderAdvantage;
                }

                for (long tick = message.StartTick; tick <= message.EndTick; tick++)
                {
                    GameInput input = new(message.GetInput(tick));
                    if (tick <= ConfirmedTick || !_remoteInputs.Set(tick, input))
                    {
                        continue;
                    }

                    if (tick <= CurrentTick && _usedRemoteInputs[tick % HistoryCapacity] != input.Buttons)
                    {
                        firstMisprediction = Math.Min(firstMisprediction, tick);
                    }
                }
            }

            if (firstMisprediction != long.MaxValue)
            {
                RollBackTo(firstMisprediction);
            }
        }

        // Restores the state before `tick` and re-simulates up to the present with the inputs known now.
        private void RollBackTo(long tick)
        {
            long present = CurrentTick;
            int depth = (int)(present - tick + 1);
            int slot = SnapshotSlot(tick);
            if (_snapshotTicks[slot] != tick)
            {
                throw new InvalidOperationException($"No snapshot for tick {tick}; the rollback window was exceeded.");
            }

            _simulation.LoadSnapshot(_snapshots[slot]);
            for (long resimulated = tick; resimulated <= present; resimulated++)
            {
                SimulateTick(resimulated);
            }

            _stats.Rollbacks++;
            _stats.ResimulatedTicks += depth;
            _stats.MaxRollbackDepth = Math.Max(_stats.MaxRollbackDepth, depth);
        }

        private void SimulateTick(long tick)
        {
            int slot = SnapshotSlot(tick);
            _simulation.SaveSnapshot(_snapshots[slot]);
            _snapshotTicks[slot] = tick;

            GameInput local = _localInputs.Get(tick);
            GameInput remote = _remoteInputs.Contains(tick) ? _remoteInputs.Get(tick) : GameInput.None;
            _usedRemoteInputs[tick % HistoryCapacity] = remote.Buttons;

            if (LocalTeam == Team.Red)
            {
                _simulation.Step(local, remote);
            }
            else
            {
                _simulation.Step(remote, local);
            }

            _observer?.OnTickSimulated(tick);
        }

        // A tick is final once it has been simulated with both real inputs and every tick before it is final.
        private void ConfirmTicks()
        {
            while (ConfirmedTick < CurrentTick && _remoteInputs.Contains(ConfirmedTick + 1))
            {
                long tick = ConfirmedTick + 1;
                ConfirmedTick = tick;
                GameInput local = _localInputs.Get(tick);
                GameInput remote = _remoteInputs.Get(tick);
                if (LocalTeam == Team.Red)
                {
                    _observer?.OnTickConfirmed(tick, local, remote);
                }
                else
                {
                    _observer?.OnTickConfirmed(tick, remote, local);
                }
            }
        }

        // Frame advantage, symmetric so latency cancels: if both peers run in step with one-way latency L, both
        // see an advantage of L; if this one is d ticks ahead, it sees L + d and the other L - d.
        private void UpdateTimeSync()
        {
            if (_stats.MessagesReceived == 0)
            {
                return;
            }

            float localAdvantage = CurrentTick - _remoteSenderTick;
            float sample = (localAdvantage - _remoteAdvantage) / 2f;
            _advantageSamples[_advantageSampleCount % AdvantageSamples] = sample;
            _advantageSampleCount++;

            int count = Math.Min(_advantageSampleCount, AdvantageSamples);
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += _advantageSamples[i];
            }

            FrameAdvantage = sum / count;
        }

        private int SnapshotSlot(long tick)
        {
            return (int)(tick % _snapshots.Length);
        }
    }
}
