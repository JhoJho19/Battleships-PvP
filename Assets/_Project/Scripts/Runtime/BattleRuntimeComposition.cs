using System;
using Battleships.Client;
using Battleships.Configuration;
using Battleships.Networking;
using Battleships.Networking.Integration;
using Battleships.Presentation;
using Battleships.Server;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Battleships.Runtime
{
    public sealed class BattleRuntimeComposition : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private ClientView clientOneView;
        [SerializeField] private ClientView clientTwoView;
        [SerializeField] private ServerStatusView serverStatusView;
        [SerializeField] private TransportLogView transportLogView;
        [SerializeField] private int placementSeed = 46;

        private InProcessTransport transport;
        private BattleServerTransportAdapter adapter;
        private BattleClient clientOne;
        private BattleClient clientTwo;
        private IDisposable serverRegistration;
        private bool pumpScheduled;

        private void Start()
        {
            if (config == null) throw new InvalidOperationException("GameConfig is not assigned.");

            transport = new InProcessTransport(new XmlMessageSerializer(ProtocolMessageTypes.All));
            var server = new BattleServer(config.CreateGameRulesConfig(), new System.Random(placementSeed),
                new SystemServerClock(), (long)(config.TurnDurationSeconds * 1000));
            adapter = new BattleServerTransportAdapter(server, transport);
            adapter.StatusChanged += RenderServerStatus;
            serverRegistration = transport.RegisterServer(adapter);

            clientOne = CreateClient(ClientEndpointId.ClientA);
            clientTwo = CreateClient(ClientEndpointId.ClientB);
            clientOneView.Bind(clientOne, config.BoardSize);
            clientTwoView.Bind(clientTwo, config.BoardSize);
            RenderServerStatus(adapter.Status);
            transportLogView.Render(transport.Log);

            clientOne.Join();
            clientTwo.Join();
            RefreshTimersAsync(destroyCancellationToken).Forget();
        }

        private BattleClient CreateClient(ClientEndpointId endpointId)
        {
            var client = new BattleClient();
            client.AttachEndpoint(transport.RegisterClient(endpointId, new NetworkSettings(), client));
            client.RequestSent += ScheduleTransportPump;
            return client;
        }

        private void ScheduleTransportPump()
        {
            if (pumpScheduled) return;
            pumpScheduled = true;
            PumpOnNextTickAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid PumpOnNextTickAsync(System.Threading.CancellationToken cancellationToken)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            pumpScheduled = false;
            if (transport == null) return;
            transport.ProcessPending();
            transportLogView.Render(transport.Log);
        }

        private async UniTaskVoid RefreshTimersAsync(System.Threading.CancellationToken cancellationToken)
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

        private void RenderServerStatus(ServerTransportStatus status) =>
            serverStatusView.Render(status.ConnectedClients, status.MatchReady, status.CurrentPlayer,
                status.HasWinner, status.StateVersion, status.TurnId);

        private void OnDestroy()
        {
            if (adapter != null) adapter.StatusChanged -= RenderServerStatus;
            if (clientOne != null) clientOne.RequestSent -= ScheduleTransportPump;
            if (clientTwo != null) clientTwo.RequestSent -= ScheduleTransportPump;
            clientOne?.Dispose();
            clientTwo?.Dispose();
            serverRegistration?.Dispose();
            transport?.Dispose();
            clientOne = null;
            clientTwo = null;
            adapter = null;
            transport = null;
        }
    }
}
