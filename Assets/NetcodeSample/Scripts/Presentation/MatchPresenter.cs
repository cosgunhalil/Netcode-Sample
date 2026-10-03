using System;
using System.Collections.Generic;
using DPF.Unity;
using NetcodeSample.Rollback;
using NetcodeSample.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NetcodeSample.Presentation
{
    /// <summary>
    /// Draws a match and hides rollback. Hook it up as a tick observer of the simulation it draws, call
    /// <see cref="Present"/> after every fixed tick and <see cref="Render"/> every frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cubes are drawn between their positions at the last two simulated ticks, so 30 Hz motion is smooth at any
    /// frame rate. Those two positions are captured as ticks are simulated, including during re-simulation, so
    /// after a rollback they're the corrected ones.
    /// </para>
    /// <para>
    /// When a correction moves a cube, the jump becomes a visual offset that fades away (exponentially, see
    /// <see cref="PresentationSettings.CorrectionHalfLife"/>): the cube glides to where it really is instead of
    /// teleporting. Cubes that only existed in a mispredicted timeline fade out; cubes a correction reveals fade in;
    /// cubes that really died or exploded play their effect.
    /// </para>
    /// </remarks>
    public sealed class MatchPresenter : ITickObserver, IDisposable
    {
        private const string ColorProperty = "_BaseColor";

        // Events older than this many ticks are forgotten; a cube's end is matched to its event within that window.
        private const long EventMemoryTicks = 90;

        private static readonly int s_colorId = Shader.PropertyToID(ColorProperty);

        private readonly GameSimulation _simulation;
        private readonly PresentationSettings _settings;
        private readonly TickFrame[] _frames;
        private readonly Dictionary<long, UnitView> _views = new();
        private readonly List<UnitView> _leaving = new();
        private readonly Stack<UnitView> _pool = new();
        private readonly Dictionary<long, Ending> _endings = new();
        private readonly Dictionary<long, long> _lastAttackTicks = new();
        private readonly List<long> _scratchIds = new();
        private readonly BaseView[] _bases = new BaseView[TeamExtensions.Count];
        private readonly List<Effect> _effects = new();
        private readonly Stack<Effect> _effectPool = new();
        private readonly MaterialPropertyBlock _block = new();
        private readonly Transform _root;
        private long _latestTick;

        public MatchPresenter(GameSimulation simulation, PresentationSettings settings)
        {
            if (!settings.HasMaterials(out string missing))
            {
                throw new ArgumentException($"Presentation settings '{settings.name}' have no {missing}. Run a Netcode Sample scene setup command.", nameof(settings));
            }

            _simulation = simulation;
            _settings = settings;
            _frames = new[] { new TickFrame(simulation.UnitCapacity), new TickFrame(simulation.UnitCapacity) };
            _root = new GameObject("Match View").transform;
            for (int team = 0; team < TeamExtensions.Count; team++)
            {
                _bases[team] = new BaseView(this, (Team)team);
            }
        }

        /// <summary>The simulation stepped <paramref name="tick"/> (first time or re-simulation): remember its unit positions and events.</summary>
        public void OnTickSimulated(long tick)
        {
            _frames[tick % 2].Capture(_simulation, tick);
            _latestTick = tick;

            foreach (GameEvent gameEvent in _simulation.Events)
            {
                switch (gameEvent.Type)
                {
                    case GameEventType.UnitDied:
                    case GameEventType.UnitExploded:
                        _endings[gameEvent.UnitId] = new Ending { Type = gameEvent.Type, Tick = tick, Team = gameEvent.Team };
                        break;
                    case GameEventType.UnitAttacked:
                        _lastAttackTicks[gameEvent.UnitId] = tick;
                        break;
                }
            }
        }

        public void OnTickConfirmed(long tick, GameInput red, GameInput blue)
        {
        }

        /// <summary>Brings the cubes up to date with the latest simulated tick; call after every fixed tick, rolled back or not.</summary>
        public void Present()
        {
            long tick = _latestTick;
            if (tick <= 0)
            {
                return;
            }

            TickFrame current = _frames[tick % 2];
            TickFrame previous = _frames[(tick - 1) % 2];
            bool hasPrevious = previous.Tick == tick - 1;

            foreach (UnitView view in _views.Values)
            {
                view.Seen = false;
            }

            for (int slot = 0; slot < current.Ids.Length; slot++)
            {
                if (!current.Alive[slot])
                {
                    continue;
                }

                long id = current.Ids[slot];
                Vector3 position = current.Positions[slot];
                Vector3 previousPosition = hasPrevious && previous.Alive[slot] && previous.Ids[slot] == id ? previous.Positions[slot] : position;
                if (!_views.TryGetValue(id, out UnitView view))
                {
                    view = Rent(current.Teams[slot], current.Kinds[slot]);
                    view.Appear(previousPosition, position, tick, revealed: SpawnTick(id) < tick - 1);
                    _views.Add(id, view);
                }
                else
                {
                    view.Advance(previousPosition, position, tick, _settings.SnapDistance);
                }

                view.Seen = true;
                view.HealthFraction = current.Health[slot] / (float)_simulation.Rules.GetStats(current.Kinds[slot]).Health;
                if (_lastAttackTicks.TryGetValue(id, out long attackTick) && attackTick > view.LastAttackTick)
                {
                    view.LastAttackTick = attackTick;
                    view.Pulse = 1f;
                }
            }

            RemoveUnseenViews(tick);
            Forget(tick - EventMemoryTicks);
        }

        /// <summary>Draws a frame; <paramref name="alpha"/> is how far (0-1) time has moved from the last tick towards the next.</summary>
        public void Render(float alpha, float deltaTime)
        {
            float decay = Mathf.Pow(0.5f, deltaTime / _settings.CorrectionHalfLife);
            foreach (UnitView view in _views.Values)
            {
                view.Render(alpha, deltaTime, decay);
            }

            for (int i = _leaving.Count - 1; i >= 0; i--)
            {
                if (!_leaving[i].RenderLeaving(deltaTime))
                {
                    Return(_leaving[i]);
                    _leaving.RemoveAt(i);
                }
            }

            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                if (!_effects[i].Render(deltaTime))
                {
                    _effects[i].GameObject.SetActive(false);
                    _effectPool.Push(_effects[i]);
                    _effects.RemoveAt(i);
                }
            }

            foreach (BaseView baseView in _bases)
            {
                baseView.Render(deltaTime);
            }
        }

        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
            }
        }

        private static long SpawnTick(long unitId) => unitId / 4;

        private void RemoveUnseenViews(long tick)
        {
            _scratchIds.Clear();
            foreach (KeyValuePair<long, UnitView> pair in _views)
            {
                if (!pair.Value.Seen)
                {
                    _scratchIds.Add(pair.Key);
                }
            }

            foreach (long id in _scratchIds)
            {
                UnitView view = _views[id];
                _views.Remove(id);
                if (_endings.TryGetValue(id, out Ending ending))
                {
                    bool exploded = ending.Type == GameEventType.UnitExploded;
                    view.Leave(dying: true);
                    SpawnEffect(view.Displayed, _settings.GetTeamColor(ending.Team), view.Size, view.Size * (exploded ? 6f : 2.5f), exploded ? 1.4f : 0.8f);
                    if (exploded)
                    {
                        _bases[(int)ending.Team.Opponent()].Hit();
                    }
                }
                else
                {
                    // It only existed in a mispredicted timeline.
                    view.Leave(dying: false);
                }

                _leaving.Add(view);
            }
        }

        private void Forget(long beforeTick)
        {
            _scratchIds.Clear();
            foreach (KeyValuePair<long, Ending> pair in _endings)
            {
                if (pair.Value.Tick < beforeTick)
                {
                    _scratchIds.Add(pair.Key);
                }
            }

            foreach (KeyValuePair<long, long> pair in _lastAttackTicks)
            {
                if (pair.Value < beforeTick)
                {
                    _scratchIds.Add(pair.Key);
                }
            }

            foreach (long id in _scratchIds)
            {
                _endings.Remove(id);
                _lastAttackTicks.Remove(id);
            }
        }

        private UnitView Rent(Team team, UnitKind kind)
        {
            UnitView view = _pool.Count > 0 ? _pool.Pop() : new UnitView(this);
            view.Reset(team, _simulation.Rules.GetStats(kind).Radius.ToFloat() * 2f);
            return view;
        }

        private void Return(UnitView view)
        {
            view.GameObject.SetActive(false);
            _pool.Push(view);
        }

        private void SpawnEffect(Vector3 position, Color color, float startSize, float endSize, float durationScale)
        {
            Effect effect = _effectPool.Count > 0 ? _effectPool.Pop() : new Effect(this);
            effect.Start(position, color, startSize, endSize, _settings.EffectSeconds * durationScale);
            _effects.Add(effect);
        }

        private GameObject CreatePrimitive(PrimitiveType type, string name, Material material)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            Object.Destroy(gameObject.GetComponent<Collider>());
            gameObject.transform.SetParent(_root, false);
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            return gameObject;
        }

        private void SetColor(Renderer renderer, Color color)
        {
            _block.SetColor(s_colorId, color);
            renderer.SetPropertyBlock(_block);
        }

        /// <summary>The units of one simulated tick, by slot.</summary>
        private sealed class TickFrame
        {
            public TickFrame(int capacity)
            {
                Ids = new long[capacity];
                Positions = new Vector3[capacity];
                Health = new int[capacity];
                Alive = new bool[capacity];
                Teams = new Team[capacity];
                Kinds = new UnitKind[capacity];
            }

            public long Tick { get; private set; } = -1;

            public long[] Ids { get; }

            public Vector3[] Positions { get; }

            public int[] Health { get; }

            public bool[] Alive { get; }

            public Team[] Teams { get; }

            public UnitKind[] Kinds { get; }

            public void Capture(GameSimulation simulation, long tick)
            {
                Tick = tick;
                for (int slot = 0; slot < Ids.Length; slot++)
                {
                    ref readonly Unit unit = ref simulation.GetUnit(slot);
                    Alive[slot] = unit.IsAlive;
                    if (!unit.IsAlive)
                    {
                        continue;
                    }

                    Ids[slot] = unit.Id;
                    Positions[slot] = unit.Position.ToVector3();
                    Health[slot] = unit.Health;
                    Teams[slot] = unit.Team;
                    Kinds[slot] = unit.Kind;
                }
            }
        }

        private struct Ending
        {
            public GameEventType Type;
            public long Tick;
            public Team Team;
        }

        private sealed class UnitView
        {
            private readonly MatchPresenter _presenter;
            private readonly Renderer _renderer;
            private Vector3 _previous;
            private Vector3 _current;
            private long _currentTick;
            private Vector3 _offset;
            private Color _teamColor;
            private float _age;
            private bool _fadingIn;
            private bool _dying;

            public UnitView(MatchPresenter presenter)
            {
                _presenter = presenter;
                GameObject = presenter.CreatePrimitive(PrimitiveType.Cube, "Cube", presenter._settings.UnitMaterial);
                _renderer = GameObject.GetComponent<Renderer>();
            }

            public GameObject GameObject { get; }

            public bool Seen { get; set; }

            public float HealthFraction { get; set; } = 1f;

            public long LastAttackTick { get; set; }

            public float Pulse { get; set; }

            public float Size { get; private set; }

            /// <summary>Where the cube was last drawn, correction offset included.</summary>
            public Vector3 Displayed { get; private set; }

            private PresentationSettings Settings => _presenter._settings;

            public void Reset(Team team, float size)
            {
                _teamColor = Settings.GetTeamColor(team);
                Size = size;
                _offset = Vector3.zero;
                _age = 0f;
                _dying = false;
                HealthFraction = 1f;
                LastAttackTick = 0;
                Pulse = 0f;
                GameObject.SetActive(true);
            }

            // A cube that a correction revealed (it spawned ticks ago in the corrected timeline) fades in;
            // a fresh spawn pops in.
            public void Appear(Vector3 previous, Vector3 current, long tick, bool revealed)
            {
                _previous = previous;
                _current = current;
                _currentTick = tick;
                _fadingIn = revealed;
                _renderer.sharedMaterial = revealed ? Settings.FadeMaterial : Settings.UnitMaterial;
                Vector3 heading = current - previous;
                GameObject.transform.rotation = heading.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(Flatten(heading)) : Quaternion.identity;
                Displayed = current;
            }

            // Moves to the newest two ticks. If the tick it already showed has changed (a rollback corrected it), the
            // difference goes into the offset, so the drawn position doesn't jump.
            public void Advance(Vector3 previous, Vector3 current, long tick, float snapDistance)
            {
                Vector3 correction = Vector3.zero;
                if (_currentTick == tick - 1)
                {
                    correction = previous - _current;
                }
                else if (_currentTick == tick)
                {
                    correction = current - _current;
                }

                _offset -= correction;
                if (_offset.sqrMagnitude > snapDistance * snapDistance)
                {
                    _offset = Vector3.zero;
                }

                _previous = previous;
                _current = current;
                _currentTick = tick;
            }

            public void Render(float alpha, float deltaTime, float decay)
            {
                _age += deltaTime;
                _offset *= decay;
                Vector3 position = Vector3.Lerp(_previous, _current, alpha) + _offset;
                Displayed = position;

                Transform transform = GameObject.transform;
                transform.position = position + (Vector3.up * (Size * 0.5f));
                Vector3 heading = Flatten(_current - _previous);
                if (heading.sqrMagnitude > 1e-6f)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading), Settings.TurnSpeed * deltaTime);
                }

                float scale = Size;
                if (!_fadingIn && _age < Settings.SpawnPopSeconds)
                {
                    scale *= EaseOutBack(_age / Settings.SpawnPopSeconds);
                }

                scale *= 1f + (Settings.AttackPulse * Pulse);
                Pulse = Mathf.Max(0f, Pulse - (deltaTime * 6f));
                transform.localScale = Vector3.one * scale;

                Color color = Color.Lerp(Settings.WoundedTint, _teamColor, 0.35f + (0.65f * Mathf.Clamp01(HealthFraction)));
                if (_fadingIn)
                {
                    color.a = Mathf.Clamp01(_age / Settings.FadeSeconds);
                    if (color.a >= 1f)
                    {
                        _fadingIn = false;
                        _renderer.sharedMaterial = Settings.UnitMaterial;
                    }
                }

                _presenter.SetColor(_renderer, color);
            }

            public void Leave(bool dying)
            {
                _dying = dying;
                _age = 0f;
                _renderer.sharedMaterial = Settings.FadeMaterial;
            }

            // Returns false when done. Dying cubes shrink quickly under their effect; mispredicted ones fade out.
            public bool RenderLeaving(float deltaTime)
            {
                _age += deltaTime;
                float duration = _dying ? Settings.SpawnPopSeconds : Settings.FadeSeconds;
                float t = Mathf.Clamp01(_age / duration);
                Color color = _teamColor;
                color.a = 1f - t;
                _presenter.SetColor(_renderer, color);
                if (_dying)
                {
                    GameObject.transform.localScale = Vector3.one * (Size * (1f - t));
                }

                return t < 1f;
            }

            private static Vector3 Flatten(Vector3 vector) => new(vector.x, 0f, vector.z);

            private static float EaseOutBack(float t)
            {
                t = Mathf.Clamp01(t) - 1f;
                const float Overshoot = 1.7f;
                return 1f + (t * t * (((Overshoot + 1f) * t) + Overshoot));
            }
        }

        private sealed class BaseView
        {
            private const float BarWidth = 3f;
            private const float BarHeight = 2.5f;

            private readonly MatchPresenter _presenter;
            private readonly Team _team;
            private readonly Transform _platform;
            private readonly Transform _fill;
            private readonly Renderer _platformRenderer;
            private readonly Vector3 _platformScale;
            private float _shownHealth = 1f;
            private float _hit;

            public BaseView(MatchPresenter presenter, Team team)
            {
                _presenter = presenter;
                _team = team;
                PresentationSettings settings = presenter._settings;
                Vector3 position = presenter._simulation.Level.GetBasePosition(team).ToVector3();
                float diameter = presenter._simulation.Rules.BaseRadius.ToFloat() * 2f;

                GameObject platform = presenter.CreatePrimitive(PrimitiveType.Cylinder, $"{team} Base", settings.BaseMaterial);
                _platform = platform.transform;
                _platformRenderer = platform.GetComponent<Renderer>();
                _platformScale = new Vector3(diameter, 0.08f, diameter);
                _platform.position = position + (Vector3.up * 0.08f);
                _platform.localScale = _platformScale;
                presenter.SetColor(_platformRenderer, settings.GetTeamColor(team));

                Vector3 barCentre = position + (Vector3.up * BarHeight);
                GameObject background = presenter.CreatePrimitive(PrimitiveType.Cube, $"{team} Health Background", settings.BarMaterial);
                background.transform.position = barCentre;
                background.transform.localScale = new Vector3(BarWidth + 0.1f, 0.3f, 0.12f);
                presenter.SetColor(background.GetComponent<Renderer>(), settings.BarBackground);

                GameObject fill = presenter.CreatePrimitive(PrimitiveType.Cube, $"{team} Health", settings.BarMaterial);
                _fill = fill.transform;
                _fill.position = barCentre - (Vector3.forward * 0.02f);
                presenter.SetColor(fill.GetComponent<Renderer>(), settings.GetTeamColor(team));
                SetFill(1f, barCentre);
            }

            public void Hit()
            {
                _hit = 1f;
            }

            public void Render(float deltaTime)
            {
                GameSimulation simulation = _presenter._simulation;
                float health = simulation.GetTeam(_team).BaseHealth / (float)simulation.Rules.BaseHealth;
                _shownHealth = Mathf.MoveTowards(_shownHealth, health, deltaTime * 2f);
                SetFill(_shownHealth, simulation.Level.GetBasePosition(_team).ToVector3() + (Vector3.up * BarHeight));

                _hit = Mathf.Max(0f, _hit - (deltaTime * 3f));
                _platform.localScale = new Vector3(_platformScale.x * (1f + (0.25f * _hit)), _platformScale.y, _platformScale.z * (1f + (0.25f * _hit)));
                Color color = Color.Lerp(_presenter._settings.GetTeamColor(_team), Color.white, _hit * 0.7f);
                _presenter.SetColor(_platformRenderer, color);
            }

            // The fill shrinks towards the bar's left end.
            private void SetFill(float fraction, Vector3 barCentre)
            {
                fraction = Mathf.Clamp01(fraction);
                _fill.localScale = new Vector3(Mathf.Max(0.0001f, BarWidth * fraction), 0.22f, 0.12f);
                _fill.position = barCentre + (Vector3.right * ((BarWidth * (fraction - 1f)) * 0.5f)) - (Vector3.forward * 0.02f);
            }
        }

        private sealed class Effect
        {
            private readonly MatchPresenter _presenter;
            private readonly Renderer _renderer;
            private Color _color;
            private float _startSize;
            private float _endSize;
            private float _duration;
            private float _age;

            public Effect(MatchPresenter presenter)
            {
                _presenter = presenter;
                GameObject = presenter.CreatePrimitive(PrimitiveType.Sphere, "Effect", presenter._settings.EffectMaterial);
                _renderer = GameObject.GetComponent<Renderer>();
            }

            public GameObject GameObject { get; }

            public void Start(Vector3 position, Color color, float startSize, float endSize, float duration)
            {
                GameObject.SetActive(true);
                GameObject.transform.position = position + (Vector3.up * (startSize * 0.5f));
                _color = color;
                _startSize = startSize;
                _endSize = endSize;
                _duration = Mathf.Max(0.01f, duration);
                _age = 0f;
            }

            // Returns false when done.
            public bool Render(float deltaTime)
            {
                _age += deltaTime;
                float t = Mathf.Clamp01(_age / _duration);
                float eased = 1f - ((1f - t) * (1f - t));
                GameObject.transform.localScale = Vector3.one * Mathf.Lerp(_startSize, _endSize, eased);
                Color color = Color.Lerp(Color.white, _color, t * 0.6f);
                color.a = 0.8f * (1f - t);
                _presenter.SetColor(_renderer, color);
                return t < 1f;
            }
        }
    }
}
