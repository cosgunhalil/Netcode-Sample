using FishNet.Broadcast;
using NetcodeSample.Rollback;

namespace NetcodeSample.Networking
{
    /// <summary>Bumped whenever a message layout or the handshake changes; peers refuse a different version.</summary>
    internal static class Protocol
    {
        public const int Version = 1;
    }

    /// <summary>The joiner's first message: who it is and what it runs.</summary>
    public struct HelloMessage : IBroadcast
    {
        public int ProtocolVersion;

        /// <summary>Rules hash and navmesh checksum; both peers must match exactly.</summary>
        public string Build;
    }

    /// <summary>The host's answer. On acceptance both peers start the match with these settings.</summary>
    public struct WelcomeMessage : IBroadcast
    {
        public bool Accepted;
        public string RejectReason;

        /// <summary>The joiner's team (the host plays the other).</summary>
        public byte JoinerTeam;

        /// <summary>Names this match; both peers put it in their recording file names.</summary>
        public string SessionId;
        public int InputDelayTicks;
        public int MaxRollbackTicks;
    }

    /// <summary>
    /// <see cref="InputMessage"/> as a FishNet broadcast. The 32 input bytes travel as four ulongs: fixed-size
    /// fields serialize without allocating, unlike an array.
    /// </summary>
    public struct NetInputMessage : IBroadcast
    {
        public long StartTick;
        public int Count;
        public long AckTick;
        public long SenderTick;
        public int SenderAdvantage;
        public ulong Inputs0;
        public ulong Inputs1;
        public ulong Inputs2;
        public ulong Inputs3;

        public static unsafe NetInputMessage From(in InputMessage message)
        {
            NetInputMessage net = new()
            {
                StartTick = message.StartTick,
                Count = message.Count,
                AckTick = message.AckTick,
                SenderTick = message.SenderTick,
                SenderAdvantage = message.SenderAdvantage,
            };

            fixed (byte* inputs = message.Inputs)
            {
                ulong* words = (ulong*)inputs;
                net.Inputs0 = words[0];
                net.Inputs1 = words[1];
                net.Inputs2 = words[2];
                net.Inputs3 = words[3];
            }

            return net;
        }

        public unsafe InputMessage ToInputMessage()
        {
            InputMessage message = new()
            {
                StartTick = StartTick,

                // Fixed buffers aren't bounds-checked: never trust a count from the network.
                Count = System.Math.Clamp(Count, 0, InputMessage.MaxInputs),
                AckTick = AckTick,
                SenderTick = SenderTick,
                SenderAdvantage = SenderAdvantage,
            };

            ulong* words = (ulong*)message.Inputs;
            words[0] = Inputs0;
            words[1] = Inputs1;
            words[2] = Inputs2;
            words[3] = Inputs3;
            return message;
        }
    }
}
