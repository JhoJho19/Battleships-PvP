using System;
using Battleships.Client;
using Battleships.Protocol;
using UnityEngine;
using UnityEngine.UI;

namespace Battleships.Presentation
{
    public sealed class BoardView : MonoBehaviour
    {
        [SerializeField] private bool opponentBoard;
        [SerializeField] private float spacing = -5f;
        [SerializeField] private float cellSize = 65f;
        [SerializeField] private CellSpriteSet sprites;

        private CellView[,] cells;
        private Action<ClientPosition> cellClicked;

        public void Initialize(int boardSize, Action<ClientPosition> onCellClicked)
        {
            if (boardSize <= 0) throw new ArgumentOutOfRangeException(nameof(boardSize));
            cellClicked = onCellClicked;
            if (cells != null && cells.GetLength(0) == boardSize && cells.GetLength(1) == boardSize)
                return;
            ClearRuntimeCells();

            var rect = (RectTransform)transform;
            var grid = GetComponent<GridLayoutGroup>() ?? gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = boardSize;
            grid.spacing = new Vector2(spacing, spacing);
            grid.childAlignment = TextAnchor.MiddleCenter;
            var available = Mathf.Min(rect.rect.width, rect.rect.height) - spacing * (boardSize - 1);
            grid.cellSize = new Vector2(cellSize, cellSize);

            cells = new CellView[boardSize, boardSize];
            for (var y = 0; y < boardSize; y++)
            for (var x = 0; x < boardSize; x++)
            {
                var position = new ClientPosition(x, y);
                var cellObject = new GameObject($"Cell {position}", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                cellObject.layer = gameObject.layer;
                cellObject.transform.SetParent(transform, false);
                var image = cellObject.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = opponentBoard;
                var button = opponentBoard ? cellObject.AddComponent<Button>() : null;
                var view = cellObject.AddComponent<CellView>();
                view.Initialize(image, button, sprites, opponentBoard, () => cellClicked?.Invoke(position));
                view.SetState(CellPresentationState.Unknown);
                cells[x, y] = view;
            }
        }

        public void Render(ClientState state)
        {
            if (state == null || cells == null) return;
            for (var y = 0; y < cells.GetLength(1); y++)
            for (var x = 0; x < cells.GetLength(0); x++)
            {
                var position = new ClientPosition(x, y);
                cells[x, y].SetState(opponentBoard
                    ? GetOpponentState(state, position)
                    : GetOwnState(state, position));
            }
        }

        private static CellPresentationState GetOwnState(ClientState state, ClientPosition position)
        {
            if (!state.OwnBoard.TryGetValue(position, out var cell)) return CellPresentationState.Unknown;
            if (cell.ShotResult.HasValue) return FromShot(cell.ShotResult.Value);
            return cell.HasShip ? CellPresentationState.OwnShip : CellPresentationState.Unknown;
        }

        private static CellPresentationState GetOpponentState(ClientState state, ClientPosition position)
        {
            if (state.PendingShot != null && state.PendingShot.Target.Equals(position))
                return CellPresentationState.Pending;
            return state.OpponentShots.TryGetValue(position, out var result)
                ? FromShot(result)
                : CellPresentationState.Unknown;
        }

        private static CellPresentationState FromShot(ShotResultCode result)
        {
            switch (result)
            {
                case ShotResultCode.Miss: return CellPresentationState.Miss;
                case ShotResultCode.Hit: return CellPresentationState.Hit;
                case ShotResultCode.Sunk: return CellPresentationState.Sunk;
                default: throw new ArgumentOutOfRangeException(nameof(result), result, null);
            }
        }

        private void ClearRuntimeCells()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
        }
    }
}
