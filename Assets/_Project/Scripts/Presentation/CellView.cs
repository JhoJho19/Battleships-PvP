using System;
using UnityEngine;
using UnityEngine.UI;

namespace Battleships.Presentation
{
    public enum CellPresentationState
    {
        Unknown,
        OwnShip,
        Miss,
        Hit,
        Sunk,
        Pending
    }

    [Serializable]
    public sealed class CellSpriteSet
    {
        public Sprite Unknown;
        public Sprite OwnShip;
        public Sprite Miss;
        public Sprite Hit;
        public Sprite Sunk;
        public Sprite Pending;
        public Sprite Hover;
        public Sprite Pressed;

        public Sprite For(CellPresentationState state)
        {
            switch (state)
            {
                case CellPresentationState.Unknown: return Unknown;
                case CellPresentationState.OwnShip: return OwnShip;
                case CellPresentationState.Miss: return Miss;
                case CellPresentationState.Hit: return Hit;
                case CellPresentationState.Sunk: return Sunk;
                case CellPresentationState.Pending: return Pending;
                default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }
    }

    public sealed class CellView : MonoBehaviour
    {
        private Image image;
        private Button button;
        private CellSpriteSet sprites;
        private Action clicked;
        private bool acceptsInput;

        public void Initialize(Image targetImage, Button targetButton, CellSpriteSet spriteSet,
            bool interactive, Action onClick)
        {
            image = targetImage ?? throw new ArgumentNullException(nameof(targetImage));
            button = targetButton;
            sprites = spriteSet ?? throw new ArgumentNullException(nameof(spriteSet));
            acceptsInput = interactive;
            clicked = onClick;

            if (button != null)
            {
                button.targetGraphic = image;
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = sprites.Hover,
                    pressedSprite = sprites.Pressed,
                    selectedSprite = sprites.Hover
                };
                button.onClick.AddListener(HandleClick);
            }
        }

        public void SetState(CellPresentationState state)
        {
            image.overrideSprite = null;
            image.sprite = sprites.For(state);
            if (button != null)
                button.interactable = acceptsInput && state == CellPresentationState.Unknown;
        }

        private void HandleClick() => clicked?.Invoke();

        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClick);
            clicked = null;
        }
    }
}
