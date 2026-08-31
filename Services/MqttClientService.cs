using MQTTnet;
using MQTTnet.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FaceSearchApp.Services
{
    public enum MqttConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting
    }

    public sealed class MqttClientService : IDisposable
    {
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

        private readonly IMqttClient _client;
        private readonly SemaphoreSlim _connectionGate = new(1, 1);
        private readonly object _stateLock = new();
        private readonly HashSet<string> _subscriptions = new(StringComparer.Ordinal);

        private CancellationTokenSource _connectionCts = new();
        private MqttClientOptions? _options;
        private Task? _reconnectTask;
        private volatile bool _manualDisconnect = true;
        private bool _disposed;

        public MqttClientService()
        {
            _client = new MqttFactory().CreateMqttClient();
            _client.ApplicationMessageReceivedAsync += OnMessageReceived;
            _client.DisconnectedAsync += OnDisconnectedAsync;
        }

        public event Action<string, string>? MessageReceived;
        public event Action<MqttConnectionState, string?>? ConnectionStateChanged;

        public bool IsConnected => _client.IsConnected;
        public bool IsConnectionRequested => !_manualDisconnect && !_disposed;

        public async Task<(bool Success, string Error)> ConnectAsync(
            string broker, int port,
            string? username = null, string? password = null,
            string clientId = "vlm-wpf")
        {
            ThrowIfDisposed();

            // 진행 중인 재연결을 중단하고 최신 연결 정보로 즉시 한 번 시도한다.
            _manualDisconnect = true;
            CancelConnectionAttempt();

            await _connectionGate.WaitAsync().ConfigureAwait(false);
            var success = false;
            var error = string.Empty;

            try
            {
                ResetConnectionCancellation();
                _manualDisconnect = false;

                var optionsBuilder = new MqttClientOptionsBuilder()
                    .WithTcpServer(broker, port)
                    .WithClientId($"{clientId}_{Guid.NewGuid():N}"[..20])
                    .WithCleanSession(true)
                    .WithTimeout(TimeSpan.FromSeconds(10));

                if (!string.IsNullOrWhiteSpace(username))
                    optionsBuilder.WithCredentials(username, password ?? string.Empty);

                _options = optionsBuilder.Build();

                if (_client.IsConnected)
                    return (true, string.Empty);

                RaiseConnectionStateChanged(MqttConnectionState.Connecting, null);
                await ConnectAndRestoreSubscriptionsAsync(_connectionCts.Token).ConfigureAwait(false);
                success = true;
            }
            catch (OperationCanceledException) when (_manualDisconnect || _disposed)
            {
                error = "연결 시도가 취소되었습니다.";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                _connectionGate.Release();
            }

            if (!success && IsConnectionRequested)
            {
                RaiseConnectionStateChanged(MqttConnectionState.Reconnecting, error);
                StartReconnectLoop();
            }

            return (success, error);
        }

        public async Task DisconnectAsync()
        {
            if (_disposed)
                return;

            _manualDisconnect = true;
            CancelConnectionAttempt();

            await _connectionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_client.IsConnected)
                {
                    await _client.DisconnectAsync(
                        new MqttClientDisconnectOptionsBuilder().Build(),
                        CancellationToken.None).ConfigureAwait(false);
                }

                RaiseConnectionStateChanged(MqttConnectionState.Disconnected, null);
            }
            catch (Exception ex)
            {
                RaiseConnectionStateChanged(MqttConnectionState.Disconnected, ex.Message);
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        public async Task SubscribeAsync(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic) || _disposed)
                return;

            lock (_stateLock)
                _subscriptions.Add(topic);

            if (!_client.IsConnected)
                return;

            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(topic))
                .Build();

            await _client.SubscribeAsync(subscribeOptions, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task UnsubscribeAsync(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic) || _disposed)
                return;

            lock (_stateLock)
                _subscriptions.Remove(topic);

            if (!_client.IsConnected)
                return;

            var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build();

            await _client.UnsubscribeAsync(unsubscribeOptions, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task PublishAsync(string topic, string payload)
        {
            if (!_client.IsConnected || _disposed)
            {
                if (IsConnectionRequested)
                    StartReconnectLoop();
                return;
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.PublishAsync(message, CancellationToken.None).ConfigureAwait(false);
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            if (_manualDisconnect || _disposed)
            {
                RaiseConnectionStateChanged(MqttConnectionState.Disconnected, e.Exception?.Message);
                return Task.CompletedTask;
            }

            var detail = e.Exception?.Message;
            if (string.IsNullOrWhiteSpace(detail))
                detail = e.ReasonString;

            RaiseConnectionStateChanged(MqttConnectionState.Reconnecting, detail);
            StartReconnectLoop();
            return Task.CompletedTask;
        }

        private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            MessageReceived?.Invoke(topic, payload);
            return Task.CompletedTask;
        }

        private void StartReconnectLoop()
        {
            lock (_stateLock)
            {
                if (_disposed || _manualDisconnect || _options is null || _client.IsConnected)
                    return;

                if (_reconnectTask is { IsCompleted: false })
                    return;

                var cancellationToken = _connectionCts.Token;
                _reconnectTask = Task.Run(() => ReconnectLoopAsync(cancellationToken));
            }
        }

        private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && IsConnectionRequested)
            {
                try
                {
                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                    await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

                    try
                    {
                        if (_client.IsConnected || _options is null || !IsConnectionRequested)
                            return;

                        RaiseConnectionStateChanged(MqttConnectionState.Reconnecting, null);
                        await ConnectAndRestoreSubscriptionsAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    finally
                    {
                        _connectionGate.Release();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    RaiseConnectionStateChanged(MqttConnectionState.Reconnecting, ex.Message);
                }
            }
        }

        private async Task ConnectAndRestoreSubscriptionsAsync(CancellationToken cancellationToken)
        {
            if (_options is null)
                throw new InvalidOperationException("MQTT 연결 정보가 설정되지 않았습니다.");

            await _client.ConnectAsync(_options, cancellationToken).ConfigureAwait(false);

            try
            {
                await RestoreSubscriptionsAsync(cancellationToken).ConfigureAwait(false);
                RaiseConnectionStateChanged(MqttConnectionState.Connected, null);
            }
            catch
            {
                if (_client.IsConnected)
                {
                    await _client.DisconnectAsync(
                        new MqttClientDisconnectOptionsBuilder().Build(),
                        CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }
        }

        private async Task RestoreSubscriptionsAsync(CancellationToken cancellationToken)
        {
            string[] topics;
            lock (_stateLock)
            {
                topics = new string[_subscriptions.Count];
                _subscriptions.CopyTo(topics);
            }

            foreach (var topic in topics)
            {
                var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(topic))
                    .Build();

                await _client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);
            }
        }

        private void ResetConnectionCancellation()
        {
            lock (_stateLock)
            {
                _connectionCts.Dispose();
                _connectionCts = new CancellationTokenSource();
                _reconnectTask = null;
            }
        }

        private void CancelConnectionAttempt()
        {
            lock (_stateLock)
            {
                if (!_connectionCts.IsCancellationRequested)
                    _connectionCts.Cancel();
            }
        }

        private void RaiseConnectionStateChanged(MqttConnectionState state, string? detail)
            => ConnectionStateChanged?.Invoke(state, detail);

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _manualDisconnect = true;
            CancelConnectionAttempt();

            _client.ApplicationMessageReceivedAsync -= OnMessageReceived;
            _client.DisconnectedAsync -= OnDisconnectedAsync;
            _client.Dispose();
            _connectionCts.Dispose();
        }
    }
}
