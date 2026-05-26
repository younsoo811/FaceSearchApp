using MQTTnet;
using MQTTnet.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FaceSearchApp.Services
{
    public class MqttClientService : IDisposable
    {
        private IMqttClient? _client;
        private readonly MqttFactory _factory = new();
        private string _currentSubTopic = string.Empty;

        public event Action<string, string>? MessageReceived; // (topic, payload)
        public bool IsConnected => _client?.IsConnected ?? false;

        public async Task<(bool Success, string Error)> ConnectAsync(
            string broker, int port,
            string? username = null, string? password = null,
            string clientId = "vlm-wpf")
        {
            try
            {
                if (_client?.IsConnected == true)
                    await DisconnectAsync();

                _client = _factory.CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += OnMessageReceived;

                var optionsBuilder = new MqttClientOptionsBuilder()
                    .WithTcpServer(broker, port)
                    .WithClientId($"{clientId}_{Guid.NewGuid():N}"[..20])
                    .WithCleanSession(true)
                    .WithTimeout(TimeSpan.FromSeconds(10));

                // 인증 정보가 있을 때만 추가
                if (!string.IsNullOrWhiteSpace(username))
                    optionsBuilder.WithCredentials(username, password ?? string.Empty);

                await _client.ConnectAsync(optionsBuilder.Build(), CancellationToken.None);
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (_client?.IsConnected == true)
                {
                    await _client.DisconnectAsync(
                        new MqttClientDisconnectOptionsBuilder().Build(),
                        CancellationToken.None);
                }
            }
            catch { /* ignore */ }
        }

        public async Task SubscribeAsync(string topic)
        {
            if (_client?.IsConnected != true || string.IsNullOrWhiteSpace(topic)) return;
            _currentSubTopic = topic;

            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(topic))
                .Build();

            await _client.SubscribeAsync(subscribeOptions, CancellationToken.None);
        }

        public async Task UnsubscribeAsync(string topic)
        {
            if (_client?.IsConnected != true || string.IsNullOrWhiteSpace(topic)) return;

            var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build();

            await _client.UnsubscribeAsync(unsubscribeOptions, CancellationToken.None);
        }

        public async Task PublishAsync(string topic, string payload)
        {
            if (_client?.IsConnected != true) return;

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.PublishAsync(message, CancellationToken.None);
        }

        private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            MessageReceived?.Invoke(topic, payload);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
