using System;
using HannibalUI.Runtime.Base;
using NetcodeSample.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace NetcodeSample.UI
{
    /// <summary>What the HUD shows; filled by the game every frame.</summary>
    public struct HudState
    {
        public Team LocalTeam;
        public int Round;

        /// <summary>Seconds left before the round's time limit; negative when there's no limit.</summary>
        public float RoundSecondsLeft;
        public int RedScore;
        public int BlueScore;

        /// <summary>Base health, 0 to 1.</summary>
        public float RedBaseHealth;
        public float BlueBaseHealth;

        /// <summary>The local big cube's remaining cooldown, 0 (ready) to 1 (just used).</summary>
        public float BigCooldown;
        public float BigCooldownSeconds;
    }

    /// <summary>In-match HUD: scores, base health, the big-cube button with its cooldown, and leaving.</summary>
    public sealed class HudScreen : VP_Canvas
    {
        [SerializeField]
        private Text _scoreText;

        [SerializeField]
        private Text _roundText;

        [SerializeField]
        private Text _youText;

        [SerializeField]
        private Image _redHealthFill;

        [SerializeField]
        private Image _blueHealthFill;

        [SerializeField]
        private Button _bigButton;

        [SerializeField]
        private Image _bigCooldownFill;

        [SerializeField]
        private Text _bigLabel;

        [SerializeField]
        private Button _leaveButton;

        public event Action SpawnBigRequested;

        public event Action LeaveRequested;

        public override ScreenId ScreenType => MatchScreens.Hud;

        public override void Setup()
        {
        }

        public void Refresh(in HudState state)
        {
            _scoreText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UIColors.Red)}>{state.RedScore}</color>  :  <color=#{ColorUtility.ToHtmlStringRGB(UIColors.Blue)}>{state.BlueScore}</color>";
            string clock = state.RoundSecondsLeft >= 0f ? $"   {(int)state.RoundSecondsLeft / 60}:{(int)state.RoundSecondsLeft % 60:00}" : string.Empty;
            _roundText.text = $"Round {state.Round + 1}{clock}";
            _youText.text = $"You are {state.LocalTeam.ToString().ToUpperInvariant()}";
            _youText.color = state.LocalTeam == Team.Red ? UIColors.Red : UIColors.Blue;
            _redHealthFill.fillAmount = Mathf.Clamp01(state.RedBaseHealth);
            _blueHealthFill.fillAmount = Mathf.Clamp01(state.BlueBaseHealth);
            _bigCooldownFill.fillAmount = Mathf.Clamp01(state.BigCooldown);
            _bigLabel.text = state.BigCooldown <= 0f ? "BIG CUBE  [Space]" : $"BIG CUBE  {state.BigCooldownSeconds:0.0}s";
        }

        protected override void RegisterEvents()
        {
            _bigButton.onClick.AddListener(OnBigClicked);
            _leaveButton.onClick.AddListener(OnLeaveClicked);
        }

        protected override void UnRegisterEvents()
        {
            _bigButton.onClick.RemoveListener(OnBigClicked);
            _leaveButton.onClick.RemoveListener(OnLeaveClicked);
        }

        private void OnBigClicked()
        {
            SpawnBigRequested?.Invoke();
        }

        private void OnLeaveClicked()
        {
            LeaveRequested?.Invoke();
        }
    }
}
