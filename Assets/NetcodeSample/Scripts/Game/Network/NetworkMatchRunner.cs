using System.IO;
using DPF.Unity;
using FishNet.Managing;
using NetcodeSample.Determinism;
using NetcodeSample.Networking;
using NetcodeSample.Presentation;
using NetcodeSample.Rollback;
using NetcodeSample.Simulation;
using NetcodeSample.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NetcodeSample.Game.Network
{
    /// <summary>
    /// A two-player peer-to-peer rollback match over FishNet. One peer hosts, the other joins by address; each
    /// simulates the whole game and they exchange only inputs. Space (or the HUD button) spawns this peer's big
    /// cube; F1 shows the netcode overlay. Both peers record their confirmed ticks with Tickwise, named by session
    /// and team so the pair compares directly.
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

        [SerializeField]
        private MatchUI _ui;

        [Header("Connection (defaults shown in the main menu)")]
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

        [Header("FishNet latency simulator (this peer's outgoing traffic; also on the F1 overlay)")]
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
        private bool _autoStartPending;

        private void Start()
        {
            if (_networkManager == null || _level == null || _rules == null || _presentation == null || _ui == null)
            {
                Debug.LogError("NetworkMatchRunner needs a Network Manager, Level Definition, Game Rules, Presentation Settings and Match UI. Run Netcode Sample > Set Up Network Match Scene.", this);
                enabled = false;
                return;
            }

            _gameRules = _rules.ToRules();
            _levelData = _level.CreateLevelData();
            _tickSeconds = 1f / _gameRules.TickRate;
            _networkManager.TimeManager.SetTickRate(TransportTickRate);
            CameraFraming.FrameBases(_levelData);

            _ui.MainMenu.SetDefaults(_address, _port, _hostTeam);
            _ui.MainMenu.HostRequested += OnHostRequested;
            _ui.MainMenu.JoinRequested += OnJoinRequested;
            _ui.Connecting.CancelRequested += OnCancelRequested;
            _ui.Hud.SpawnBigRequested += OnSpawnBigRequested;
            _ui.Hud.LeaveRequested += OnLeaveRequested;
            _ui.DebugOverlay.ConditionsChanged += OnConditionsChanged;
            _ui.DebugOverlay.ChaosToggled += OnChaosToggled;
            _ui.DebugOverlay.SetConditions(new SimulatedConditions { Enabled = _simulateLatency, LatencyMs = _latencyMs, PacketLoss = _packetLoss, OutOfOrder = _outOfOrder });
            _ui.DebugOverlay.SetVisible(false);

            PrepareMatch();

            // Started from the first Update, after the UI director has shown its start screen.
            _autoStartPending = _autoStartWithParrelSync && TryGetParrelSyncRole(out _);
        }

        // A fresh simulation and peer, ready to host or join.
        private void PrepareMatch()
        {
            _simulation = new GameSimulation(_gameRules, _levelData, new BurstNavigationWorldFactory());
            _peer = new FishNetPeer(_networkManager, MatchRecorder.DescribeBuild(_simulation));
            _peer.MatchReady += OnMatchReady;
            _accumulator = 0f;
            _pendingSpawnBig = false;
            _ui.DebugOverlay.SetChaos(false, 0);
        }

        private void OnHostRequested(ushort port, Team hostTeam)
        {
            _port = port;
            _hostTeam = hostTeam;
            ApplyLatencySimulation();
            _peer.Host(_port, _hostTeam, new RollbackSettings { InputDelayTicks = _inputDelayTicks, MaxRollbackTicks = _maxRollbackTicks });
            _ui.ShowConnecting(_peer.StatusMessage);
        }

        private void OnJoinRequested(string address, ushort port)
        {
            _address = address;
            _port = port;
            ApplyLatencySimulation();
            _peer.Join(_address, _port);
            _ui.ShowConnecting(_peer.StatusMessage);
        }

        private void OnCancelRequested()
        {
            ResetToMenu("Cancelled.");
        }

        private void OnLeaveRequested()
        {
            ResetToMenu("You left the match.");
        }

        private void OnSpawnBigRequested()
        {
            _pendingSpawnBig = true;
        }

        private void OnChaosToggled()
        {
            if (_session == null)
            {
                return;
            }

            ChaosSettings chaos = _simulation.Chaos;
            _simulation.Chaos = new ChaosSettings { Enabled = !chaos.Enabled, FromTick = _simulation.Tick + 1 };
            _ui.DebugOverlay.SetChaos(_simulation.Chaos.Enabled, _simulation.Chaos.FromTick);
        }

        private void OnConditionsChanged(SimulatedConditions conditions)
        {
            _simulateLatency = conditions.Enabled;
            _latencyMs = conditions.LatencyMs;
            _packetLoss = conditions.PacketLoss;
            _outOfOrder = conditions.OutOfOrder;
            ApplyLatencySimulation();
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
            _ui.ShowHud();
            Debug.Log($"Network match {_peer.SessionId}: playing {_peer.LocalTeam} as {(_peer.IsHost ? "host" : "joiner")}, input delay {_peer.Settings.InputDelayTicks}, max rollback {_peer.Settings.MaxRollbackTicks}.", this);
        }

        private void Update()
        {
            if (_peer == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                _ui.DebugOverlay.SetVisible(!_ui.DebugOverlay.IsVisible);
            }

            if (_autoStartPending)
            {
                _autoStartPending = false;
                TryGetParrelSyncRole(out bool isClone);
                if (isClone)
                {
                    OnJoinRequested(_address, _port);
                }
                else
                {
                    OnHostRequested(_port, _hostTeam);
                }
            }

            if (_session == null)
            {
                UpdateConnecting();
                return;
            }

            if (_peer.State != PeerState.Ready)
            {
                ResetToMenu(_peer.StatusMessage);
                return;
            }

            // Latched until a tick consumes it, so a press between ticks (or during a stall) is never lost.
            _pendingSpawnBig |= keyboard != null && keyboard.spaceKey.wasPressedThisFrame;

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
            RefreshMatchUI();
        }

        private void UpdateConnecting()
        {
            switch (_peer.State)
            {
                case PeerState.Connecting:
                case PeerState.WaitingForPeer:
                    _ui.Connecting.SetStatus(_peer.StatusMessage);
                    break;
                case PeerState.Failed:
                case PeerState.Disconnected:
                    ResetToMenu(_peer.StatusMessage);
                    break;
            }

            _ui.DebugOverlay.SetStats($"Not in a match. {_peer.StatusMessage}\n[F1] hide");
        }

        private void RefreshMatchUI()
        {
            ref readonly MatchState match = ref _simulation.Match;
            ref readonly TeamState red = ref _simulation.GetTeam(Team.Red);
            ref readonly TeamState blue = ref _simulation.GetTeam(Team.Blue);
            ref readonly TeamState local = ref _simulation.GetTeam(_peer.LocalTeam);
            float bigCooldownSeconds = local.BigCooldown.ToFloat() / _gameRules.TickRate;
            float bigCooldownTotal = _gameRules.BigCooldown.ToFloat();

            _ui.Hud.Refresh(new HudState
            {
                LocalTeam = _peer.LocalTeam,
                Round = match.Round,
                RoundSecondsLeft = _gameRules.RoundTimeLimitTicks > 0 ? Mathf.Max(0, _gameRules.RoundTimeLimitTicks - match.RoundTicks) / (float)_gameRules.TickRate : -1f,
                RedScore = red.Score,
                BlueScore = blue.Score,
                RedBaseHealth = red.BaseHealth / (float)_gameRules.BaseHealth,
                BlueBaseHealth = blue.BaseHealth / (float)_gameRules.BaseHealth,
                BigCooldown = bigCooldownTotal > 0f ? bigCooldownSeconds / bigCooldownTotal : 0f,
                BigCooldownSeconds = bigCooldownSeconds,
            });

            if (match.RoundPauseTicks > 0)
            {
                _ui.ShowRoundEnd(match.LastRoundResult, _peer.LocalTeam, red.Score, blue.Score, match.RoundPauseTicks / (float)_gameRules.TickRate);
            }
            else if (_ui.IsRoundEndShown)
            {
                _ui.HideRoundEnd();
            }

            RollbackStats stats = _session.Stats;
            string rtt = _peer.RoundTripTimeMs >= 0 ? $"{_peer.RoundTripTimeMs} ms" : "n/a (host)";
            string recording = _recorder != null ? Path.GetFileName(_recorder.Recorder.Path) : "off";
            _ui.DebugOverlay.SetStats(
                $"session {_peer.SessionId}   {_peer.LocalTeam} {(_peer.IsHost ? "host" : "joiner")}   [F1] hide\n" +
                $"tick {_session.CurrentTick}   confirmed {_session.ConfirmedTick}   remote inputs to {_session.RemoteInputTick}\n" +
                $"rollbacks {stats.Rollbacks}   max depth {stats.MaxRollbackDepth}   re-simulated {stats.ResimulatedTicks}\n" +
                $"stalls {stats.StalledTicks}   skips {stats.SkippedTicks}   advantage {_session.FrameAdvantage:+0.0;-0.0}   rtt {rtt}\n" +
                $"input delay {_session.Settings.InputDelayTicks}   max rollback {_session.Settings.MaxRollbackTicks}   recording {recording}");
        }

        // Finishes the recording and goes back to the main menu with a fresh simulation.
        private void ResetToMenu(string message)
        {
            if (_session != null)
            {
                Debug.Log($"Network match ended: {message}", this);
            }

            TearDown();
            PrepareMatch();
            _ui.ShowMainMenu(message);
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
            if (_peer != null)
            {
                _peer.MatchReady -= OnMatchReady;
                _peer.Dispose();
                _peer = null;
            }

            _simulation?.Dispose();
            _simulation = null;
        }

        private void OnDestroy()
        {
            if (_ui != null)
            {
                _ui.MainMenu.HostRequested -= OnHostRequested;
                _ui.MainMenu.JoinRequested -= OnJoinRequested;
                _ui.Connecting.CancelRequested -= OnCancelRequested;
                _ui.Hud.SpawnBigRequested -= OnSpawnBigRequested;
                _ui.Hud.LeaveRequested -= OnLeaveRequested;
                _ui.DebugOverlay.ConditionsChanged -= OnConditionsChanged;
                _ui.DebugOverlay.ChaosToggled -= OnChaosToggled;
            }

            TearDown();
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
