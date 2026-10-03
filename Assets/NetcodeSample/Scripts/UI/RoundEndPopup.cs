using HannibalUI.Runtime.Base;
using NetcodeSample.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace NetcodeSample.UI
{
    /// <summary>The round result over the HUD, shown through HannibalUI's popup layer until the next round starts.</summary>
    public sealed class RoundEndPopup : VP_Popup
    {
        [SerializeField]
        private Text _titleText;

        [SerializeField]
        private Text _detailText;

        public void Show(RoundResult result, Team localTeam, int redScore, int blueScore, float secondsToNextRound)
        {
            Team? winner = result == RoundResult.RedWon ? Team.Red : result == RoundResult.BlueWon ? Team.Blue : null;
            if (winner == null)
            {
                _titleText.text = "DRAW";
                _titleText.color = Color.white;
            }
            else
            {
                _titleText.text = winner == localTeam ? "ROUND WON" : "ROUND LOST";
                _titleText.color = winner == Team.Red ? UIColors.Red : UIColors.Blue;
            }

            _detailText.text = $"{redScore} : {blueScore}\nNext round in {Mathf.CeilToInt(secondsToNextRound)}";
        }
    }
}
