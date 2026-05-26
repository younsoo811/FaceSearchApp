using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceSearchApp.Models;
using FaceSearchApp.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FaceSearchApp.ViewModels
{
    public enum AnalysisState { Idle, Analyzing, Complete }

    public partial class VlmViewModel : ObservableObject, IDisposable
    {
        private readonly MqttClientService _mqtt = new();
        private AnalysisState _state = AnalysisState.Idle;
        private string? _selectedImagePath;

        // ── MQTT 설정 ──────────────────────────────────────────────
        [ObservableProperty]
        private string _broker = "192.168.0.178";

        [ObservableProperty]
        private int _port = 1883;

        [ObservableProperty]
        private string _pubTopic = "vlm/request";

        [ObservableProperty]
        private string _subTopic = "vlm/response";

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _connectionStatus = "연결되지 않음";

        [ObservableProperty]
        private string _connectButtonText = "연결";

        [ObservableProperty]
        private string _username = "seo_ai";

        [ObservableProperty]
        private string _password = "qrqvud3";

        [ObservableProperty]
        private bool _useCredentials = true;

        // ── 이미지 선택 ────────────────────────────────────────────
        [ObservableProperty]
        private BitmapImage? _selectedImage;

        [ObservableProperty]
        private string _selectedFileName = "선택된 파일 없음";

        // ── 분석 결과 (우측 패널) ──────────────────────────────────
        [ObservableProperty]
        private BitmapImage? _analysisImage;

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private string _resultText = string.Empty;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        // ── 상태 파생 프로퍼티 ─────────────────────────────────────
        public bool IsIdle => _state == AnalysisState.Idle;
        public bool IsAnalyzing => _state == AnalysisState.Analyzing;
        public bool HasResult => _state == AnalysisState.Complete;
        public bool IsActiveAnalysis => !IsIdle;

        private void SetState(AnalysisState state)
        {
            _state = state;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsAnalyzing));
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(IsActiveAnalysis));
        }

        public VlmViewModel()
        {
            _mqtt.MessageReceived += OnMqttMessageReceived;
        }

        // ── Commands ───────────────────────────────────────────────

        [RelayCommand]
        private async Task ToggleConnectionAsync()
        {
            if (_mqtt.IsConnected)
            {
                await _mqtt.UnsubscribeAsync(SubTopic);
                await _mqtt.DisconnectAsync();
                IsConnected = false;
                ConnectionStatus = "연결 해제됨";
                ConnectButtonText = "연결";
                StatusMessage = "MQTT 연결이 해제되었습니다.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Broker))
            {
                StatusMessage = "브로커 주소를 입력하세요.";
                return;
            }

            ConnectionStatus = "연결 중...";
            StatusMessage = string.Empty;

            var (success, error) = await _mqtt.ConnectAsync(
                Broker, Port,
                username: UseCredentials ? Username : null,
                password: UseCredentials ? Password : null);
            if (success)
            {
                IsConnected = true;
                ConnectionStatus = $"연결됨 · {Broker}:{Port}";
                ConnectButtonText = "연결 해제";
                StatusMessage = "MQTT 연결 성공.";

                if (!string.IsNullOrWhiteSpace(SubTopic))
                {
                    await _mqtt.SubscribeAsync(SubTopic);
                    StatusMessage = $"구독 중: {SubTopic}";
                }
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "연결 실패";
                ConnectButtonText = "연결";
                StatusMessage = $"연결 실패: {error}";
            }
        }

        [RelayCommand]
        private void SelectImage()
        {
            var dialog = new OpenFileDialog
            {
                Title = "분석할 이미지 선택",
                Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|모든 파일|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            _selectedImagePath = dialog.FileName;
            SelectedFileName = Path.GetFileName(_selectedImagePath);
            SelectedImage = LoadBitmapImage(_selectedImagePath);
            StatusMessage = $"이미지 선택됨: {SelectedFileName}";
        }

        [RelayCommand]
        private async Task AnalyzeAsync()
        {
            if (!_mqtt.IsConnected)
            {
                StatusMessage = "먼저 MQTT 브로커에 연결하세요.";
                return;
            }
            if (_selectedImagePath is null)
            {
                StatusMessage = "분석할 이미지를 선택하세요.";
                return;
            }
            if (string.IsNullOrWhiteSpace(PubTopic))
            {
                StatusMessage = "Publish 토픽을 입력하세요.";
                return;
            }

            try
            {
                // 이미지 → Base64
                var bytes = await File.ReadAllBytesAsync(_selectedImagePath);
                var base64 = Convert.ToBase64String(bytes);
                var payload = JsonSerializer.Serialize(new { @base64 = base64 });

                // 우측 패널에 이미지 세팅 + 로딩 상태
                AnalysisImage = SelectedImage;
                Description = string.Empty;
                ResultText = string.Empty;
                SetState(AnalysisState.Analyzing);

                // MQTT Publish
                await _mqtt.PublishAsync(PubTopic, payload);
                StatusMessage = $"분석 요청 전송 완료 → [{PubTopic}]";
            }
            catch (Exception ex)
            {
                StatusMessage = $"전송 오류: {ex.Message}";
                SetState(AnalysisState.Idle);
            }
        }

        // ── MQTT 수신 ──────────────────────────────────────────────

        private void OnMqttMessageReceived(string topic, string payload)
        {
            if (!topic.Equals(SubTopic, StringComparison.OrdinalIgnoreCase)) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var response = JsonSerializer.Deserialize<VlmResponse>(payload);
                    if (response is null)
                    {
                        StatusMessage = "응답 파싱 실패: null 응답";
                        return;
                    }

                    Description = response.Desc ?? string.Empty;
                    ResultText = response.Result ?? string.Empty;
                    SetState(AnalysisState.Complete);
                    StatusMessage = $"분석 결과 수신 완료 ← [{topic}]";
                }
                catch (JsonException ex)
                {
                    StatusMessage = $"JSON 파싱 오류: {ex.Message}";
                    SetState(AnalysisState.Complete); // 로딩 해제
                }
            });
        }

        // ── 헬퍼 ──────────────────────────────────────────────────

        private static BitmapImage LoadBitmapImage(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        public void Dispose()
        {
            _mqtt.MessageReceived -= OnMqttMessageReceived;
            _mqtt.Dispose();
        }
    }
}
