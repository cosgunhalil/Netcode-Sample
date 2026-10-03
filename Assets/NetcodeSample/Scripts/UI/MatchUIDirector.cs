using HannibalUI.Runtime.Base;

namespace NetcodeSample.UI
{
    /// <summary>The screens of the match UI, by HannibalUI screen id.</summary>
    public static class MatchScreens
    {
        public static readonly ScreenId MainMenu = new("MainMenu");
        public static readonly ScreenId Connecting = new("Connecting");
        public static readonly ScreenId Hud = new("Hud");
    }

    /// <summary>
    /// HannibalUI director for the match UI. Hand-written instead of generated: the game drives navigation
    /// directly (connecting, match start, match end), so there's no event route table.
    /// </summary>
    public sealed class MatchUIDirector : VP_Director
    {
        protected override ScreenId StartScreen => MatchScreens.MainMenu;
    }
}
