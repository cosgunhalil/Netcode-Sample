using DPF.Unity;
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

        private GameSimulation _simulation;
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
                _simulation.Step(_redSpawnBig ? GameInput.SpawnBig : GameInput.None, _blueSpawnBig ? GameInput.SpawnBig : GameInput.None);
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

            GUILayout.BeginArea(new Rect(10, 10, 520, 140), GUI.skin.box);
            GUILayout.Label($"Hot-seat   tick {match.Tick}   round {match.Round + 1}{pause}");
            GUILayout.Label($"RED   score {red.Score}   base {red.BaseHealth}   cubes {red.UnitCount}   big cooldown {red.BigCooldown.ToFloat() / _simulation.Rules.TickRate:0.0}s   [Space]");
            GUILayout.Label($"BLUE  score {blue.Score}   base {blue.BaseHealth}   cubes {blue.UnitCount}   big cooldown {blue.BigCooldown.ToFloat() / _simulation.Rules.TickRate:0.0}s   [Enter]");
            GUILayout.Label($"light hash {_simulation.ComputeLightHash():X16}");
            GUILayout.EndArea();
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
