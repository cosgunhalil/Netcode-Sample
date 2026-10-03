using System;
using HannibalUI.Runtime.Base;
using UnityEngine;
using UnityEngine.UI;

namespace NetcodeSample.UI
{
    /// <summary>Shown while hosting (waiting for the other player) or joining.</summary>
    public sealed class ConnectingScreen : VP_Canvas
    {
        [SerializeField]
        private Text _statusText;

        [SerializeField]
        private Button _cancelButton;

        public event Action CancelRequested;

        public override ScreenId ScreenType => MatchScreens.Connecting;

        public override void Setup()
        {
        }

        public void SetStatus(string status)
        {
            _statusText.text = status;
        }

        protected override void RegisterEvents()
        {
            _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        protected override void UnRegisterEvents()
        {
            _cancelButton.onClick.RemoveListener(OnCancelClicked);
        }

        private void OnCancelClicked()
        {
            CancelRequested?.Invoke();
        }
    }
}
