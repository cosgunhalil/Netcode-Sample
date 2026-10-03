using System.IO;
using DPF.Unity;
using NetcodeSample.Determinism;
using NetcodeSample.Rollback;
using NetcodeSample.Simulation;
using NetcodeSample.Simulation.Navigation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NetcodeSample.Game.Local
{
    public enum LocalMatchMode
    {
        /// <summary>One simulation, both players on one keyboard, no rollback.</summary>
        HotSeat,

        /// <summary>Hot-seat, but every tick rolls back and re-simulates to check snapshots are complete.</summary>
        SyncTest,

        /// <summary>Two rollback peers in this process, talking through a simulated network.</summary>
        Loopback,
    }

    /// <summary>
    /// Runs a match without real networking, for developing and checking the game and the rollback layer.
    /// Space is red's big-cube button, Enter is blue's. Every mode records its confirmed ticks with Tickwise.
    /// </summary>
    public sealed class LocalMatchRunner : MonoBehaviour
    {
        // At most this many ticks per frame, so a long frame can't make the game spiral.
        private const int MaxStepsPerFrame = 4;

        [SerializeField]
        private LocalMatchMode _mode = LocalMatchMode.HotSeat;

        [SerializeField]
        private LevelDefinition _level;

        [SerializeField]
        private GameRulesAsset _rules;

        [SerializeField]
        [Tooltip("Burst runs navigation on Unity's job system; off uses the single-threaded managed world. Both give identical results.")]
        private bool _useBurstNavigation = true;

        [SerializeField]
        private bool _frameCameraOnStart = true;

        [Header("Tickwise")]
        [SerializeField]
        [Tooltip("Records confirmed ticks to persistentDataPath/tickwise.")]
        private bool _record = true;

        [SerializeField]
        [Tooltip("Plants a determinism bug from Chaos From Tick on: spawn offsets taken from the wall clock. In Loopback only the red peer gets it. Also toggled on screen.")]
        private bool _chaos;

        [SerializeField]
        [Min(1)]
        private long _chaosFromTick = 300;

        [Header("Sync test")]
        [SerializeField]
        [Range(1, 16)]
        private int _syncTestDistance = 7;

        [Header("Loopback")]
        [SerializeField]
        [Range(0, 15)]
        private int _inputDelayTicks = 2;

        [SerializeField]
        [Range(1, 15)]
        private int _maxRollbackTicks = 8;

        [SerializeField]
        [Range(0f, 300f)]
        private float _latencyMs = 80f;

        [SerializeField]
        [Range(0f, 150f)]
        private float _jitterMs = 30f;

        [SerializeField]
        [Range(0f, 0.5f)]
        private float _packetLoss = 0.05f;

        [SerializeField]
        [Tooltip("Whose simulation is drawn.")]
        private Team _viewedPeer = Team.Red;

        private readonly Peer[] _peers = new Peer[TeamExtensions.Count];
        private GameRules _gameRules;
        private LevelData _levelData;
        private SyncTestSession _syncTest;
        private LoopbackNetwork _network;
        private double _networkClock;
        private PlaceholderView _view;
        private float _tickSeconds;
        private float _accumulator;

        /// <summary>The simulation being drawn.</summary>
        public GameSimulation ViewedSimulation => _mode == LocalMatchMode.Loopback ? _peers[(int)_viewedPeer].Simulation : _peers[0].Simulation;

        private void Start()
        {
            if (_level == null || _rules == null)
            {
                Debug.LogError("LocalMatchRunner needs a Level Definition and Game Rules. Run Netcode Sample > Set Up Local Match Scene.", this);
                enabled = false;
                return;
            }

            _gameRules = _rules.ToRules();
            _levelData = _level.CreateLevelData();
            _tickSeconds = 1f / _gameRules.TickRate;

            switch (_mode)
            {
                case LocalMatchMode.HotSeat:
                    _peers[0] = CreatePeer(TickwiseSupport.CreateRecordingPath("hotseat"));
                    break;
                case LocalMatchMode.SyncTest:
                    _peers[0] = CreatePeer(TickwiseSupport.CreateRecordingPath("synctest"));
                    _syncTest = new SyncTestSession(_peers[0].Simulation, _syncTestDistance);
                    break;
                case LocalMatchMode.Loopback:
                    StartLoopback();
                    break;
            }

            ApplyChaos(_chaos, _chaosFromTick);
            _view = new PlaceholderView(ViewedSimulation);
            if (_frameCameraOnStart)
            {
                FrameCamera();
            }

            Debug.Log($"{_mode} started: rules hash {_gameRules.ComputeHash():X16}, navmesh checksum {ViewedSimulation.NavMeshChecksum:X16}, snapshot {ViewedSimulation.SnapshotSize / 1024} KiB.", this);
        }

        private void StartLoopback()
        {
            RollbackSettings settings = new() { InputDelayTicks = _inputDelayTicks, MaxRollbackTicks = _maxRollbackTicks };
            _network = new LoopbackNetwork(seed: 1);
            ApplyNetworkConditions();

            // Both peers' recordings share a timestamp, so they're easy to pair up for tickwise compare.
            string sessionPath = TickwiseSupport.CreateRecordingPath("loopback");
            string stem = Path.Combine(Path.GetDirectoryName(sessionPath), Path.GetFileNameWithoutExtension(sessionPath));
            for (int team = 0; team < TeamExtensions.Count; team++)
            {
                Peer peer = CreatePeer(path: null);
                IInputTransport transport = team == (int)Team.Red ? _network.First : _network.Second;
                if (_record)
                {
                    MatchRecorder recorder = TickwiseSupport.TryStartRecording($"{stem}-{(Team)team}.rec".ToLowerInvariant(), peer.Simulation, this);
                    if (recorder != null)
                    {
                        peer.ConfirmedRecorder = new ConfirmedTickRecorder(recorder, peer.Simulation, settings.MaxRollbackTicks);
                    }
                }

                peer.Session = new RollbackSession(peer.Simulation, (Team)team, transport, settings, peer.ConfirmedRecorder);
                _peers[team] = peer;
            }
        }

        private Peer CreatePeer(string path)
        {
            INavigationWorldFactory navigation = _useBurstNavigation ? new BurstNavigationWorldFactory() : new ManagedNavigationWorldFactory();
            Peer peer = new() { Simulation = new GameSimulation(_gameRules, _levelData, navigation) };
            if (_record && path != null)
            {
                peer.Recorder = TickwiseSupport.TryStartRecording(path, peer.Simulation, this);
            }

            return peer;
        }

        private void Update()
        {
            if (_peers[0] == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // Latched until a tick consumes them, so a press between ticks is never lost.
                _peers[(int)Team.Red].PendingSpawnBig |= keyboard.spaceKey.wasPressedThisFrame;
                Peer blueOwner = _mode == LocalMatchMode.Loopback ? _peers[(int)Team.Blue] : _peers[0];
                blueOwner.PendingBlueSpawnBig |= keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            }

            if (_mode == LocalMatchMode.Loopback)
            {
                ApplyNetworkConditions();
            }

            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= _tickSeconds && steps < MaxStepsPerFrame)
            {
                RunTick();
                _view.Capture(ViewedSimulation);
                _accumulator -= _tickSeconds;
                steps++;
            }

            if (steps == MaxStepsPerFrame)
            {
                _accumulator = Mathf.Min(_accumulator, _tickSeconds);
            }

            _view.Render(_accumulator / _tickSeconds);
        }

        private void RunTick()
        {
            switch (_mode)
            {
                case LocalMatchMode.HotSeat:
                case LocalMatchMode.SyncTest:
                {
                    Peer peer = _peers[0];
                    GameInput red = ToInput(peer.PendingSpawnBig);
                    GameInput blue = ToInput(peer.PendingBlueSpawnBig);
                    peer.PendingSpawnBig = false;
                    peer.PendingBlueSpawnBig = false;
                    if (_syncTest != null)
                    {
                        _syncTest.Advance(red, blue);
                    }
                    else
                    {
                        peer.Simulation.Step(red, blue);
                    }

                    peer.Recorder?.RecordTick(red, blue);
                    break;
                }

                case LocalMatchMode.Loopback:
                {
                    _networkClock += _tickSeconds;
                    _network.Update(_networkClock);
                    Peer red = _peers[(int)Team.Red];
                    Peer blue = _peers[(int)Team.Blue];
                    if (red.Session.Advance(ToInput(red.PendingSpawnBig)) == AdvanceResult.Simulated)
                    {
                        red.PendingSpawnBig = false;
                    }

                    if (blue.Session.Advance(ToInput(blue.PendingBlueSpawnBig)) == AdvanceResult.Simulated)
                    {
                        blue.PendingBlueSpawnBig = false;
                    }

                    break;
                }
            }
        }

        private void OnDestroy()
        {
            _view?.Destroy();
            foreach (Peer peer in _peers)
            {
                peer?.Dispose(this);
            }
        }

        private void OnGUI()
        {
            if (_peers[0] == null)
            {
                return;
            }

            GameSimulation simulation = ViewedSimulation;
            ref readonly MatchState match = ref simulation.Match;
            ref readonly TeamState red = ref simulation.GetTeam(Team.Red);
            ref readonly TeamState blue = ref simulation.GetTeam(Team.Blue);
            string pause = match.RoundPauseTicks > 0 ? $"   {match.LastRoundResult}, next round in {match.RoundPauseTicks / (float)_gameRules.TickRate:0.0}s" : string.Empty;

            GUILayout.BeginArea(new Rect(10, 10, 640, _mode == LocalMatchMode.Loopback ? 330 : 200), GUI.skin.box);
            string viewing = _mode == LocalMatchMode.Loopback ? $"   viewing {_viewedPeer} peer" : string.Empty;
            GUILayout.Label($"{_mode}{viewing}   tick {match.Tick}   round {match.Round + 1}{pause}");
            GUILayout.Label($"RED   score {red.Score}   base {red.BaseHealth}   cubes {red.UnitCount}   big cooldown {red.BigCooldown.ToFloat() / _gameRules.TickRate:0.0}s   [Space]");
            GUILayout.Label($"BLUE  score {blue.Score}   base {blue.BaseHealth}   cubes {blue.UnitCount}   big cooldown {blue.BigCooldown.ToFloat() / _gameRules.TickRate:0.0}s   [Enter]");

            switch (_mode)
            {
                case LocalMatchMode.HotSeat:
                    GUILayout.Label($"light hash {simulation.ComputeLightHash():X16}");
                    break;
                case LocalMatchMode.SyncTest:
                    string result = _syncTest.FirstMismatchTick < 0 ? "no mismatch" : $"MISMATCH at tick {_syncTest.FirstMismatchTick}";
                    GUILayout.Label($"sync test: re-simulating {_syncTest.CheckDistance} ticks every tick ({_syncTest.ResimulatedTicks} so far): {result}");
                    break;
                case LocalMatchMode.Loopback:
                    DrawPeerStats(_peers[(int)Team.Red]);
                    DrawPeerStats(_peers[(int)Team.Blue]);
                    GUILayout.Label($"latency {_latencyMs:0} ms");
                    _latencyMs = GUILayout.HorizontalSlider(_latencyMs, 0f, 300f);
                    GUILayout.Label($"jitter {_jitterMs:0} ms");
                    _jitterMs = GUILayout.HorizontalSlider(_jitterMs, 0f, 150f);
                    GUILayout.Label($"packet loss {_packetLoss:P0}");
                    _packetLoss = GUILayout.HorizontalSlider(_packetLoss, 0f, 0.5f);
                    break;
            }

            DrawRecordingAndChaos();
            GUILayout.EndArea();
        }

        private static void DrawPeerStats(Peer peer)
        {
            RollbackSession session = peer.Session;
            RollbackStats stats = session.Stats;
            GUILayout.Label(
                $"{session.LocalTeam,-4} peer: tick {session.CurrentTick}  confirmed {session.ConfirmedTick}  rollbacks {stats.Rollbacks} (max {stats.MaxRollbackDepth} ticks)  " +
                $"stalls {stats.StalledTicks}  skips {stats.SkippedTicks}  advantage {session.FrameAdvantage:+0.0;-0.0}");
        }

        private void DrawRecordingAndChaos()
        {
            int recording = 0;
            foreach (Peer peer in _peers)
            {
                if (peer?.Recorder != null || peer?.ConfirmedRecorder != null)
                {
                    recording++;
                }
            }

            GUILayout.Label(recording > 0 ? $"recording {recording} Tickwise file(s) (Netcode Sample > Tickwise > Open Recordings Folder)" : "not recording");

            ChaosSettings chaos = ChaosTarget.Chaos;
            string target = _mode == LocalMatchMode.Loopback ? " on the red peer" : string.Empty;
            string label = chaos.Enabled ? $"CHAOS ON{target} from tick {chaos.FromTick} (click to stop)" : $"Chaos off (click to plant the bug{target} from the next tick)";
            if (GUILayout.Button(label))
            {
                ApplyChaos(!chaos.Enabled, ChaosTarget.Tick + 1);
            }
        }

        // In loopback the bug goes into one peer only: that's a desync between machines.
        private GameSimulation ChaosTarget => _peers[0].Simulation;

        private void ApplyChaos(bool enabled, long fromTick)
        {
            ChaosTarget.Chaos = new ChaosSettings { Enabled = enabled, FromTick = fromTick };
            if (enabled)
            {
                Debug.LogWarning($"Chaos: the planted determinism bug is active from tick {fromTick}. Tickwise will find it there.", this);
            }
        }

        private void ApplyNetworkConditions()
        {
            _network.Conditions = new NetworkConditions { LatencyMs = _latencyMs, JitterMs = _jitterMs, PacketLoss = _packetLoss };
        }

        private static GameInput ToInput(bool spawnBig)
        {
            return spawnBig ? GameInput.SpawnBig : GameInput.None;
        }

        private void FrameCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 red = _levelData.GetBasePosition(Team.Red).ToVector3();
            Vector3 blue = _levelData.GetBasePosition(Team.Blue).ToVector3();
            Vector3 center = (red + blue) * 0.5f;
            float span = Vector3.Distance(red, blue);
            camera.transform.position = center + new Vector3(0f, span * 0.9f, -span * 0.35f);
            camera.transform.LookAt(center);
        }

        private sealed class Peer
        {
            public GameSimulation Simulation;
            public RollbackSession Session;

            // Hot-seat and sync test record every tick; loopback peers record confirmed ticks.
            public MatchRecorder Recorder;
            public ConfirmedTickRecorder ConfirmedRecorder;

            // In hot-seat and sync test one peer holds both buttons; in loopback each peer holds its own.
            public bool PendingSpawnBig;
            public bool PendingBlueSpawnBig;

            public void Dispose(Object context)
            {
                // Finishing writes the file's index and trailer; without them it can't be read.
                if (Recorder != null)
                {
                    Debug.Log($"Tickwise: recorded {Recorder.TicksRecorded} ticks to {Recorder.Path}", context);
                    Recorder.Dispose();
                }

                if (ConfirmedRecorder != null)
                {
                    Debug.Log($"Tickwise: recorded {ConfirmedRecorder.Recorder.TicksRecorded} confirmed ticks to {ConfirmedRecorder.Recorder.Path}", context);
                    ConfirmedRecorder.Dispose();
                }

                Simulation?.Dispose();
            }
        }
    }
}
