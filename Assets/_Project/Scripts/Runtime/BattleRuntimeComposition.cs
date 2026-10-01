using System;
using System.Threading;
using Battleships.Client;
using Battleships.Configuration;
using Battleships.Networking;
using Battleships.Networking.Integration;
using Battleships.Presentation;
using Battleships.Server;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Battleships.Runtime
{
    public sealed class BattleRuntimeComposition : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private ClientView clientOneView;
        [SerializeField] private ClientView clientTwoView;
        [SerializeField] private ClientDebugView clientOneDebugView;
        [SerializeField] private ClientDebugView clientTwoDebugView;
        [SerializeField] private ServerStatusView serverStatusView;
        [SerializeField] private TransportLogView transportLogView;
        [SerializeField] private Button restartSceneButton;
        [SerializeField] private int placementSeed = 46;

        private InProcessTransport transport;
        private BattleServerTransportAdapter adapter;
        private ClientSessionIdentity clientOneIdentity;
        private ClientSessionIdentity clientTwoIdentity;
        private ClientRuntime clientOneRuntime;
        private ClientRuntime clientTwoRuntime;
        private IDisposable serverRegistration;
        private CancellationTokenSource lifetimeCancellation;
        private bool pumpScheduled;
        private bool disposed;
        private double lastTransportRealtime;

        private void Start()
        {
            if (config == null) throw new InvalidOperationException("GameConfig is not assigned.");

            lifetimeCancellation = new CancellationTokenSource();
            restartSceneButton.onClick.AddListener(RestartScene);

            transport = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));
            var server = new BattleServer(config.CreateGameRulesConfig(), new System.Random(placementSeed),
                new SystemServerClock(), (long)(config.TurnDurationSeconds * 1000));
            adapter = new BattleServerTransportAdapter(server, transport);
            adapter.StatusChanged += RenderServerStatus;
            serverRegistration = transport.RegisterServer(adapter);

            clientOneIdentity = new ClientSessionIdentity();
            clientTwoIdentity = new ClientSessionIdentity();
            clientOneRuntime = CreateClientRuntime(ClientEndpointId.ClientA, clientOneIdentity,
                clientOneView, clientOneDebugView, new NetworkSettings(silentlyDisconnected: true), true);
            clientTwoRuntime = CreateClientRuntime(ClientEndpointId.ClientB, clientTwoIdentity,
                clientTwoView, clientTwoDebugView, new NetworkSettings(silentlyDisconnected: true), true);
            RenderServerStatus(adapter.Status);
            transportLogView.Render(transport.Log);

            lastTransportRealtime = Time.realtimeSinceStartupAsDouble;
            ProcessServerDeadlinesAsync(lifetimeCancellation.Token).Forget();
            RefreshTimersAsync(lifetimeCancellation.Token).Forget();
        }

        private ClientRuntime CreateClientRuntime(ClientEndpointId endpointId,
            ClientSessionIdentity sessionIdentity, ClientView clientView, ClientDebugView debugView,
            NetworkSettings settings, bool loggingEnabled)
        {
            var receiver = new ClientReceiver();
            var endpoint = transport.RegisterClient(endpointId, settings, receiver);
            endpoint.LoggingEnabled = loggingEnabled;
            adapter.ActivateEndpoint(endpoint.Identity);
            var client = new BattleClient(sessionIdentity, config.HeartbeatTimeoutSeconds * 1000d,
                RealtimeMilliseconds);
            receiver.Client = client;
            client.AttachEndpoint(endpoint);
            var debug = new ClientDebugController(client, endpoint);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            var runtime = new ClientRuntime(endpointId, sessionIdentity, client, endpoint, debug,
                clientView, debugView, cancellation, receiver);

            client.RequestSent += ScheduleTransportPump;
            debug.RecreateRequested += OnRecreateClientRequested;
            clientView.Bind(client, config.BoardSize);
            debugView.Bind(debug);
            RunHeartbeatAsync(runtime, cancellation.Token).Forget();
            return runtime;
        }

        private void ScheduleTransportPump()
        {
            if (pumpScheduled) return;
            lastTransportRealtime = Time.realtimeSinceStartupAsDouble;
            pumpScheduled = true;
            PumpTransportAsync(lifetimeCancellation.Token).Forget();
        }

        private async UniTaskVoid PumpTransportAsync(CancellationToken cancellationToken)
        {
            do
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                if (transport == null) break;

                var now = Time.realtimeSinceStartupAsDouble;
                transport.AdvanceTimeBy(Math.Max(0, (now - lastTransportRealtime) * 1000d));
                lastTransportRealtime = now;
                transportLogView.Render(transport.Log);
            } while (transport.PendingCount > 0);

            pumpScheduled = false;
        }

        private async UniTaskVoid RunHeartbeatAsync(ClientRuntime runtime,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await UniTask.Delay((int)(config.HeartbeatIntervalSeconds * 1000f),
                    DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                runtime.Client.CheckConnection();
                runtime.Client.SendHeartbeat();
            }
        }

        private async UniTaskVoid RefreshTimersAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                clientOneRuntime?.ClientView.RefreshTimer(now);
                clientTwoRuntime?.ClientView.RefreshTimer(now);
                await UniTask.Delay(100, DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private async UniTaskVoid ProcessServerDeadlinesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (adapter != null && adapter.ProcessDeadlines()) ScheduleTransportPump();
                await UniTask.Delay(100, DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private void RenderServerStatus(ServerTransportStatus status) =>
            serverStatusView.Render(status.ConnectedClients, status.MatchReady, status.CurrentPlayer,
                status.HasWinner, status.StateVersion, status.TurnId);

        private void OnRecreateClientRequested(ClientEndpointId endpointId)
        {
            var oldRuntime = endpointId == ClientEndpointId.ClientA
                ? clientOneRuntime
                : clientTwoRuntime;
            if (oldRuntime == null) return;

            var previous = oldRuntime.Endpoint.Settings;
            var loggingEnabled = oldRuntime.Endpoint.LoggingEnabled;
            var resumedSettings = new NetworkSettings(previous.LatencyMilliseconds,
                previous.JitterMilliseconds, previous.LossRate, previous.DuplicateRate, true,
                previous.RandomSeed);
            var identity = oldRuntime.SessionIdentity;
            var clientView = oldRuntime.ClientView;
            var debugView = oldRuntime.DebugView;

            DisposeClientRuntime(oldRuntime);
            var replacement = CreateClientRuntime(endpointId, identity, clientView, debugView,
                resumedSettings, loggingEnabled);
            if (endpointId == ClientEndpointId.ClientA) clientOneRuntime = replacement;
            else clientTwoRuntime = replacement;

            replacement.Debug.ReportRuntimeEvent("Client recreated");
            replacement.Debug.Connect();
        }

        private void DisposeClientRuntime(ClientRuntime runtime)
        {
            if (runtime == null) return;
            runtime.Cancellation.Cancel();
            runtime.Client.RequestSent -= ScheduleTransportPump;
            runtime.Debug.RecreateRequested -= OnRecreateClientRequested;
            runtime.ClientView.Unbind();
            runtime.DebugView.Unbind();
            runtime.Debug.Dispose();
            runtime.Receiver.Client = null;
            runtime.Endpoint.Dispose();
            adapter.DeactivateEndpoint(runtime.Endpoint.Identity);
            runtime.Client.Dispose();
            runtime.Cancellation.Dispose();
        }

        private void RestartScene()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            DisposeRuntime();
            SceneManager.LoadScene(sceneName);
        }

        private void DisposeRuntime()
        {
            if (disposed) return;
            disposed = true;
            restartSceneButton?.onClick.RemoveListener(RestartScene);
            lifetimeCancellation?.Cancel();
            if (adapter != null) adapter.StatusChanged -= RenderServerStatus;
            DisposeClientRuntime(clientOneRuntime);
            DisposeClientRuntime(clientTwoRuntime);
            serverRegistration?.Dispose();
            transport?.Dispose();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
            clientOneRuntime = null;
            clientTwoRuntime = null;
            clientOneIdentity = null;
            clientTwoIdentity = null;
            adapter = null;
            transport = null;
        }

        private double RealtimeMilliseconds() => Time.realtimeSinceStartupAsDouble * 1000d;

        private void OnDestroy() => DisposeRuntime();

        private sealed class ClientRuntime
        {
            public ClientEndpointId EndpointId { get; }
            public ClientSessionIdentity SessionIdentity { get; }
            public BattleClient Client { get; }
            public ClientTransportEndpoint Endpoint { get; }
            public ClientDebugController Debug { get; }
            public ClientView ClientView { get; }
            public ClientDebugView DebugView { get; }
            public CancellationTokenSource Cancellation { get; }
            public ClientReceiver Receiver { get; }

            public ClientRuntime(ClientEndpointId endpointId, ClientSessionIdentity sessionIdentity,
                BattleClient client, ClientTransportEndpoint endpoint, ClientDebugController debug,
                ClientView clientView, ClientDebugView debugView,
                CancellationTokenSource cancellation, ClientReceiver receiver)
            {
                EndpointId = endpointId;
                SessionIdentity = sessionIdentity;
                Client = client;
                Endpoint = endpoint;
                Debug = debug;
                ClientView = clientView;
                DebugView = debugView;
                Cancellation = cancellation;
                Receiver = receiver;
            }
        }

        private sealed class ClientReceiver : ITransportMessageReceiver
        {
            public BattleClient Client { get; set; }
            public void Receive(TransportDelivery delivery) => Client?.Receive(delivery);
        }
    }
}
