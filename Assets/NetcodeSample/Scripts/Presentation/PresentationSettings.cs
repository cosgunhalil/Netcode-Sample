using NetcodeSample.Simulation;
using UnityEngine;

namespace NetcodeSample.Presentation
{
    /// <summary>
    /// Materials, colours and smoothing settings for the match view. Materials are assets (created by the scene
    /// setup commands) rather than runtime instances, so builds keep the shader variants they need.
    /// </summary>
    [CreateAssetMenu(menuName = "Netcode Sample/Presentation Settings", fileName = "Presentation")]
    public sealed class PresentationSettings : ScriptableObject
    {
        [Header("Materials")]
        [SerializeField]
        [Tooltip("Opaque lit material for cubes; tinted per cube through _BaseColor.")]
        private Material _unitMaterial;

        [SerializeField]
        [Tooltip("Transparent material for cubes fading in or out.")]
        private Material _fadeMaterial;

        [SerializeField]
        [Tooltip("Opaque lit material for base platforms.")]
        private Material _baseMaterial;

        [SerializeField]
        [Tooltip("Transparent unlit material for flashes and explosions.")]
        private Material _effectMaterial;

        [SerializeField]
        [Tooltip("Unlit material for health bars.")]
        private Material _barMaterial;

        [Header("Colours")]
        [SerializeField]
        private Color _redColor = new(0.92f, 0.22f, 0.2f);

        [SerializeField]
        private Color _blueColor = new(0.2f, 0.45f, 0.95f);

        [SerializeField]
        [Tooltip("Cubes blend towards this colour as they lose health.")]
        private Color _woundedTint = new(0.12f, 0.12f, 0.14f);

        [SerializeField]
        private Color _barBackground = new(0.08f, 0.08f, 0.1f);

        [Header("Rollback smoothing")]
        [SerializeField]
        [Range(0.005f, 0.3f)]
        [Tooltip("A correction's visual offset halves every this many seconds.")]
        private float _correctionHalfLife = 0.05f;

        [SerializeField]
        [Range(0.5f, 20f)]
        [Tooltip("Corrections larger than this (metres) snap instead of being smoothed.")]
        private float _snapDistance = 3f;

        [Header("Animation")]
        [SerializeField]
        [Range(0.01f, 1f)]
        private float _spawnPopSeconds = 0.15f;

        [SerializeField]
        [Range(0.01f, 1f)]
        [Tooltip("Cubes that only existed in a wrong prediction fade out (and cubes revealed by a correction fade in) over this long.")]
        private float _fadeSeconds = 0.25f;

        [SerializeField]
        [Range(0.05f, 2f)]
        private float _effectSeconds = 0.45f;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("How much a cube swells for a moment when it hits.")]
        private float _attackPulse = 0.18f;

        [SerializeField]
        [Tooltip("How quickly cubes turn towards where they're going, in degrees per second.")]
        private float _turnSpeed = 720f;

        public Material UnitMaterial => _unitMaterial;

        public Material FadeMaterial => _fadeMaterial;

        public Material BaseMaterial => _baseMaterial;

        public Material EffectMaterial => _effectMaterial;

        public Material BarMaterial => _barMaterial;

        public Color WoundedTint => _woundedTint;

        public Color BarBackground => _barBackground;

        public float CorrectionHalfLife => _correctionHalfLife;

        public float SnapDistance => _snapDistance;

        public float SpawnPopSeconds => _spawnPopSeconds;

        public float FadeSeconds => _fadeSeconds;

        public float EffectSeconds => _effectSeconds;

        public float AttackPulse => _attackPulse;

        public float TurnSpeed => _turnSpeed;

        public Color GetTeamColor(Team team) => team == Team.Red ? _redColor : _blueColor;

        public bool HasMaterials(out string missing)
        {
            missing = _unitMaterial == null ? nameof(UnitMaterial)
                : _fadeMaterial == null ? nameof(FadeMaterial)
                : _baseMaterial == null ? nameof(BaseMaterial)
                : _effectMaterial == null ? nameof(EffectMaterial)
                : _barMaterial == null ? nameof(BarMaterial)
                : null;
            return missing == null;
        }
    }
}
