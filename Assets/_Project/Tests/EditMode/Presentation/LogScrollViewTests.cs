using System.Linq;
using System.Reflection;
using Battleships.Presentation;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Battleships.Tests.Presentation
{
    public sealed class LogScrollViewTests
    {
        private GameObject root;
        private LogScrollView view;
        private ScrollRect scroll;
        private RectTransform viewport;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Log test canvas", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image),
                typeof(Mask), typeof(ScrollRect), typeof(LogScrollView));
            viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(root.transform, false);
            viewport.sizeDelta = new Vector2(400, 100);
            var contentObject = new GameObject("Content", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            var content = contentObject.GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var text = contentObject.GetComponent<TMP_Text>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = 20;
            text.alignment = TextAlignmentOptions.TopLeft;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewportObject.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            view = viewportObject.GetComponent<LogScrollView>();
            SetField("logText", text);
            SetField("scrollRect", scroll);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void ContentGrowsAndScrollsWithoutResizingViewportAndFollowsOnlyAtBottom()
        {
            var originalRect = viewport.rect;
            view.Render(Lines(30));
            Assert.That(scroll.content.rect.height, Is.GreaterThan(viewport.rect.height));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(0).Within(0.001f));

            scroll.OnScroll(new PointerEventData(null) { scrollDelta = new Vector2(0, 3) });
            var position = scroll.content.anchoredPosition;
            Assert.That(scroll.verticalNormalizedPosition, Is.GreaterThan(0f));
            view.Render(Lines(31));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(position.y).Within(0.01f));
            view.Render(Lines(31));
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(position.y).Within(0.01f));
            Assert.That(viewport.rect, Is.EqualTo(originalRect));

            scroll.verticalNormalizedPosition = 0;
            view.Render(Lines(32));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(0).Within(0.001f));
        }

        [Test]
        public void RebindingCanResetTheScrollPositionForANewClientHistory()
        {
            view.Render(Lines(30));
            scroll.verticalNormalizedPosition = 1;
            view.Render(Lines(20), resetScroll: true);
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(0).Within(0.001f));
        }

        private static string Lines(int count) =>
            string.Join("\n", Enumerable.Range(0, count).Select(i => $"[14:07:03] Event {i}"));

        private void SetField(string name, object value) =>
            typeof(LogScrollView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(view, value);
    }
}
