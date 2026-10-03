namespace NetcodeSample.Simulation
{
    /// <summary>Switches on the planted determinism bug in <see cref="GameSimulation.Chaos"/>, for the Tickwise demo.</summary>
    public struct ChaosSettings
    {
        public bool Enabled;

        /// <summary>The first tick the bug affects.</summary>
        public long FromTick;
    }
}
