using System;
using System.Collections.Generic;
using Battleships.Client;
using Battleships.Networking;
using Battleships.Protocol;
using TMPro;
using UnityEngine;

namespace Battleships.Presentation
{
    public sealed class ClientView : MonoBehaviour
    {
        private static readonly Color PositiveStatusColor = new Color32(0, 255, 28, 255);

        [SerializeField] private TMP_Text playerNumber;
        [SerializeField] private TMP_Text yourTurn;
        [SerializeField] private TMP_Text matchStatus;
        [SerializeField] private TMP_Text stateVersion;
        [SerializeField] private TMP_Text turnId;
        [SerializeField] private TMP_Text timer;
        [SerializeField] private TMP_Text pendingRequest;
        [SerializeField] private TMP_Text endpoint;
        [SerializeField] private TMP_Text connected;
        [SerializeField] private TMP_Text lastRequestId;
        [SerializeField] private TMP_Text recentEvents;
        [SerializeField] private BoardView ownBoard;
        [SerializeField] private BoardView opponentBoard;

        private readonly Queue<string> events = new Queue<string>();
        private BattleClient client;

        public void Bind(BattleClient battleClient, int configuredBoardSize)
        {
            Unbind();
            client = battleClient ?? throw new ArgumentNullException(nameof(battleClient));
            ownBoard.Initialize(configuredBoardSize, null);
            opponentBoard.Initialize(configuredBoardSize, Fire);
            client.StateChanged += Render;
            client.RuntimeEvent += AddEvent;
            Render();
        }

        public void RefreshTimer(long unixTimeMilliseconds)
        {
            if (client == null || timer == null) return;
            var state = client.State;
            if (state.MatchStatus != ClientMatchStatus.InProgress ||
                state.TurnDeadlineUnixTimeMilliseconds <= 0)
            {
                timer.text = "00 : 00";
                return;
            }

            var remaining = Math.Max(0,
                state.TurnDeadlineUnixTimeMilliseconds - unixTimeMilliseconds);
            var seconds = (int)Math.Ceiling(remaining / 1000d);
            timer.text = $"{seconds / 60:00} : {seconds % 60:00}";
        }

        private void Fire(ClientPosition position) => client?.TryFire(position);

        private void Render()
        {
            if (client == null) return;
            var state = client.State;
            playerNumber.text = state.HasIdentity
                ? state.PlayerSlot == PlayerSlot.PlayerOne ? "Player 1" : "Player 2"
                : "Waiting";
            SetStatusText(yourTurn, state.IsYourTurn ? "Your turn" : "Waiting", state.IsYourTurn);
            matchStatus.text = MatchStatusText(state.MatchStatus);
            stateVersion.text = state.StateVersion > 0 ? $"v{state.StateVersion}" : "v0";
            turnId.text = state.TurnId > 0 ? state.TurnId.ToString() : "-";
            pendingRequest.text = state.PendingShot == null
                ? string.Empty
                : $"Fire {state.PendingShot.Target}\nRequest #{ShortId(state.PendingShot.RequestId)}";
            endpoint.text = client.Connection.IsConnected
                ? $"{FriendlyEndpoint(client.Connection.Endpoint.EndpointId)} / gen {client.Connection.Endpoint.Generation}"
                : "Not registered";
            SetStatusText(connected, client.Connection.IsConnected ? "Yes" : "No",
                client.Connection.IsConnected);
            lastRequestId.text = string.IsNullOrWhiteSpace(client.LastRequestId)
                ? "-"
                : $"#{ShortId(client.LastRequestId)}";
            ownBoard.Render(state);
            opponentBoard.Render(state);
        }

        private void AddEvent(string value)
        {
            if (events.Count == 5) events.Dequeue();
            events.Enqueue(value);
            recentEvents.text = string.Join("\n", events);
        }

        private void Unbind()
        {
            if (client == null) return;
            client.StateChanged -= Render;
            client.RuntimeEvent -= AddEvent;
            client = null;
        }

        private void OnDestroy() => Unbind();

        private static string MatchStatusText(ClientMatchStatus status)
        {
            switch (status)
            {
                case ClientMatchStatus.WaitingForPlayers: return "Waiting for players";
                case ClientMatchStatus.InProgress: return "In progress";
                case ClientMatchStatus.Finished: return "Finished";
                default: throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }

        private static string FriendlyEndpoint(ClientEndpointId id) =>
            id == ClientEndpointId.ClientA ? "Client A" : "Client B";

        private static string ShortId(string requestId) =>
            requestId.Length <= 8 ? requestId : requestId.Substring(0, 8);

        private static void SetStatusText(TMP_Text target, string value, bool positive)
        {
            target.text = value;
            target.color = positive ? PositiveStatusColor : Color.red;
        }
    }
}
