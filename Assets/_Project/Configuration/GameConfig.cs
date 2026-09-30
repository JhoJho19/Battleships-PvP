using System;
using System.Collections.ObjectModel;
using Battleships.Domain;
using UnityEngine;

namespace Battleships.Configuration
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "Battleships/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        [SerializeField, Min(1)] private int boardSize = 6;
        [SerializeField] private int[] shipLengths = { 3, 2, 2, 1 };
        [SerializeField, Min(0f)] private float turnDurationSeconds = 15f;
        [SerializeField, Min(0f)] private float latencyMilliseconds;
        [SerializeField, Min(0f)] private float jitterMilliseconds;
        [SerializeField, Range(0f, 1f)] private float lossRate;
        [SerializeField, Range(0f, 1f)] private float duplicateRate;
        [SerializeField, Min(0.01f)] private float heartbeatIntervalSeconds = 1f;
        [SerializeField, Min(0.01f)] private float heartbeatTimeoutSeconds = 5f;

        public int BoardSize => boardSize;
        public ReadOnlyCollection<int> ShipLengths => Array.AsReadOnly(shipLengths);
        public float TurnDurationSeconds => turnDurationSeconds;
        public float LatencyMilliseconds => latencyMilliseconds;
        public float JitterMilliseconds => jitterMilliseconds;
        public float LossRate => lossRate;
        public float DuplicateRate => duplicateRate;
        public float HeartbeatIntervalSeconds => heartbeatIntervalSeconds;
        public float HeartbeatTimeoutSeconds => heartbeatTimeoutSeconds;

        public GameRulesConfig CreateGameRulesConfig() => new GameRulesConfig(boardSize, shipLengths);
    }
}
