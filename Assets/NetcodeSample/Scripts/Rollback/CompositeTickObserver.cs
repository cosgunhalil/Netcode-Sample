namespace NetcodeSample.Rollback
{
    /// <summary>Forwards tick notifications to several observers in order, e.g. the Tickwise recorder and the presentation.</summary>
    public sealed class CompositeTickObserver : ITickObserver
    {
        private readonly ITickObserver[] _observers;

        public CompositeTickObserver(params ITickObserver[] observers)
        {
            _observers = observers;
        }

        public void OnTickSimulated(long tick)
        {
            foreach (ITickObserver observer in _observers)
            {
                observer?.OnTickSimulated(tick);
            }
        }

        public void OnTickConfirmed(long tick, Simulation.GameInput red, Simulation.GameInput blue)
        {
            foreach (ITickObserver observer in _observers)
            {
                observer?.OnTickConfirmed(tick, red, blue);
            }
        }
    }
}
