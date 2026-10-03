using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using NetcodeSample.Rollback;
using NetcodeSample.Simulation;

namespace NetcodeSample.Networking
{
    public enum PeerState
    {
        Idle,
        Connecting,

        /// <summary>Hosting, waiting for the other player to join.</summary>
        WaitingForPeer,

        /// <summary>Handshake done; the match runs.</summary>
        Ready,

        /// <summary>Couldn't connect, or the handshake was refused.</summary>
        Failed,

        /// <summary>Was ready, then the connection ended.</summary>
        Disconnected,
    }

    /// <summary>
    /// Connects two peers with FishNet and carries rollback inputs between them. FishNet is only the transport:
    /// the host runs a server (no local client) and the joiner connects to it as its only client; nothing is
    /// synchronized through NetworkObjects. After a handshake that checks both peers run the same build, inputs go
    /// both ways as unreliable broadcasts.
    /// </summary>
    public sealed class FishNetPeer : IInputTransport, IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly string _build;
        private readonly Queue<InputMessage> _received = new();
        private NetworkConnection _remote;
        private RollbackSettings _hostSettings;
        private Team _hostTeam;
        private bool _isHost;
        private bool _started;

        /// <param name="build">What both peers must share, e.g. the rules hash and navmesh checksum.</param>
        public FishNetPeer(NetworkManager networkManager, string build)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _build = build;
        }

        /// <summary>Raised once when the handshake succeeds; <see cref="LocalTeam"/>, <see cref="Settings"/> and <see cref="SessionId"/> are set.</summary>
        public event Action MatchReady;

        public PeerState State { get; private set; }

        public string StatusMessage { get; private set; } = string.Empty;

        public bool IsHost => _isHost;

        public Team LocalTeam { get; private set; }

        public RollbackSettings Settings { get; private set; }

        public string SessionId { get; private set; }

        /// <summary>The joiner's measured round trip in milliseconds; the server-only host doesn't measure one.</summary>
        public long RoundTripTimeMs => _isHost ? -1 : _networkManager.TimeManager.RoundTripTime;

        /// <summary>Opens a server on <paramref name="port"/> and waits for one player to join.</summary>
        public void Host(ushort port, Team hostTeam, RollbackSettings settings)
        {
            EnsureIdle();
            _isHost = true;
            _started = true;
            _hostTeam = hostTeam;
            _hostSettings = settings;

            Transport transport = _networkManager.TransportManager.Transport;
            transport.SetMaximumClients(1);
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _networkManager.ServerManager.RegisterBroadcast<HelloMessage>(OnHello);
            _networkManager.ServerManager.RegisterBroadcast<NetInputMessage>(OnServerInput);

            SetState(PeerState.Connecting, $"Starting server on port {port}...");
            if (!_networkManager.ServerManager.StartConnection(port))
            {
                SetState(PeerState.Failed, $"Could not start a server on port {port}.");
            }
        }

        /// <summary>Connects to a host at <paramref name="address"/>.</summary>
        public void Join(string address, ushort port)
        {
            EnsureIdle();
            _isHost = false;
            _started = true;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _networkManager.ClientManager.RegisterBroadcast<WelcomeMessage>(OnWelcome);
            _networkManager.ClientManager.RegisterBroadcast<NetInputMessage>(OnClientInput);

            SetState(PeerState.Connecting, $"Connecting to {address}:{port}...");
            if (!_networkManager.ClientManager.StartConnection(address, port))
            {
                SetState(PeerState.Failed, $"Could not connect to {address}:{port}.");
            }
        }

        /// <summary>FishNet's latency simulator, on this peer's outgoing traffic. Set it on both peers for a two-way delay.</summary>
        public void SetSimulatedConditions(bool enabled, long latencyMs, double packetLoss, double outOfOrder)
        {
            LatencySimulator simulator = _networkManager.TransportManager.LatencySimulator;
            simulator.SetLatency(latencyMs);
            simulator.SetPacketLoss(packetLoss);
            simulator.SetOutOfOrder(outOfOrder);
            simulator.SetEnabled(enabled);
        }

        public void Send(in InputMessage message)
        {
            if (State != PeerState.Ready)
            {
                return;
            }

            NetInputMessage net = NetInputMessage.From(message);
            if (_isHost)
            {
                _networkManager.ServerManager.Broadcast(_remote, net, true, Channel.Unreliable);
            }
            else
            {
                _networkManager.ClientManager.Broadcast(net, Channel.Unreliable);
            }
        }

        public bool TryReceive(out InputMessage message)
        {
            if (_received.Count > 0)
            {
                message = _received.Dequeue();
                return true;
            }

            message = default;
            return false;
        }

        public void Dispose()
        {
            ServerManagerCleanup();
            ClientManagerCleanup();
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                SetState(PeerState.WaitingForPeer, "Hosting. Waiting for the other player to join...");
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped && State != PeerState.Ready && State != PeerState.Disconnected)
            {
                SetState(PeerState.Failed, "The server stopped.");
            }
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped && connection == _remote)
            {
                SetState(PeerState.Disconnected, "The other player left.");
            }
        }

        private void OnHello(NetworkConnection connection, HelloMessage hello, Channel channel)
        {
            if (_remote != null)
            {
                return;
            }

            string reason = null;
            if (hello.ProtocolVersion != Protocol.Version)
            {
                reason = $"Protocol version {hello.ProtocolVersion} doesn't match the host's {Protocol.Version}.";
            }
            else if (hello.Build != _build)
            {
                reason = $"Different game build. Host: {_build}. Joiner: {hello.Build}. Use the same rules and level.";
            }

            if (reason != null)
            {
                _networkManager.ServerManager.Broadcast(connection, new WelcomeMessage { Accepted = false, RejectReason = reason }, true, Channel.Reliable);
                connection.Disconnect(immediately: false);
                return;
            }

            _remote = connection;
            Team joinerTeam = _hostTeam.Opponent();
            SessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            _networkManager.ServerManager.Broadcast(connection, new WelcomeMessage
            {
                Accepted = true,
                JoinerTeam = (byte)joinerTeam,
                SessionId = SessionId,
                InputDelayTicks = _hostSettings.InputDelayTicks,
                MaxRollbackTicks = _hostSettings.MaxRollbackTicks,
            }, true, Channel.Reliable);

            LocalTeam = _hostTeam;
            Settings = _hostSettings;
            SetState(PeerState.Ready, $"Playing {LocalTeam} against {joinerTeam}.");
            MatchReady?.Invoke();
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                SetState(PeerState.Connecting, "Connected. Checking the game build...");
                _networkManager.ClientManager.Broadcast(new HelloMessage { ProtocolVersion = Protocol.Version, Build = _build }, Channel.Reliable);
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (State == PeerState.Ready)
                {
                    SetState(PeerState.Disconnected, "The connection to the host ended.");
                }
                else if (State != PeerState.Failed)
                {
                    SetState(PeerState.Failed, "Could not connect to the host.");
                }
            }
        }

        private void OnWelcome(WelcomeMessage welcome, Channel channel)
        {
            if (!welcome.Accepted)
            {
                SetState(PeerState.Failed, $"The host refused: {welcome.RejectReason}");
                return;
            }

            LocalTeam = (Team)welcome.JoinerTeam;
            SessionId = welcome.SessionId;
            Settings = new RollbackSettings { InputDelayTicks = welcome.InputDelayTicks, MaxRollbackTicks = welcome.MaxRollbackTicks };
            SetState(PeerState.Ready, $"Playing {LocalTeam} against {LocalTeam.Opponent()}.");
            MatchReady?.Invoke();
        }

        private void OnServerInput(NetworkConnection connection, NetInputMessage message, Channel channel)
        {
            if (connection == _remote)
            {
                _received.Enqueue(message.ToInputMessage());
            }
        }

        private void OnClientInput(NetInputMessage message, Channel channel)
        {
            _received.Enqueue(message.ToInputMessage());
        }

        private void EnsureIdle()
        {
            if (State != PeerState.Idle)
            {
                throw new InvalidOperationException("A peer hosts or joins once; create a new one for another match.");
            }
        }

        private void SetState(PeerState state, string message)
        {
            State = state;
            StatusMessage = message;
        }

        private void ServerManagerCleanup()
        {
            if (!_started || !_isHost || _networkManager == null)
            {
                return;
            }

            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            _networkManager.ServerManager.UnregisterBroadcast<HelloMessage>(OnHello);
            _networkManager.ServerManager.UnregisterBroadcast<NetInputMessage>(OnServerInput);
            if (_networkManager.ServerManager.Started)
            {
                _networkManager.ServerManager.StopConnection(sendDisconnectMessage: true);
            }
        }

        private void ClientManagerCleanup()
        {
            if (!_started || _isHost || _networkManager == null)
            {
                return;
            }

            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            _networkManager.ClientManager.UnregisterBroadcast<WelcomeMessage>(OnWelcome);
            _networkManager.ClientManager.UnregisterBroadcast<NetInputMessage>(OnClientInput);
            if (_networkManager.ClientManager.Started)
            {
                _networkManager.ClientManager.StopConnection();
            }
        }
    }
}
