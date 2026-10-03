using System;
using HannibalUI.Runtime.Base;
using NetcodeSample.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace NetcodeSample.UI
{
    /// <summary>Host (port, the host's team) or join (address, port).</summary>
    public sealed class MainMenuScreen : VP_Canvas
    {
        [SerializeField]
        private InputField _addressField;

        [SerializeField]
        private InputField _portField;

        [SerializeField]
        private Button _teamButton;

        [SerializeField]
        private Text _teamLabel;

        [SerializeField]
        private Button _hostButton;

        [SerializeField]
        private Button _joinButton;

        [SerializeField]
        private Text _messageText;

        private Team _hostTeam = Team.Red;

        /// <summary>Port and the team the host plays.</summary>
        public event Action<ushort, Team> HostRequested;

        /// <summary>Address and port.</summary>
        public event Action<string, ushort> JoinRequested;

        public override ScreenId ScreenType => MatchScreens.MainMenu;

        public override void Setup()
        {
        }

        public void SetDefaults(string address, ushort port, Team hostTeam)
        {
            _addressField.text = address;
            _portField.text = port.ToString();
            _hostTeam = hostTeam;
            RefreshTeam();
        }

        public void SetMessage(string message)
        {
            _messageText.text = message ?? string.Empty;
        }

        protected override void RegisterEvents()
        {
            _teamButton.onClick.AddListener(OnTeamClicked);
            _hostButton.onClick.AddListener(OnHostClicked);
            _joinButton.onClick.AddListener(OnJoinClicked);
        }

        protected override void UnRegisterEvents()
        {
            _teamButton.onClick.RemoveListener(OnTeamClicked);
            _hostButton.onClick.RemoveListener(OnHostClicked);
            _joinButton.onClick.RemoveListener(OnJoinClicked);
        }

        private void OnTeamClicked()
        {
            _hostTeam = _hostTeam.Opponent();
            RefreshTeam();
        }

        private void OnHostClicked()
        {
            if (TryGetPort(out ushort port))
            {
                HostRequested?.Invoke(port, _hostTeam);
            }
        }

        private void OnJoinClicked()
        {
            string address = _addressField.text.Trim();
            if (string.IsNullOrEmpty(address))
            {
                SetMessage("Enter the host's address.");
                return;
            }

            if (TryGetPort(out ushort port))
            {
                JoinRequested?.Invoke(address, port);
            }
        }

        private bool TryGetPort(out ushort port)
        {
            if (ushort.TryParse(_portField.text.Trim(), out port) && port > 0)
            {
                return true;
            }

            SetMessage("The port must be a number from 1 to 65535.");
            return false;
        }

        private void RefreshTeam()
        {
            _teamLabel.text = $"Host plays {_hostTeam.ToString().ToUpperInvariant()}";
            _teamLabel.color = _hostTeam == Team.Red ? UIColors.Red : UIColors.Blue;
        }
    }
}
