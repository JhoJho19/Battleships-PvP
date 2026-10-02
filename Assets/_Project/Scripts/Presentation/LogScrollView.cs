using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Battleships.Presentation
{
    public sealed class LogScrollView : MonoBehaviour
    {
        [SerializeField] private TMP_Text logText;
        [SerializeField] private ScrollRect scrollRect;

        public void Render(string value, bool resetScroll = false)
        {
            if (!resetScroll && logText.text == value) return;

            Canvas.ForceUpdateCanvases();
            var content = scrollRect.content;
            var followNewest = resetScroll || content.rect.height <= scrollRect.viewport.rect.height ||
                               scrollRect.verticalNormalizedPosition * Mathf.Max(0,
                                   content.rect.height - scrollRect.viewport.rect.height) <= 1f;
            var previousPosition = content.anchoredPosition;
            logText.text = value;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();
            scrollRect.StopMovement();

            if (followNewest) scrollRect.verticalNormalizedPosition = 0;
            else content.anchoredPosition = previousPosition;
        }
    }
}
