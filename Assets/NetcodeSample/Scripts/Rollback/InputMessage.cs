using System;

namespace NetcodeSample.Rollback
{
    /// <summary>
    /// What peers send each other every tick, unreliably: the sender's recent inputs (resent until acknowledged,
    /// so a lost packet costs nothing), what it has received so far, and its clock for time sync.
    /// </summary>
    public unsafe struct InputMessage
    {
        /// <summary>The most inputs one message carries; must exceed input delay + max rollback.</summary>
        public const int MaxInputs = 32;

        /// <summary>The tick of <c>Inputs[0]</c>.</summary>
        public long StartTick;
        public int Count;

        /// <summary>The sender has every input of the receiver up to and including this tick.</summary>
        public long AckTick;

        /// <summary>The last tick the sender simulated.</summary>
        public long SenderTick;

        /// <summary>The sender's frame advantage (its tick minus the receiver's tick as last heard), for time sync.</summary>
        public int SenderAdvantage;

        public fixed byte Inputs[MaxInputs];

        public long EndTick => StartTick + Count - 1;

        public byte GetInput(long tick)
        {
            if (tick < StartTick || tick > EndTick)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            return Inputs[tick - StartTick];
        }
    }

    /// <summary>The network as the rollback session sees it: unreliable, unordered datagrams to the other peer.</summary>
    public interface IInputTransport
    {
        void Send(in InputMessage message);

        bool TryReceive(out InputMessage message);
    }
}
