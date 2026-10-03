using System;
using UnityEngine;
using UnityEngine.UI;

namespace NetcodeSample.UI
{
    /// <summary>The network latency simulator's settings on this peer.</summary>
    public struct SimulatedConditions
    {
        public bool Enabled;
        public int LatencyMs;
        public float PacketLoss;
        public float OutOfOrder;
    }

    /// <summary>
    /// Netcode diagnostics on their own canvas, outside the HannibalUI director (which shows one screen at a time):
    /// rollback and network stats, FishNet's latency simulator, and the chaos toggle.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField]
        private Canvas _canvas;

        [SerializeField]
        private Text _statsText;

        [SerializeField]
        private Toggle _latencyToggle;

        [SerializeField]
        private Slider _latencySlider;

        [SerializeField]
        private Slider _lossSlider;

        [SerializeField]
        private Slider _outOfOrderSlider;

        [SerializeField]
        private Text _conditionsText;

        [SerializeField]
        private Button _chaosButton;

        [SerializeField]
        private Text _chaosLabel;

        private bool _updating;

        public event Action<SimulatedConditions> ConditionsChanged;

        public event Action ChaosToggled;

        public bool IsVisible => _canvas.enabled;

        public SimulatedConditions Conditions => new()
        {
            Enabled = _latencyToggle.isOn,
            LatencyMs = Mathf.RoundToInt(_latencySlider.value),
            PacketLoss = _lossSlider.value,
            OutOfOrder = _outOfOrderSlider.value,
        };

        public void SetVisible(bool visible)
        {
            _canvas.enabled = visible;
        }

        public void SetStats(string stats)
        {
            if (_canvas.enabled)
            {
                _statsText.text = stats;
            }
        }

        public void SetChaos(bool enabled, long fromTick)
        {
            _chaosLabel.text = enabled ? $"CHAOS ON from tick {fromTick} (this peer only)" : "Plant chaos on this peer";
            _chaosLabel.color = enabled ? UIColors.Red : UIColors.Text;
        }

        /// <summary>Shows values without raising <see cref="ConditionsChanged"/>.</summary>
        public void SetConditions(SimulatedConditions conditions)
        {
            _updating = true;
            _latencyToggle.isOn = conditions.Enabled;
            _latencySlider.value = conditions.LatencyMs;
            _lossSlider.value = conditions.PacketLoss;
            _outOfOrderSlider.value = conditions.OutOfOrder;
            _updating = false;
            RefreshConditionsText();
        }

        private void Awake()
        {
            _latencySlider.minValue = 0f;
            _latencySlider.maxValue = 500f;
            _latencySlider.wholeNumbers = true;
            _lossSlider.minValue = 0f;
            _lossSlider.maxValue = 0.5f;
            _outOfOrderSlider.minValue = 0f;
            _outOfOrderSlider.maxValue = 0.5f;
            SetChaos(false, 0);
        }

        private void OnEnable()
        {
            _latencyToggle.onValueChanged.AddListener(OnToggleChanged);
            _latencySlider.onValueChanged.AddListener(OnSliderChanged);
            _lossSlider.onValueChanged.AddListener(OnSliderChanged);
            _outOfOrderSlider.onValueChanged.AddListener(OnSliderChanged);
            _chaosButton.onClick.AddListener(OnChaosClicked);
        }

        private void OnDisable()
        {
            _latencyToggle.onValueChanged.RemoveListener(OnToggleChanged);
            _latencySlider.onValueChanged.RemoveListener(OnSliderChanged);
            _lossSlider.onValueChanged.RemoveListener(OnSliderChanged);
            _outOfOrderSlider.onValueChanged.RemoveListener(OnSliderChanged);
            _chaosButton.onClick.RemoveListener(OnChaosClicked);
        }

        private void OnToggleChanged(bool value) => OnConditionsChanged();

        private void OnSliderChanged(float value) => OnConditionsChanged();

        private void OnConditionsChanged()
        {
            RefreshConditionsText();
            if (!_updating)
            {
                ConditionsChanged?.Invoke(Conditions);
            }
        }

        private void RefreshConditionsText()
        {
            SimulatedConditions conditions = Conditions;
            _conditionsText.text = conditions.Enabled
                ? $"latency {conditions.LatencyMs} ms   loss {conditions.PacketLoss:P0}   out of order {conditions.OutOfOrder:P0}"
                : "latency simulator off";
        }

        private void OnChaosClicked()
        {
            ChaosToggled?.Invoke();
        }
    }
}
