using System;

namespace NetcodeSample.Simulation
{
    /// <summary>
    /// One player's input for one tick. Small cubes spawn on a schedule, so the only input is the big-cube button.
    /// Button presses are one-shot events: a press is consumed by the tick it belongs to.
    /// </summary>
    public readonly struct GameInput : IEquatable<GameInput>
    {
        private const byte SpawnBigButton = 1 << 0;

        public GameInput(byte buttons)
        {
            Buttons = buttons;
        }

        public static GameInput None => default;

        public static GameInput SpawnBig => new(SpawnBigButton);

        /// <summary>The raw bit field, as sent over the network and recorded by Tickwise.</summary>
        public byte Buttons { get; }

        public bool WantsSpawnBig => (Buttons & SpawnBigButton) != 0;

        public static bool operator ==(GameInput left, GameInput right) => left.Equals(right);

        public static bool operator !=(GameInput left, GameInput right) => !left.Equals(right);

        public bool Equals(GameInput other) => Buttons == other.Buttons;

        public override bool Equals(object obj) => obj is GameInput other && Equals(other);

        public override int GetHashCode() => Buttons;

        public override string ToString() => WantsSpawnBig ? "SpawnBig" : "None";
    }
}
