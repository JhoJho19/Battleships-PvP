using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Battleships.Presentation
{
    public sealed class ClientDebugView : MonoBehaviour
    {
        private static readonly Color PositiveStatusColor = new Color32(0, 255, 28, 255);

        [Header("Runtime")]
        [SerializeField] private TMP_Text endpoint;
        [SerializeField] private TMP_Text connected;
        [SerializeField] private TMP_Text lastRequestId;
        [SerializeField] private TMP_Text recentEvents;

        [Header("Network settings")]
        [SerializeField] private TMP_InputField latency;
        [SerializeField] private TMP_InputField jitter;
        [SerializeField] private TMP_InputField loss;
        [SerializeField] private TMP_InputField duplicate;
        [SerializeField] private Toggle networkLog;
        [SerializeField] private Button disconnect;
        [SerializeField] private Button connect;
        [SerializeField] private Button recreateClient;

        private ClientDebugController controller;

        public void Bind(ClientDebugController debugController)
        {
            Unbind();
            controller = debugController ?? throw new ArgumentNullException(nameof(debugController));
            controller.Changed += Render;
            latency.onEndEdit.AddListener(OnLatencyChanged);
            jitter.onEndEdit.AddListener(OnJitterChanged);
            loss.onEndEdit.AddListener(OnLossChanged);
            duplicate.onEndEdit.AddListener(OnDuplicateChanged);
            networkLog.onValueChanged.AddListener(OnNetworkLogChanged);
            disconnect.onClick.AddListener(OnDisconnect);
            connect.onClick.AddListener(OnConnect);
            recreateClient.onClick.AddListener(OnRecreateClient);
            Render();
        }

        public void Unbind()
        {
            if (controller != null) controller.Changed -= Render;
            latency?.onEndEdit.RemoveListener(OnLatencyChanged);
            jitter?.onEndEdit.RemoveListener(OnJitterChanged);
            loss?.onEndEdit.RemoveListener(OnLossChanged);
            duplicate?.onEndEdit.RemoveListener(OnDuplicateChanged);
            networkLog?.onValueChanged.RemoveListener(OnNetworkLogChanged);
            disconnect?.onClick.RemoveListener(OnDisconnect);
            connect?.onClick.RemoveListener(OnConnect);
            recreateClient?.onClick.RemoveListener(OnRecreateClient);
            controller = null;
        }

        private void Render()
        {
            if (controller == null) return;
            var settings = controller.Settings;
            endpoint.text = controller.EndpointText;
            SetStatusText(connected, controller.ConnectedText, controller.IsDeliveryEnabled);
            lastRequestId.text = controller.LastRequestIdText;
            recentEvents.text = controller.RecentEventsText;
            latency.SetTextWithoutNotify(Format(settings.LatencyMilliseconds));
            jitter.SetTextWithoutNotify(Format(settings.JitterMilliseconds));
            loss.SetTextWithoutNotify(Format(settings.LossRate * 100d));
            duplicate.SetTextWithoutNotify(Format(settings.DuplicateRate * 100d));
            networkLog.SetIsOnWithoutNotify(controller.IsNetworkLogEnabled);
        }

        private void OnLatencyChanged(string value) => latency.SetTextWithoutNotify(controller.SetLatency(value));
        private void OnJitterChanged(string value) => jitter.SetTextWithoutNotify(controller.SetJitter(value));
        private void OnLossChanged(string value) => loss.SetTextWithoutNotify(controller.SetLossPercent(value));
        private void OnDuplicateChanged(string value) =>
            duplicate.SetTextWithoutNotify(controller.SetDuplicatePercent(value));
        private void OnNetworkLogChanged(bool enabled) => controller.SetNetworkLogEnabled(enabled);
        private void OnDisconnect() => controller.Disconnect();
        private void OnConnect() => controller.Connect();
        private void OnRecreateClient() => controller.RequestRecreate();
        private void OnDestroy() => Unbind();

        private static string Format(double value) => value.ToString("0.##",
            System.Globalization.CultureInfo.InvariantCulture);

        private static void SetStatusText(TMP_Text target, string value, bool positive)
        {
            target.text = value;
            target.color = positive ? PositiveStatusColor : Color.red;
        }
    }
}
