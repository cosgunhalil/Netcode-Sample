using HannibalUI.Runtime.Base;
using NetcodeSample.Simulation;
using UnityEngine;

namespace NetcodeSample.UI
{
    /// <summary>
    /// The match UI as the game sees it: navigation between screens, the screens' events and displays, the round-end
    /// popup and the debug overlay. Built by <c>Netcode Sample &gt; Set Up Network Match Scene</c>.
    /// </summary>
    public sealed class MatchUI : MonoBehaviour
    {
        // A HannibalUI screen switch deactivates the old screen (0.5 s) then activates the new one (0.5 s). A switch
        // started before the previous one finishes can leave the previous target enabled too (the cancelled switch
        // still activates its screen), so switches are serialized here: a request during a switch waits for it.
        private const float SwitchSeconds = 1.05f;

        [SerializeField]
        private MatchUIDirector _director;

        [SerializeField]
        private MainMenuScreen _mainMenu;

        [SerializeField]
        private ConnectingScreen _connecting;

        [SerializeField]
        private HudScreen _hud;

        [SerializeField]
        private DebugOverlay _debugOverlay;

        [SerializeField]
        private RoundEndPopup _roundEndPopupPrefab;

        private RoundEndPopup _roundEndPopup;
        private ScreenId _pendingScreen;
        private ScreenId _currentScreen;
        private float _switchEndsAt;

        public MainMenuScreen MainMenu => _mainMenu;

        public ConnectingScreen Connecting => _connecting;

        public HudScreen Hud => _hud;

        public DebugOverlay DebugOverlay => _debugOverlay;

        public bool IsRoundEndShown => _roundEndPopup != null;

        public void ShowMainMenu(string message)
        {
            HideRoundEnd();
            _mainMenu.SetMessage(message);
            Navigate(MatchScreens.MainMenu);
        }

        public void ShowConnecting(string status)
        {
            _connecting.SetStatus(status);
            Navigate(MatchScreens.Connecting);
        }

        public void ShowHud()
        {
            Navigate(MatchScreens.Hud);
        }

        /// <summary>Shows the round result, or updates it if it's already up.</summary>
        public void ShowRoundEnd(RoundResult result, Team localTeam, int redScore, int blueScore, float secondsToNextRound)
        {
            if (_roundEndPopup == null)
            {
                _roundEndPopup = (RoundEndPopup)_director.Navigation.ShowPopup(_roundEndPopupPrefab);
            }

            _roundEndPopup?.Show(result, localTeam, redScore, blueScore, secondsToNextRound);
        }

        public void HideRoundEnd()
        {
            if (_roundEndPopup != null)
            {
                _director.Navigation.HidePopup(_roundEndPopup);
                _roundEndPopup = null;
            }
        }

        private void Start()
        {
            // The director shows its start screen in its own Start.
            _currentScreen = MatchScreens.MainMenu;
            _switchEndsAt = Time.unscaledTime + SwitchSeconds;
        }

        private void Update()
        {
            if (_pendingScreen.IsValid && Time.unscaledTime >= _switchEndsAt)
            {
                ScreenId screen = _pendingScreen;
                _pendingScreen = default;
                Navigate(screen);
            }

            if (Time.unscaledTime >= _switchEndsAt)
            {
                HideOtherScreens();
            }
        }

        // Once a switch has settled only the current screen may be up; anything else is a leftover of an
        // interrupted HannibalUI transition.
        private void HideOtherScreens()
        {
            HideIfOther(_mainMenu);
            HideIfOther(_connecting);
            HideIfOther(_hud);
        }

        private void HideIfOther(VP_Canvas screen)
        {
            if (screen.ScreenType != _currentScreen && screen.TryGetComponent(out Canvas canvas) && canvas.enabled)
            {
                canvas.enabled = false;
            }
        }

        // Only the latest request matters: a queued screen is replaced by a newer one.
        private void Navigate(ScreenId screen)
        {
            if (Time.unscaledTime < _switchEndsAt)
            {
                _pendingScreen = screen;
                return;
            }

            _director.Show(screen);
            _currentScreen = screen;
            _switchEndsAt = Time.unscaledTime + SwitchSeconds;
        }

    }
}
