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
        private BattleClient clientOne;
        private BattleClient clientTwo;
        private ClientDebugController clientOneDebug;
        private ClientDebugController clientTwoDebug;
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

            clientOne = CreateClient(ClientEndpointId.ClientA, clientOneDebugView, out clientOneDebug);
            clientTwo = CreateClient(ClientEndpointId.ClientB, clientTwoDebugView, out clientTwoDebug);
            clientOneDebug.RecreateRequested += OnRecreateClientRequested;
            clientTwoDebug.RecreateRequested += OnRecreateClientRequested;
            clientOneView.Bind(clientOne, config.BoardSize);
            clientTwoView.Bind(clientTwo, config.BoardSize);
            RenderServerStatus(adapter.Status);
            transportLogView.Render(transport.Log);

            lastTransportRealtime = Time.realtimeSinceStartupAsDouble;
            clientOne.Join();
            clientTwo.Join();
            ProcessServerDeadlinesAsync(lifetimeCancellation.Token).Forget();
            RefreshTimersAsync(lifetimeCancellation.Token).Forget();
        }

        private BattleClient CreateClient(ClientEndpointId endpointId, ClientDebugView debugView,
            out ClientDebugController debugController)
        {
            var client = new BattleClient();
            var endpoint = transport.RegisterClient(endpointId, new NetworkSettings(), client);
            client.AttachEndpoint(endpoint);
            debugController = new ClientDebugController(client, endpoint);
            debugView.Bind(debugController);
            client.RequestSent += ScheduleTransportPump;
            return client;
        }

        private void ScheduleTransportPump()
        {
            if (pumpScheduled) return;
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

        private async UniTaskVoid RefreshTimersAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                clientOneView.RefreshTimer(now);
                clientTwoView.RefreshTimer(now);
                await UniTask.Delay(100, DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private async UniTaskVoid ProcessServerDeadlinesAsync(
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (adapter != null && adapter.ProcessDeadlines())
                    ScheduleTransportPump();

                await UniTask.Delay(100, DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private void RenderServerStatus(ServerTransportStatus status) =>
            serverStatusView.Render(status.ConnectedClients, status.MatchReady, status.CurrentPlayer,
                status.HasWinner, status.StateVersion, status.TurnId);

        private void OnRecreateClientRequested(ClientEndpointId endpointId) =>
            Debug.Log($"Recreate requested for {endpointId}; recovery is intentionally deferred to PLAN.md 4.9.");

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
            if (clientOne != null) clientOne.RequestSent -= ScheduleTransportPump;
            if (clientTwo != null) clientTwo.RequestSent -= ScheduleTransportPump;
            if (clientOneDebug != null) clientOneDebug.RecreateRequested -= OnRecreateClientRequested;
            if (clientTwoDebug != null) clientTwoDebug.RecreateRequested -= OnRecreateClientRequested;
            clientOneDebugView?.Unbind();
            clientTwoDebugView?.Unbind();
            clientOneDebug?.Dispose();
            clientTwoDebug?.Dispose();
            clientOne?.Dispose();
            clientTwo?.Dispose();
            serverRegistration?.Dispose();
            transport?.Dispose();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
            clientOneDebug = null;
            clientTwoDebug = null;
            clientOne = null;
            clientTwo = null;
            adapter = null;
            transport = null;
        }

        private void OnDestroy() => DisposeRuntime();
    }
}
