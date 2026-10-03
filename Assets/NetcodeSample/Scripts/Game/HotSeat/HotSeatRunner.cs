using DPF.Unity;
using NetcodeSample.Determinism;
using NetcodeSample.Simulation;
using NetcodeSample.Simulation.Navigation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NetcodeSample.Game.HotSeat
{
    /// <summary>
    /// Runs the simulation locally with both players on one keyboard: Space spawns a red big cube, Enter a blue one.
    /// No networking or rollback; a fixed-tick loop around <see cref="GameSimulation"/> for developing the game rules.
    /// </summary>
    public sealed class HotSeatRunner : MonoBehaviour
    {
        // At most this many ticks per frame, so a long frame can't make the game spiral.
        private const int MaxStepsPerFrame = 4;

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
        [Tooltip("Records every tick to persistentDataPath/tickwise/hotseat-<time>.rec.")]
        private bool _record = true;

        [SerializeField]
        [Tooltip("Plants a determinism bug from Chaos From Tick on: spawn offsets taken from the wall clock. Also toggled from the on-screen readout.")]
        private bool _chaos;

        [SerializeField]
        [Min(1)]
        private long _chaosFromTick = 300;

        private GameSimulation _simulation;
        private MatchRecorder _recorder;
        private PlaceholderView _view;
        private float _tickSeconds;
        private float _accumulator;
        private bool _redSpawnBig;
        private bool _blueSpawnBig;

        public GameSimulation Simulation => _simulation;

        private void Start()
        {
            if (_level == null || _rules == null)
            {
                Debug.LogError("HotSeatRunner needs a Level Definition and Game Rules. Run Netcode Sample > Set Up Hot-Seat Scene.", this);
                enabled = false;
                return;
            }

            GameRules rules = _rules.ToRules();
            INavigationWorldFactory navigation = _useBurstNavigation ? new BurstNavigationWorldFactory() : new ManagedNavigationWorldFactory();
            _simulation = new GameSimulation(rules, _level.CreateLevelData(), navigation);
            _tickSeconds = 1f / rules.TickRate;
            _view = new PlaceholderView(_simulation);
            ApplyChaos(_chaos, _chaosFromTick);
            if (_record)
            {
                _recorder = TickwiseSupport.TryStartRecording(TickwiseSupport.CreateRecordingPath("hotseat"), _simulation, this);
            }

            if (_frameCameraOnStart)
            {
                FrameCamera();
            }

            Debug.Log($"Hot-seat started: rules hash {rules.ComputeHash():X16}, navmesh checksum {_simulation.NavMeshChecksum:X16}, snapshot {_simulation.SnapshotSize} bytes.", this);
        }

        private void Update()
        {
            if (_simulation == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // Latched until the next tick consumes them, so a press between ticks is never lost.
                _redSpawnBig |= keyboard.spaceKey.wasPressedThisFrame;
                _blueSpawnBig |= keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            }

            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= _tickSeconds && steps < MaxStepsPerFrame)
            {
                GameInput red = _redSpawnBig ? GameInput.SpawnBig : GameInput.None;
                GameInput blue = _blueSpawnBig ? GameInput.SpawnBig : GameInput.None;
                _simulation.Step(red, blue);
                _recorder?.RecordTick(red, blue);
                _redSpawnBig = false;
                _blueSpawnBig = false;
                _view.Capture(_simulation);
                _accumulator -= _tickSeconds;
                steps++;
            }

            if (steps == MaxStepsPerFrame)
            {
                _accumulator = Mathf.Min(_accumulator, _tickSeconds);
            }

            _view.Render(_accumulator / _tickSeconds);
        }

        private void OnDestroy()
        {
            _view?.Destroy();

            // Finishing writes the file's index and trailer; without them it can't be read.
            if (_recorder != null)
            {
                Debug.Log($"Tickwise: recorded {_recorder.TicksRecorded} ticks to {_recorder.Path}", this);
                _recorder.Dispose();
                _recorder = null;
            }

            _simulation?.Dispose();
            _simulation = null;
        }

        private void OnGUI()
        {
            if (_simulation == null)
            {
                return;
            }

            ref readonly MatchState match = ref _simulation.Match;
            ref readonly TeamState red = ref _simulation.GetTeam(Team.Red);
            ref readonly TeamState blue = ref _simulation.GetTeam(Team.Blue);
            string pause = match.RoundPauseTicks > 0 ? $"   {match.LastRoundResult}, next round in {match.RoundPauseTicks / (float)_simulation.Rules.TickRate:0.0}s" : string.Empty;

            GUILayout.BeginArea(new Rect(10, 10, 560, 190), GUI.skin.box);
            GUILayout.Label($"Hot-seat   tick {match.Tick}   round {match.Round + 1}{pause}");
            GUILayout.Label($"RED   score {red.Score}   base {red.BaseHealth}   cubes {red.UnitCount}   big cooldown {red.BigCooldown.ToFloat() / _simulation.Rules.TickRate:0.0}s   [Space]");
            GUILayout.Label($"BLUE  score {blue.Score}   base {blue.BaseHealth}   cubes {blue.UnitCount}   big cooldown {blue.BigCooldown.ToFloat() / _simulation.Rules.TickRate:0.0}s   [Enter]");
            GUILayout.Label($"light hash {_simulation.ComputeLightHash():X16}");
            GUILayout.Label(_recorder != null ? $"recording {System.IO.Path.GetFileName(_recorder.Path)} ({_recorder.TicksRecorded} ticks)" : "not recording");
            ChaosSettings chaos = _simulation.Chaos;
            string chaosLabel = chaos.Enabled ? $"CHAOS ON from tick {chaos.FromTick} (click to stop)" : "Chaos off (click to plant the bug from the next tick)";
            if (GUILayout.Button(chaosLabel))
            {
                ApplyChaos(!chaos.Enabled, _simulation.Tick + 1);
            }
            GUILayout.EndArea();
        }

        private void ApplyChaos(bool enabled, long fromTick)
        {
            _simulation.Chaos = new ChaosSettings { Enabled = enabled, FromTick = fromTick };
            if (enabled)
            {
                Debug.LogWarning($"Chaos: the planted determinism bug is active from tick {fromTick}. A self-check of this recording will fail there.", this);
                // Markers attach to a recorded tick.
                if (_recorder != null && _recorder.TicksRecorded > 0)
                {
                    _recorder.RecordMarker($"chaos-from:{fromTick}");
                }
            }
        }

        private void FrameCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 red = _simulation.Level.GetBasePosition(Team.Red).ToVector3();
            Vector3 blue = _simulation.Level.GetBasePosition(Team.Blue).ToVector3();
            Vector3 center = (red + blue) * 0.5f;
            float span = Vector3.Distance(red, blue);
            camera.transform.position = center + new Vector3(0f, span * 0.9f, -span * 0.35f);
            camera.transform.LookAt(center);
        }
    }
}
