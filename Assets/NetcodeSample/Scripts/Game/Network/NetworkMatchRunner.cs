using System.IO;
using DPF.Unity;
using FishNet.Managing;
using NetcodeSample.Determinism;
using NetcodeSample.Networking;
using NetcodeSample.Presentation;
using NetcodeSample.Rollback;
using NetcodeSample.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NetcodeSample.Game.Network
{
    /// <summary>
    /// A two-player peer-to-peer rollback match over FishNet. One peer hosts, the other joins by address; each
    /// simulates the whole game and they exchange only inputs. Space spawns this peer's big cube. Both peers record
    /// their confirmed ticks with Tickwise, named by session and team so the pair compares directly.
    /// </summary>
    public sealed class NetworkMatchRunner : MonoBehaviour
    {
        private const int MaxStepsPerFrame = 4;

        // FishNet sends queued messages on its own tick; faster than the simulation keeps that from adding latency.
        private const ushort TransportTickRate = 60;

        [SerializeField]
        private NetworkManager _networkManager;

        [SerializeField]
        private LevelDefinition _level;

        [SerializeField]
        private GameRulesAsset _rules;

        [SerializeField]
        private PresentationSettings _presentation;

        [Header("Connection")]
        [SerializeField]
        private string _address = "127.0.0.1";

        [SerializeField]
        private ushort _port = 7770;

        [SerializeField]
        [Tooltip("The host plays this team; the joiner plays the other.")]
        private Team _hostTeam = Team.Red;

        [SerializeField]
        [Tooltip("In the editor with ParrelSync: the original editor hosts and the clone joins, without the menu.")]
        private bool _autoStartWithParrelSync = true;

        [Header("Rollback (the host's settings apply to both peers)")]
        [SerializeField]
        [Range(0, 15)]
        private int _inputDelayTicks = 2;

        [SerializeField]
        [Range(1, 15)]
        private int _maxRollbackTicks = 8;

        [Header("FishNet latency simulator (this peer's outgoing traffic)")]
        [SerializeField]
        private bool _simulateLatency;

        [SerializeField]
        [Range(0, 500)]
        private int _latencyMs = 60;

        [SerializeField]
        [Range(0f, 0.5f)]
        private float _packetLoss;

        [SerializeField]
        [Range(0f, 0.5f)]
        private float _outOfOrder;

        [Header("Tickwise")]
        [SerializeField]
        private bool _record = true;

        private GameRules _gameRules;
        private LevelData _levelData;
        private GameSimulation _simulation;
        private FishNetPeer _peer;
        private RollbackSession _session;
        private ConfirmedTickRecorder _recorder;
        private MatchPresenter _presenter;
        private float _tickSeconds;
        private float _accumulator;
        private bool _pendingSpawnBig;
        private string _lastMessage = string.Empty;

        private void Start()
        {
            if (_networkManager == null || _level == null || _rules == null || _presentation == null)
            {
                Debug.LogError("NetworkMatchRunner needs a Network Manager, Level Definition, Game Rules and Presentation Settings. Run Netcode Sample > Set Up Network Match Scene.", this);
                enabled = false;
                return;
            }

            _gameRules = _rules.ToRules();
            _levelData = _level.CreateLevelData();
            _tickSeconds = 1f / _gameRules.TickRate;
            _networkManager.TimeManager.SetTickRate(TransportTickRate);
            CameraFraming.FrameBases(_levelData);
            PrepareMatch();

            if (_autoStartWithParrelSync && TryGetParrelSyncRole(out bool isClone))
            {
                if (isClone)
                {
                    Join();
                }
                else
                {
                    Host();
                }
            }
        }

        // A fresh simulation and peer, ready to host or join.
        private void PrepareMatch()
        {
            _simulation = new GameSimulation(_gameRules, _levelData, new BurstNavigationWorldFactory());
            _peer = new FishNetPeer(_networkManager, MatchRecorder.DescribeBuild(_simulation));
            _peer.MatchReady += OnMatchReady;
            _accumulator = 0f;
            _pendingSpawnBig = false;
        }

        private void Host()
        {
            ApplyLatencySimulation();
            _peer.Host(_port, _hostTeam, new RollbackSettings { InputDelayTicks = _inputDelayTicks, MaxRollbackTicks = _maxRollbackTicks });
        }

        private void Join()
        {
            ApplyLatencySimulation();
            _peer.Join(_address, _port);
        }

        private void OnMatchReady()
        {
            if (_record)
            {
                string path = Path.Combine(TickwiseSupport.RecordingsFolder, $"online-{_peer.SessionId}-{_peer.LocalTeam}.rec".ToLowerInvariant());
                MatchRecorder recorder = TickwiseSupport.TryStartRecording(path, _simulation, this);
                if (recorder != null)
                {
                    _recorder = new ConfirmedTickRecorder(recorder, _simulation, _peer.Settings.MaxRollbackTicks);
                }
            }

            _presenter = new MatchPresenter(_simulation, _presentation);
            _session = new RollbackSession(_simulation, _peer.LocalTeam, _peer, _peer.Settings, new CompositeTickObserver(_recorder, _presenter));
            Debug.Log($"Network match {_peer.SessionId}: playing {_peer.LocalTeam} as {(_peer.IsHost ? "host" : "joiner")}, input delay {_peer.Settings.InputDelayTicks}, max rollback {_peer.Settings.MaxRollbackTicks}.", this);
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            if (_peer.State != PeerState.Ready)
            {
                EndMatch(_peer.StatusMessage);
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // Latched until a tick consumes it, so a press between ticks (or during a stall) is never lost.
                _pendingSpawnBig |= keyboard.spaceKey.wasPressedThisFrame;
            }

            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= _tickSeconds && steps < MaxStepsPerFrame)
            {
                if (_session.Advance(_pendingSpawnBig ? GameInput.SpawnBig : GameInput.None) == AdvanceResult.Simulated)
                {
                    _pendingSpawnBig = false;
                }

                _presenter.Present();
                _accumulator -= _tickSeconds;
                steps++;
            }

            if (steps == MaxStepsPerFrame)
            {
                _accumulator = Mathf.Min(_accumulator, _tickSeconds);
            }

            _presenter.Render(_accumulator / _tickSeconds, Time.deltaTime);
        }

        // Finishes the recording and resets to the menu with a fresh simulation.
        private void EndMatch(string message)
        {
            _lastMessage = message;
            Debug.Log($"Network match ended: {message}", this);
            TearDown();
            PrepareMatch();
        }

        private void TearDown()
        {
            _presenter?.Dispose();
            _presenter = null;
            if (_recorder != null)
            {
                Debug.Log($"Tickwise: recorded {_recorder.Recorder.TicksRecorded} confirmed ticks to {_recorder.Recorder.Path}", this);
                _recorder.Dispose();
                _recorder = null;
            }

            _session = null;
            _peer?.Dispose();
            _peer = null;
            _simulation?.Dispose();
            _simulation = null;
        }

        private void OnDestroy()
        {
            TearDown();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 640, 330), GUI.skin.box);
            if (_session == null)
            {
                DrawMenu();
            }
            else
            {
                DrawMatch();
            }

            GUILayout.EndArea();
        }

        // A stand-in for the HannibalUI main menu of phase 8.
        private void DrawMenu()
        {
            GUILayout.Label("Peer-to-peer rollback match (FishNet)");
            if (_peer == null)
            {
                return;
            }

            if (_peer.State == PeerState.Idle || _peer.State == PeerState.Failed)
            {
                if (_peer.State == PeerState.Failed)
                {
                    GUILayout.Label(_peer.StatusMessage);
                    if (GUILayout.Button("Back"))
                    {
                        TearDown();
                        PrepareMatch();
                    }

                    return;
                }

                if (!string.IsNullOrEmpty(_lastMessage))
                {
                    GUILayout.Label($"Last match: {_lastMessage}");
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Port", GUILayout.Width(60));
                if (ushort.TryParse(GUILayout.TextField(_port.ToString(), GUILayout.Width(80)), out ushort port))
                {
                    _port = port;
                }

                GUILayout.Label("Host plays", GUILayout.Width(80));
                if (GUILayout.Button(_hostTeam.ToString(), GUILayout.Width(80)))
                {
                    _hostTeam = _hostTeam.Opponent();
                }

                if (GUILayout.Button("Host"))
                {
                    Host();
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Address", GUILayout.Width(60));
                _address = GUILayout.TextField(_address, GUILayout.Width(160));
                if (GUILayout.Button("Join"))
                {
                    Join();
                }

                GUILayout.EndHorizontal();
                DrawLatencyControls();
                return;
            }

            GUILayout.Label(_peer.StatusMessage);
            if (GUILayout.Button("Cancel"))
            {
                TearDown();
                PrepareMatch();
            }
        }

        private void DrawMatch()
        {
            ref readonly MatchState match = ref _simulation.Match;
            ref readonly TeamState red = ref _simulation.GetTeam(Team.Red);
            ref readonly TeamState blue = ref _simulation.GetTeam(Team.Blue);
            string pause = match.RoundPauseTicks > 0 ? $"   {match.LastRoundResult}, next round in {match.RoundPauseTicks / (float)_gameRules.TickRate:0.0}s" : string.Empty;
            string you = _peer.LocalTeam.ToString().ToUpperInvariant();

            GUILayout.Label($"{_peer.StatusMessage}   session {_peer.SessionId}   round {match.Round + 1}{pause}");
            GUILayout.Label($"RED   score {red.Score}   base {red.BaseHealth}   cubes {red.UnitCount}   big cooldown {red.BigCooldown.ToFloat() / _gameRules.TickRate:0.0}s");
            GUILayout.Label($"BLUE  score {blue.Score}   base {blue.BaseHealth}   cubes {blue.UnitCount}   big cooldown {blue.BigCooldown.ToFloat() / _gameRules.TickRate:0.0}s");
            GUILayout.Label($"You are {you}: Space spawns your big cube.");

            RollbackStats stats = _session.Stats;
            string rtt = _peer.RoundTripTimeMs >= 0 ? $"   rtt {_peer.RoundTripTimeMs} ms" : string.Empty;
            GUILayout.Label($"tick {_session.CurrentTick}   confirmed {_session.ConfirmedTick}   rollbacks {stats.Rollbacks} (max {stats.MaxRollbackDepth} ticks)   stalls {stats.StalledTicks}   skips {stats.SkippedTicks}   advantage {_session.FrameAdvantage:+0.0;-0.0}{rtt}");
            GUILayout.Label(_recorder != null ? $"recording {Path.GetFileName(_recorder.Recorder.Path)}" : "not recording");

            DrawLatencyControls();

            ChaosSettings chaos = _simulation.Chaos;
            string label = chaos.Enabled ? $"CHAOS ON on this peer from tick {chaos.FromTick} (click to stop)" : "Chaos off (click to plant the bug on this peer only)";
            if (GUILayout.Button(label))
            {
                _simulation.Chaos = new ChaosSettings { Enabled = !chaos.Enabled, FromTick = _simulation.Tick + 1 };
            }

            if (GUILayout.Button("Leave match"))
            {
                EndMatch("You left.");
            }
        }

        private void DrawLatencyControls()
        {
            bool changed = false;
            bool simulate = GUILayout.Toggle(_simulateLatency, " Simulate latency on this peer's outgoing traffic (FishNet)");
            changed |= simulate != _simulateLatency;
            _simulateLatency = simulate;
            if (_simulateLatency)
            {
                GUILayout.Label($"latency {_latencyMs} ms   packet loss {_packetLoss:P0}   out of order {_outOfOrder:P0}");
                int latency = Mathf.RoundToInt(GUILayout.HorizontalSlider(_latencyMs, 0f, 500f));
                float loss = GUILayout.HorizontalSlider(_packetLoss, 0f, 0.5f);
                float outOfOrder = GUILayout.HorizontalSlider(_outOfOrder, 0f, 0.5f);
                changed |= latency != _latencyMs || !Mathf.Approximately(loss, _packetLoss) || !Mathf.Approximately(outOfOrder, _outOfOrder);
                _latencyMs = latency;
                _packetLoss = loss;
                _outOfOrder = outOfOrder;
            }

            if (changed)
            {
                ApplyLatencySimulation();
            }
        }

        private void ApplyLatencySimulation()
        {
            _peer?.SetSimulatedConditions(_simulateLatency, _latencyMs, _packetLoss, _outOfOrder);
        }

        // True in the editor of a project that has ParrelSync clones; isClone is true in the clone. Detected from
        // ParrelSync's files (the ".clone" marker in a clone, "<project>_clone_N" folders beside the original),
        // because ParrelSync's API is editor-only and this assembly also builds into players.
        private static bool TryGetParrelSyncRole(out bool isClone)
        {
            isClone = false;
#if UNITY_EDITOR
            DirectoryInfo project = Directory.GetParent(Application.dataPath);
            if (project == null || project.Parent == null)
            {
                return false;
            }

            isClone = File.Exists(Path.Combine(project.FullName, ".clone"));
            return isClone || project.Parent.GetDirectories($"{project.Name}_clone_*").Length > 0;
#else
            return false;
#endif
        }
    }
}
