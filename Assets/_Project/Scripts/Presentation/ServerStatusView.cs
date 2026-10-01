using Battleships.Protocol;
using TMPro;
using UnityEngine;

namespace Battleships.Presentation
{
    public sealed class ServerStatusView : MonoBehaviour
    {
        [SerializeField] private TMP_Text connectedClients;
        [SerializeField] private TMP_Text matchStatus;
        [SerializeField] private TMP_Text stateVersion;
        [SerializeField] private TMP_Text currentTurn;
        [SerializeField] private TMP_Text turnId;

        public void Render(int clients, bool matchReady, PlayerSlot currentPlayer,
            bool hasWinner, long version, long currentTurnId)
        {
            connectedClients.text = $"Connected Clients: {clients}/2";
            matchStatus.text = $"Match Status: {(matchReady ? hasWinner ? "Finished" : "In Progress" : "Waiting for Players")}";
            stateVersion.text = $"State Version: {(matchReady ? $"v{version}" : "-")}";
            currentTurn.text = $"Current Turn: {(matchReady && !hasWinner ? PlayerText(currentPlayer) : "-")}";
            turnId.text = $"Turn Id: {(matchReady ? currentTurnId.ToString() : "-")}";
        }

        private static string PlayerText(PlayerSlot player) =>
            player == PlayerSlot.PlayerOne ? "Player 1" : "Player 2";
    }
}
