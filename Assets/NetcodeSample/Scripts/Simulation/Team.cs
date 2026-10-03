namespace NetcodeSample.Simulation
{
    public enum Team : byte
    {
        Red = 0,
        Blue = 1,
    }

    public static class TeamExtensions
    {
        public const int Count = 2;

        public static Team Opponent(this Team team)
        {
            return team == Team.Red ? Team.Blue : Team.Red;
        }
    }
}
