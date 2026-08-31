using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceSearchApp.Models;
using FaceSearchApp.Services;
using FaceSearchApp.Views;
using Microsoft.Win32;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FaceSearchApp.ViewModels
{
    public partial class EventTypeItem : ObservableObject
    {
        public string Key { get; set; } = string.Empty;     // 실제 값
        public string Display { get; set; } = string.Empty; // 화면 표시
        [ObservableProperty] private bool _isLiveEnabled = true;
    }

    public partial class VlmViewModel : ObservableObject, IDisposable
    {
        private readonly ISnackbarService _snackbarService;
        private readonly IContentDialogService _dialogService;

        private CancellationTokenSource? _analysisLoopCts;

        private readonly string _configPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        private readonly MqttClientService _mqtt;
        private Guid? _currentAnalysisId;

        // ── MQTT 설정 ──────────────────────────────────────────────
        [ObservableProperty] private string _broker = "192.168.0.53";
        [ObservableProperty] private int _port = 1883;
        [ObservableProperty] private string _pubTopic = "vlm/request";
        [ObservableProperty] private string _subTopic = "vlm/response";
        private string _eventTopic = "infer_app/events/#";
        [ObservableProperty] private string _username = "seo_ai";
        [ObservableProperty] private string _password = "qrqvud3";
        [ObservableProperty] private bool _useCredentials = true;
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private string _connectionStatus = "연결되지 않음";
        [ObservableProperty] private string _connectButtonText = "연결";

        private int _delayTime = 5000; // 기본 5초

        // ── 이미지 선택 ────────────────────────────────────────────
        [ObservableProperty] private BitmapImage? _queryImage;
        [ObservableProperty] private string _imageInfo = string.Empty;
        private string? _selectedImagePath;

        [ObservableProperty]
        private bool _isFireMode = true;
        [ObservableProperty]
        private bool _isContinuousMode;
        private bool _isContinuousModeBackup;

        public ObservableCollection<EventTypeItem> EventTypes { get; } = new();
        public ObservableCollection<EventTypeItem> RealtimeEventExcludeTypes { get; } = new();

        [ObservableProperty]
        private EventTypeItem? _selectedEventType;

        public ObservableCollection<EventTypeItem> LabelTypes { get; } =
            [
            new() {Key = "", Display = "전체 표시"},
            new() {Key = "0", Display = "오경보"},
            new() {Key = "1", Display = "정상"},
            ];
        [ObservableProperty]
        private EventTypeItem? _selectedLabelType;

        [ObservableProperty]
        private bool _isLiveEventMode = false;
        [ObservableProperty]
        private bool _showNegativeConfidenceRealtimeEvents;
        [ObservableProperty]
        private bool _useRealtimeAnalysisDelay = true;
        [ObservableProperty]
        private bool _skipRealtimeAnalysisWhenBusy = true;

        // ── 분석 상태 ──────────────────────────────────────────────
        [ObservableProperty] private bool _isAnalyzing;
        [ObservableProperty] private string _statusMessage = "VLM 서버 연결 상태를 확인해주세요.";

        // ── 분석 히스토리 ──────────────────────────────────────────
        private readonly ObservableCollection<AnalysisItem> _allHistory = new();
        public ICollectionView AnalysisHistory { get; }
        public ObservableCollection<RealtimeEventItem> RealtimeEvents { get; } = new();

        public VlmViewModel(
            ISnackbarService snackbarService,
            IContentDialogService dialogService,
            MqttClientService mqtt)
        {
            _snackbarService = snackbarService;
            _dialogService = dialogService;
            _mqtt = mqtt;

            // CollectionView 필터 설정
            AnalysisHistory = CollectionViewSource.GetDefaultView(_allHistory);
            AnalysisHistory.Filter = FilterItem;

            LoadVlmSettings();
            EnsureDefaultEventTypes();
            EnsureDefaultRealtimeEventExcludeTypes();
            _selectedEventType = EventTypes.FirstOrDefault();
            _selectedLabelType = LabelTypes.FirstOrDefault();

            _mqtt.MessageReceived += OnMqttMessageReceived;
            _mqtt.ConnectionStateChanged += OnMqttConnectionStateChanged;
        }

        private bool FilterItem(object obj)
        {
            if (obj is not AnalysisItem item) return false;

            if (item.IsRealtimeSelected) return true;

            // 분석 중인 항목은 필터 무관하게 항상 표시
            if (item.IsAnalyzing) return true;

            var key = SelectedLabelType?.Key ?? string.Empty;

            // 전체 표시 (key가 비어있는 경우)
            if (string.IsNullOrEmpty(key)) return true;

            return item.Decision == key;
        }

        private bool _isRevertingLiveEventMode;

        async partial void OnIsLiveEventModeChanged(bool oldValue, bool newValue)
        {
            // 토글 되돌리기(연결 실패 시) 과정에서 재귀적으로 다시 들어오는 것을 방지
            if (_isRevertingLiveEventMode) return;

            if (newValue)
            {
                var connected = await EnsureConnectedAsync();
                if (!connected)
                {
                    StatusMessage = "서버 연결 실패 - 서버 상태를 확인해주세요.";
                    _snackbarService.Show(
                        "실시간 분석 시작 실패",
                        "서버에 연결할 수 없습니다. 서버 상태를 확인해주세요.",
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24),
                        TimeSpan.FromSeconds(3));

                    _isRevertingLiveEventMode = true;
                    IsLiveEventMode = false;
                    _isRevertingLiveEventMode = false;
                    return;
                }
            }

            await ManageLiveEventTopic(newValue);
        }
        private async Task ManageLiveEventTopic(bool subscribe)
        {
            if (string.IsNullOrEmpty(_eventTopic)) return;
            if (subscribe)
            {
                await _mqtt.SubscribeAsync(_eventTopic);
                StatusMessage += $"구독 중: {_eventTopic}";
            }
            else
            {
                await _mqtt.UnsubscribeAsync(_eventTopic);
                StatusMessage = "실시간 분석이 해제되었습니다.";
            }
        }

        partial void OnSelectedLabelTypeChanged(EventTypeItem? value)
        {
            AnalysisHistory.Refresh();
        }

        #region MQTT 설정 로드/저장
        private void LoadVlmSettings()
        {
            try
            {
                if (!File.Exists(_configPath))
                    return;

                var json = File.ReadAllText(_configPath);

                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("VlmSettings", out var vlm))
                    return;

                Broker = vlm.GetProperty("Broker").GetString() ?? Broker;
                Port = vlm.GetProperty("Port").GetInt32();

                PubTopic = vlm.GetProperty("PubTopic").GetString() ?? PubTopic;
                SubTopic = vlm.GetProperty("SubTopic").GetString() ?? SubTopic;
                _eventTopic = vlm.GetProperty("EventTopic").GetString() ?? _eventTopic;

                Username = vlm.GetProperty("Username").GetString() ?? Username;
                Password = vlm.GetProperty("Password").GetString() ?? Password;

                UseCredentials = vlm.GetProperty("UseCredentials").GetBoolean();

                if (vlm.TryGetProperty("SkipNoResponseTimeoutSeconds", out var timeoutProp)
                    && timeoutProp.TryGetInt32(out var timeoutValue) && timeoutValue >= 1)
                {
                    SkipNoResponseTimeoutSeconds = timeoutValue;
                }

                if (vlm.TryGetProperty("ShowNegativeConfidenceRealtimeEvents", out var negativeConfidenceProp))
                {
                    ShowNegativeConfidenceRealtimeEvents = negativeConfidenceProp.ValueKind == JsonValueKind.True;
                }

                if (vlm.TryGetProperty("UseRealtimeAnalysisDelay", out var useDelayProp))
                {
                    UseRealtimeAnalysisDelay = useDelayProp.ValueKind != JsonValueKind.False;
                }

                if (vlm.TryGetProperty("SkipRealtimeAnalysisWhenBusy", out var skipBusyProp))
                {
                    SkipRealtimeAnalysisWhenBusy = skipBusyProp.ValueKind != JsonValueKind.False;
                }

                if (vlm.TryGetProperty("DelayTime", out var delayProp) &&
                    delayProp.TryGetInt32(out var delayValue) && delayValue >= 0)
                {
                    _delayTime = delayValue;
                }

                LoadEventTypes(vlm);
                LoadRealtimeEventExcludeTypes(vlm);
            }
            catch
            {
                // 실패 시 기본값 유지
            }
        }

        private void LoadEventTypes(JsonElement vlm)
        {
            if (!vlm.TryGetProperty("EventTypes", out var eventTypesProp) ||
                eventTypesProp.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var items = new List<EventTypeItem>();

            foreach (var item in eventTypesProp.EnumerateArray())
            {
                EventTypeItem? eventType = item.ValueKind switch
                {
                    JsonValueKind.String => CreateEventTypeItem(item.GetString(), null, true),
                    JsonValueKind.Object => CreateEventTypeItem(
                        item.TryGetProperty("Key", out var keyProp) ? keyProp.GetString() : null,
                        item.TryGetProperty("Display", out var displayProp) ? displayProp.GetString() : null,
                        !item.TryGetProperty("IsLiveEnabled", out var liveProp) ||
                        liveProp.ValueKind != JsonValueKind.False),
                    _ => null
                };

                if (eventType is null ||
                    items.Any(x => x.Key.Equals(eventType.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                items.Add(eventType);
            }

            if (items.Count == 0)
                return;

            EventTypes.Clear();
            foreach (var item in items)
                EventTypes.Add(item);
        }

        private static EventTypeItem? CreateEventTypeItem(string? key, string? display, bool isLiveEnabled)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var normalizedKey = key.Trim();
            return new EventTypeItem
            {
                Key = normalizedKey,
                Display = string.IsNullOrWhiteSpace(display) ? normalizedKey : display.Trim(),
                IsLiveEnabled = isLiveEnabled
            };
        }

        private void EnsureDefaultEventTypes()
        {
            if (EventTypes.Count > 0)
                return;

            EventTypes.Add(new EventTypeItem { Key = "Fire", Display = "화재" });
            EventTypes.Add(new EventTypeItem { Key = "Fall", Display = "쓰러짐" });
            EventTypes.Add(new EventTypeItem { Key = "WorkAtHeight", Display = "고소작업" });
            EventTypes.Add(new EventTypeItem { Key = "ElectricalWork", Display = "전기작업" });
        }

        private void LoadRealtimeEventExcludeTypes(JsonElement vlm)
        {
            if (!vlm.TryGetProperty("RealtimeEventExcludeTypes", out var eventTypesProp) ||
                eventTypesProp.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var items = new List<EventTypeItem>();

            foreach (var item in eventTypesProp.EnumerateArray())
            {
                EventTypeItem? eventType = item.ValueKind switch
                {
                    JsonValueKind.String => CreateEventTypeItem(item.GetString(), null, true),
                    JsonValueKind.Object => CreateEventTypeItem(
                        item.TryGetProperty("Key", out var keyProp) ? keyProp.GetString() : null,
                        item.TryGetProperty("Display", out var displayProp) ? displayProp.GetString() : null,
                        true),
                    _ => null
                };

                if (eventType is null ||
                    items.Any(x => x.Key.Equals(eventType.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                items.Add(eventType);
            }

            if (items.Count == 0)
                return;

            RealtimeEventExcludeTypes.Clear();
            foreach (var item in items)
                RealtimeEventExcludeTypes.Add(item);
        }

        private void EnsureDefaultRealtimeEventExcludeTypes()
        {
            if (RealtimeEventExcludeTypes.Count > 0)
                return;

            RealtimeEventExcludeTypes.Add(new EventTypeItem { Key = "Detect", Display = "Detect" });
            RealtimeEventExcludeTypes.Add(new EventTypeItem { Key = "PersonTrack", Display = "PersonTrack" });
        }

        private void SaveVlmSettings()
        {
            try
            {
                JsonObject root;

                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                root["VlmSettings"] = new JsonObject
                {
                    ["Broker"] = Broker,
                    ["Port"] = Port,

                    ["PubTopic"] = PubTopic,
                    ["SubTopic"] = SubTopic,
                    ["EventTopic"] = _eventTopic,

                    ["Username"] = Username,
                    ["Password"] = Password,

                    ["UseCredentials"] = UseCredentials,

                    ["SkipNoResponseTimeoutSeconds"] = SkipNoResponseTimeoutSeconds,
                    ["ShowNegativeConfidenceRealtimeEvents"] = ShowNegativeConfidenceRealtimeEvents,
                    ["UseRealtimeAnalysisDelay"] = UseRealtimeAnalysisDelay,
                    ["SkipRealtimeAnalysisWhenBusy"] = SkipRealtimeAnalysisWhenBusy,

                    ["DelayTime"] = _delayTime,

                    ["EventTypes"] = new JsonArray(
                        EventTypes.Select(x => new JsonObject
                        {
                            ["Key"] = x.Key,
                            ["Display"] = x.Display,
                            ["IsLiveEnabled"] = x.IsLiveEnabled
                        }).ToArray<JsonNode?>()),

                    ["RealtimeEventExcludeTypes"] = new JsonArray(
                        RealtimeEventExcludeTypes.Select(x => new JsonObject
                        {
                            ["Key"] = x.Key,
                            ["Display"] = x.Display
                        }).ToArray<JsonNode?>())
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                File.WriteAllText(_configPath, root.ToJsonString(options));
            }
            catch
            {
                // 저장 실패 무시
            }
        }
        #endregion

        // ═══════════════════════════════════════════════════════════
        // MQTT 연결
        // ═══════════════════════════════════════════════════════════

        [RelayCommand]
        private async Task ToggleConnectionAsync()
        {
            if (_mqtt.IsConnectionRequested)
            {
                await _mqtt.UnsubscribeAsync(SubTopic);
                await _mqtt.UnsubscribeAsync(_eventTopic);
                await _mqtt.DisconnectAsync();
                IsConnected = false;
                ConnectionStatus = "연결 해제됨";
                ConnectButtonText = "연결";
                StatusMessage = "MQTT 연결이 해제되었습니다.";
                return;
            }

            var connected = await EnsureConnectedAsync();
            if (connected && !string.IsNullOrEmpty(_eventTopic) && IsLiveEventMode)
            {
                await _mqtt.SubscribeAsync(_eventTopic);
                StatusMessage += $"구독 중: {_eventTopic}";
            }
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (_mqtt.IsConnected)
                return true;

            if (string.IsNullOrWhiteSpace(Broker))
            {
                StatusMessage = "브로커 주소를 입력하세요.";
                return false;
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

                // 연결 설정 저장
                SaveVlmSettings();

                if (!string.IsNullOrWhiteSpace(SubTopic))
                {
                    await _mqtt.SubscribeAsync(SubTopic);
                    StatusMessage = $"구독 중: {SubTopic} ";
                }

                return true;
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = _mqtt.IsConnectionRequested ? "재연결 대기 중" : "연결 실패";
                ConnectButtonText = _mqtt.IsConnectionRequested ? "연결 취소" : "연결";
                StatusMessage = _mqtt.IsConnectionRequested
                    ? $"연결 실패: {error} · 5초 후 자동 재연결합니다."
                    : $"연결 실패: {error}";
                return false;
            }
        }

        private void OnMqttConnectionStateChanged(MqttConnectionState state, string? detail)
        {
            void UpdateState()
            {
                switch (state)
                {
                    case MqttConnectionState.Connecting:
                        IsConnected = false;
                        ConnectionStatus = "연결 중...";
                        ConnectButtonText = "연결 취소";
                        break;

                    case MqttConnectionState.Connected:
                        IsConnected = true;
                        ConnectionStatus = $"연결됨 · {Broker}:{Port}";
                        ConnectButtonText = "연결 해제";
                        StatusMessage = "MQTT 연결 성공.";
                        break;

                    case MqttConnectionState.Reconnecting:
                        IsConnected = false;
                        ConnectionStatus = "재연결 중...";
                        ConnectButtonText = "연결 취소";
                        StatusMessage = string.IsNullOrWhiteSpace(detail)
                            ? "MQTT 연결이 끊어져 재연결을 시도합니다."
                            : $"MQTT 연결 끊김: {detail} · 자동 재연결 중...";
                        break;

                    default:
                        IsConnected = false;
                        ConnectionStatus = "연결 해제됨";
                        ConnectButtonText = "연결";
                        StatusMessage = string.IsNullOrWhiteSpace(detail)
                            ? "MQTT 연결이 해제되었습니다."
                            : $"MQTT 연결 해제: {detail}";
                        break;
                }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                UpdateState();
            else
                dispatcher.BeginInvoke(UpdateState);
        }

        // ═══════════════════════════════════════════════════════════
        // 이미지 선택
        // ═══════════════════════════════════════════════════════════

        [RelayCommand]
        private void SelectImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "분석할 이미지 선택",
                Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|모든 파일|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
                LoadImageFromPath(dialog.FileName);
        }

        public void LoadImageFromPath(string path)
        {
            if (!File.Exists(path))
            {
                StatusMessage = "파일을 찾을 수 없습니다.";
                return;
            }

            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!validExtensions.Contains(ext))
            {
                StatusMessage = "지원하지 않는 이미지 형식입니다.";
                return;
            }

            try
            {
                _selectedImagePath = path;
                QueryImage = LoadBitmapImage(path);

                var fileInfo = new FileInfo(path);
                var sizeKB = fileInfo.Length / 1024.0;
                var sizeStr = sizeKB < 1024
                    ? $"{sizeKB:F1} KB"
                    : $"{sizeKB / 1024.0:F2} MB";

                ImageInfo = $"{Path.GetFileName(path)} · {QueryImage.PixelWidth}×{QueryImage.PixelHeight} · {sizeStr}";
                StatusMessage = "이미지가 선택되었습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"이미지 로드 실패: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ClearQuery()
        {
            QueryImage = null;
            ImageInfo = string.Empty;
            _selectedImagePath = null;
            StatusMessage = "이미지가 초기화되었습니다.";
        }

        // ═══════════════════════════════════════════════════════════
        // 분석 요청
        // ═══════════════════════════════════════════════════════════

        [RelayCommand(CanExecute = nameof(CanAnalyze))]
        private async Task AnalyzeAsync()
        {
            //if (!_mqtt.IsConnected || _selectedImagePath is null || IsAnalyzing)
            if (!_mqtt.IsConnected || _selectedImagePath is null)
            {
                if (!_mqtt.IsConnected)
                {
                    StatusMessage = $"서버 연결 상태 오류";
                    _snackbarService.Show("분석 요청 실패", "서버 연결 상태를 확인해주세요.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
                }
                return;
            }

            try
            {
                if (IsContinuousMode)
                {
                    _isContinuousModeBackup = IsContinuousMode;
                    await ContinuousModeAnalyzeAsync();
                    return;
                }

                // 이미지 → Base64
                var bytes = await File.ReadAllBytesAsync(_selectedImagePath);
                var base64 = Convert.ToBase64String(bytes);

                // 새 분석 아이템 생성
                var item = new AnalysisItem
                {
                    Image = QueryImage,
                    ImageInfo = ImageInfo,
                    AnalysisTypeDisplay = SelectedEventType?.Display ?? SelectedEventType?.Key ?? string.Empty,
                    IsAnalyzing = true
                };

                if (_allHistory.Count > 99)
                    _allHistory.RemoveAt(_allHistory.Count - 1);

                // 히스토리 맨 위에 추가
                _allHistory.Insert(0, item);
                _currentAnalysisId = item.Id;
                IsAnalyzing = true;

                // MQTT Publish (ID 포함)
                var payload = JsonSerializer.Serialize(new
                {
                    id = item.Id.ToString(),
                    eventType = SelectedEventType?.Key ?? "Unknown",
                    classType = "Unknown",
                    boundingBox = new Dictionary<string, List<double>>(),
                    @confidence = new Dictionary<string, double>(),
                    @base64 = base64
                });

                await _mqtt.PublishAsync(PubTopic, payload);
                StatusMessage = $"분석 요청 완료 (분석 진행 중...)";

                // 미응답 생략 타임아웃 시작
                if (IsSkipNoResponse)
                    StartResponseTimeout(item.Id);

                AnalyzeCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"전송 오류: {ex.Message}";
                if (_currentAnalysisId.HasValue)
                {
                    var item = _allHistory.FirstOrDefault(x => x.Id == _currentAnalysisId.Value);
                    if (item is not null)
                        _allHistory.Remove(item);
                }
                IsAnalyzing = false;
                _currentAnalysisId = null;
                AnalyzeCommand.NotifyCanExecuteChanged();
            }
        }
        private async Task ContinuousModeAnalyzeAsync()
        {
            try
            {
                IsAnalyzing = true;

                AnalyzeCommand.NotifyCanExecuteChanged();
                CancelAnalysisCommand.NotifyCanExecuteChanged();

                // 이전 루프 정리
                _analysisLoopCts?.Cancel();
                _analysisLoopCts = new CancellationTokenSource();

                var token = _analysisLoopCts.Token;

                // 이미지 미리 읽기
                var bytes = await File.ReadAllBytesAsync(_selectedImagePath);
                var base64 = Convert.ToBase64String(bytes);

                var timeoutSeconds = Math.Max(1, SkipNoResponseTimeoutSeconds);
                StatusMessage = $"{timeoutSeconds}초 간격 자동 전송 시작";

                while (!token.IsCancellationRequested)
                {
                    // 새 분석 아이템 생성
                    var item = new AnalysisItem
                    {
                        Image = QueryImage,
                        ImageInfo = ImageInfo,
                        AnalysisTypeDisplay = SelectedEventType?.Display ?? SelectedEventType?.Key ?? string.Empty,
                        IsAnalyzing = true
                    };

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _allHistory.Insert(0, item);
                    });

                    _currentAnalysisId = item.Id;

                    // MQTT Payload
                    var payload = JsonSerializer.Serialize(new
                    {
                        id = item.Id.ToString(),
                        @base64 = base64
                    });

                    // Publish
                    await _mqtt.PublishAsync(PubTopic, payload);

                    StatusMessage = $"분석 요청 전송 완료 ({DateTime.Now:HH:mm:ss})";

                    // n초 대기
                    await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), token);
                }
            }
            catch (TaskCanceledException)
            {
                StatusMessage = "자동 전송 중지";
            }
            catch (Exception ex)
            {
                StatusMessage = $"전송 오류: {ex.Message}";
            }
            finally
            {
                IsAnalyzing = false;
                _currentAnalysisId = null;

                AnalyzeCommand.NotifyCanExecuteChanged();
                CancelAnalysisCommand.NotifyCanExecuteChanged();
            }
        }

        //private bool CanAnalyze()
        //    => _mqtt.IsConnected && QueryImage is not null && !IsAnalyzing;
        private bool CanAnalyze() => true;

        // ═══════════════════════════════════════════════════════════
        // 분석 취소
        // ═══════════════════════════════════════════════════════════

        [RelayCommand(CanExecute = nameof(CanCancelAnalysis))]
        private async void CancelAnalysis()
        {
            CancelResponseTimeout();

            if (_isContinuousModeBackup)
            {
                ContinuousModeCancelAnalysis();
                return;
            }

            if (IsLiveEventMode)
            {
                await LiveEventModeCancelAnalysis();
                return;
            }

            if (!_currentAnalysisId.HasValue) return;

            var item = _allHistory.FirstOrDefault(x => x.Id == _currentAnalysisId.Value);
            if (item is not null)
                _allHistory.Remove(item);

            IsAnalyzing = false;
            _currentAnalysisId = null;
            StatusMessage = "분석 요청이 취소되었습니다.";

            AnalyzeCommand.NotifyCanExecuteChanged();
            CancelAnalysisCommand.NotifyCanExecuteChanged();

            _snackbarService.Show("분석 요청 취소", "분석 요청이 취소 되었습니다.", ControlAppearance.Info, new SymbolIcon(SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
        }
        private void ContinuousModeCancelAnalysis()
        {
            StatusMessage = "자동 분석 요청 중지 중...";

            _analysisLoopCts?.Cancel();

            IsAnalyzing = false;
            _currentAnalysisId = null;

            StatusMessage = "자동 분석 요청이 중지되었습니다.";

            AnalyzeCommand.NotifyCanExecuteChanged();
            CancelAnalysisCommand.NotifyCanExecuteChanged();

            _snackbarService.Show(
                "분석 중지",
                "자동 전송이 중지되었습니다.",
                ControlAppearance.Info,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(3));
        }
        private async Task LiveEventModeCancelAnalysis()
        {
            IsAnalyzing = false;
            _currentAnalysisId = null;
            StatusMessage = "실시간 이벤트 분석이 중지되었습니다.";
            IsLiveEventMode = false;
            AnalyzeCommand.NotifyCanExecuteChanged();
            CancelAnalysisCommand.NotifyCanExecuteChanged();
            _snackbarService.Show(
                "분석 중지",
                "실시간 분석이 중지되었습니다.",
                ControlAppearance.Info,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(3));
        }

        //private bool CanCancelAnalysis() => IsAnalyzing;
        private bool CanCancelAnalysis() => true;

        [RelayCommand(CanExecute = nameof(CanCancelAnalysisItem))]
        private void CancelAnalysisItem(AnalysisItem? item)
        {
            if (item is null || !item.IsAnalyzing)
                return;

            if (_currentAnalysisId == item.Id)
            {
                CancelResponseTimeout();
                IsAnalyzing = false;
                _currentAnalysisId = null;
                _isRealtimeCooldown = false;
            }

            var realtimeItem = RealtimeEvents.FirstOrDefault(x => x.AnalysisItem?.Id == item.Id);
            if (realtimeItem is not null)
            {
                realtimeItem.IsAnalyzing = false;
                realtimeItem.IsAnalysisCompleted = false;
                realtimeItem.AnalysisItem = null;
            }

            _allHistory.Remove(item);
            AnalysisHistory.Refresh();

            StatusMessage = "분석 요청이 취소되었습니다.";
            AnalyzeCommand.NotifyCanExecuteChanged();
            CancelAnalysisCommand.NotifyCanExecuteChanged();
            CancelAnalysisItemCommand.NotifyCanExecuteChanged();

            _snackbarService.Show(
                "분석 요청 취소",
                "선택한 분석 요청이 취소되었습니다.",
                ControlAppearance.Info,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(3));
        }

        private bool CanCancelAnalysisItem(AnalysisItem? item)
            => item?.IsAnalyzing == true;

        [RelayCommand]
        private void ClearHistory()
        {
            CancelAnalysis();
            _allHistory.Clear();
            RealtimeEvents.Clear();
            StatusMessage = "분석 기록이 초기화되었습니다.";
        }

        public AnalysisItem? SelectRealtimeEvent(RealtimeEventItem item)
        {
            foreach (var historyItem in _allHistory)
                historyItem.IsRealtimeSelected = false;

            if (item.IsSelected)
            {
                item.IsSelected = false;

                AnalysisHistory.Refresh();
                return null;
            }

            foreach (var eventItem in RealtimeEvents)
                eventItem.IsSelected = false;

            item.IsSelected = true;

            var analysisItem = item.AnalysisItem;
            if (analysisItem is null)
            {
                AnalysisHistory.Refresh();
                return null;
            }

            analysisItem.IsRealtimeSelected = true;

            AnalysisHistory.Refresh();
            return analysisItem;
        }

        // ═══════════════════════════════════════════════════════════
        // MQTT 수신
        // ═══════════════════════════════════════════════════════════

        // ── 미응답 생략 ────────────────────────────────────────────
        [ObservableProperty] private bool _isSkipNoResponse;

        // 미응답 생략 대기 시간(초). 사용자가 UI에서 조정 가능.
        [ObservableProperty] private int _skipNoResponseTimeoutSeconds = 10;

        private CancellationTokenSource? _timeoutCts;

        // 미응답 생략을 켜고 끌 때, 현재 진행 중인 요청에도 즉시 반영되도록 처리
        partial void OnIsSkipNoResponseChanged(bool oldValue, bool newValue)
        {
            if (newValue)
            {
                // 현재 분석 중인 요청이 있으면 지금부터 타임아웃 적용
                if (_currentAnalysisId.HasValue && IsAnalyzing)
                    StartResponseTimeout(_currentAnalysisId.Value);
            }
            else
            {
                // 미응답 생략을 끄면 진행 중인 타임아웃도 즉시 취소
                CancelResponseTimeout();
            }
        }

        partial void OnSkipNoResponseTimeoutSecondsChanged(int oldValue, int newValue)
        {
            // 최소 1초 보장
            if (newValue < 1)
            {
                SkipNoResponseTimeoutSeconds = 1;
                return;
            }

            // 값이 바뀐 시점에 이미 대기 중인 타임아웃이 있으면 새 값으로 다시 시작
            if (IsSkipNoResponse && _timeoutCts is not null && _currentAnalysisId.HasValue && IsAnalyzing)
                StartResponseTimeout(_currentAnalysisId.Value);
        }

        // ═══════════════════════════════════════════════════════════
        // 미응답 생략 타임아웃
        // ═══════════════════════════════════════════════════════════

        private void StartResponseTimeout(Guid itemId)
        {
            // 이전 타임아웃 취소
            _timeoutCts?.Cancel();
            _timeoutCts = new CancellationTokenSource();
            var token = _timeoutCts.Token;

            var timeoutSeconds = Math.Max(1, SkipNoResponseTimeoutSeconds);

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), token);

                    // 타임아웃 경과 → UI 스레드에서 처리
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var item = _allHistory.FirstOrDefault(x => x.Id == itemId && x.IsAnalyzing);
                        if (item is null) return;

                        _allHistory.Remove(item);
                        var realtimeItem = RealtimeEvents.FirstOrDefault(x => x.AnalysisItem?.Id == itemId);
                        if (realtimeItem is not null)
                        {
                            realtimeItem.IsAnalyzing = false;
                            realtimeItem.IsAnalysisCompleted = false;
                        }

                        IsAnalyzing = false;
                        _currentAnalysisId = null;
                        StatusMessage = "응답 시간 초과 - 요청이 자동 삭제되었습니다.";

                        AnalyzeCommand.NotifyCanExecuteChanged();
                        CancelAnalysisCommand.NotifyCanExecuteChanged();

                        _snackbarService.Show(
                            "응답 시간 초과",
                            $"{timeoutSeconds}초 내 응답이 없어 해당 요청이 자동으로 삭제되었습니다.",
                            ControlAppearance.Caution,
                            new SymbolIcon(SymbolRegular.ClockAlarm24),
                            TimeSpan.FromSeconds(3));
                    });
                }
                catch (TaskCanceledException)
                {
                    // 정상 응답 수신 또는 수동 취소 → 무시
                }
            }, token);
        }

        private void CancelResponseTimeout()
        {
            _timeoutCts?.Cancel();
            _timeoutCts = null;
        }

        private bool _isRealtimeCooldown;
        private void OnMqttMessageReceived(string topic, string payload)
        {
            if (topic.Equals(SubTopic, StringComparison.OrdinalIgnoreCase))
            {
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

                        // ID로 매칭 (또는 첫 번째 분석 중인 항목)
                        AnalysisItem? targetItem = null;

                        // 응답에 id가 있으면 매칭 시도
                        var hasResponseId = false;
                        if (!string.IsNullOrEmpty(response.Id) && Guid.TryParse(response.Id, out var guid))
                        {
                            hasResponseId = true;
                            targetItem = _allHistory.FirstOrDefault(x => x.Id == guid);
                        }
                        //if (payload.Contains("\"id\""))
                        //{
                        //    var doc = JsonDocument.Parse(payload);
                        //    if (doc.RootElement.TryGetProperty("id", out var idProp))
                        //    {
                        //        var idStr = idProp.GetString();
                        //        if (Guid.TryParse(idStr, out var guid))
                        //            targetItem = AnalysisHistory.FirstOrDefault(x => x.Id == guid);
                        //    }
                        //}

                        // ID가 있는 응답은 해당 ID가 없으면 취소/삭제된 요청으로 보고 무시
                        if (!hasResponseId)
                            targetItem ??= _allHistory.FirstOrDefault(x => x.IsAnalyzing);

                        if (targetItem is null)
                        {
                            StatusMessage = "분석 결과를 매칭할 항목이 없습니다.";
                            return;
                        }

                        // 결과 업데이트
                        targetItem.Description = response.Desc ?? string.Empty;
                        targetItem.Result = response.Result ?? string.Empty;
                        targetItem.Decision = response.Decision ?? string.Empty;
                        targetItem.SetConfidenceScore(ParseVlmConfidence(response.Confidence));
                        targetItem.IsAnalyzing = false;

                        var realtimeItem = RealtimeEvents.FirstOrDefault(x => x.AnalysisItem?.Id == targetItem.Id);
                        if (realtimeItem is not null)
                        {
                            realtimeItem.Decision = targetItem.Decision;
                            realtimeItem.IsAnalyzing = false;
                            realtimeItem.IsAnalysisCompleted = true;
                        }

                        CancelResponseTimeout();

                        AnalysisHistory.Refresh();

                        if (!_isContinuousModeBackup)
                        {
                            if (IsLiveEventMode && UseRealtimeAnalysisDelay && _delayTime > 0)
                            {
                                // 실시간 이벤트 모드에서는 설정된 시간 동안 추가 분석 요청을 지연
                                _isRealtimeCooldown = true;

                                Task.Run(async () =>
                                {
                                    await Task.Delay(_delayTime);

                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        IsAnalyzing = _allHistory.Any(x => x.IsAnalyzing);
                                        _isRealtimeCooldown = false;

                                        if (IsLiveEventMode)
                                            StatusMessage = "실시간 이벤트 대기 중...";
                                    });
                                });
                            }
                            else
                            {
                                IsAnalyzing = _allHistory.Any(x => x.IsAnalyzing);
                            }
                        }
                        _currentAnalysisId = null;
                        StatusMessage = $"분석 결과 수신 완료";

                        AnalyzeCommand.NotifyCanExecuteChanged();
                        CancelAnalysisCommand.NotifyCanExecuteChanged();

                        _snackbarService.Show("분석 완료", "이미지 분석을 완료 하였습니다.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
                    }
                    catch (JsonException ex)
                    {
                        StatusMessage = $"JSON 파싱 오류: {ex.Message}";
                        _snackbarService.Show("분석 실패", ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
                    }
                });
            }
            else if (IsEventTopic(topic) && IsLiveEventMode)
            {
                Application.Current.Dispatcher.Invoke(async () =>
                {
                    try
                    {
                        var response = JsonSerializer.Deserialize<MqttEventDataModel>(
                            payload,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                        if (response == null)
                        {
                            StatusMessage = "응답 파싱 실패";
                            return;
                        }

                        var eventItem = response.EventItem;
                        var eventType = eventItem.EventType ?? "Unknown";
                        var classType = eventItem.ClassType ?? "Unknown";

                        if (!ShowNegativeConfidenceRealtimeEvents && HasNegativeConfidence(eventItem))
                        {
                            StatusMessage = $"실시간 이벤트 제외 (음수 Confidence): {eventType} ({classType})";
                            return;
                        }

                        if (IsRealtimeEventExcluded(eventType))
                        {
                            StatusMessage = $"실시간 이벤트 제외: {eventType} ({classType})";
                            return;
                        }

                        var imageBase64 = !string.IsNullOrWhiteSpace(eventItem.ImageInfo.FullFrameBase64Data)
                            ? eventItem.ImageInfo.FullFrameBase64Data
                            : eventItem.ImageInfo.Base64Data;

                        var configuredEventType = FindConfiguredEventType(eventType);
                        var isAnalysisTarget = configuredEventType is not null && configuredEventType.IsLiveEnabled;
                        var analysisEventType = configuredEventType?.Key ?? eventType;
                        var analysisTypeDisplay = configuredEventType?.Display ?? eventType;
                        var confidences = BuildRealtimeConfidences(eventItem);
                        var bbox = BuildRealtimeBoundingBoxes(eventItem);
                        RemoveNegativeRealtimeConfidence(confidences, bbox);

                        BitmapImage? image = null;
                        if (!string.IsNullOrWhiteSpace(imageBase64))
                            image = Base64ToBitmapImage(imageBase64);

                        var realtimeItem = new RealtimeEventItem
                        {
                            Image = image,
                            EventType = eventType,
                            AnalysisEventType = analysisEventType,
                            EventTypeDisplay = configuredEventType?.Display ?? eventType,
                            AnalysisTypeDisplay = analysisTypeDisplay,
                            ClassType = classType,
                            ImageBase64 = imageBase64,
                            Confidences = new Dictionary<string, double>(confidences),
                            BoundingBoxes = CloneBoundingBoxes(bbox),
                            Timestamp = GetRealtimeEventTimestamp(response, eventItem),
                            IsAnalysisTarget = isAnalysisTarget
                        };

                        if (RealtimeEvents.Count >= 200)
                            RealtimeEvents.RemoveAt(RealtimeEvents.Count - 1);

                        RealtimeEvents.Insert(0, realtimeItem);

                        if (!isAnalysisTarget || string.IsNullOrEmpty(imageBase64))
                        {
                            StatusMessage = $"실시간 이벤트 수신 (분석 생략): {eventType} ({classType})";
                            return;
                        }

                        eventType = configuredEventType!.Key;

                        if (SkipRealtimeAnalysisWhenBusy && (IsAnalyzing || _isRealtimeCooldown))
                        {
                            StatusMessage = $"실시간 이벤트 수신 (분석 대기): {eventType} ({classType})";
                            realtimeItem.IsAnalysisTarget = false; // 분석 대상이지만 현재 분석 중이므로 분석 제외로 표시
                            return;
                        }

                        if (confidences.Count == 0)
                        {
                            StatusMessage = "Confidence 오류: 데이터 없음";
                            realtimeItem.IsAnalysisTarget = false; // 분석 대상이지만 Confidence 데이터가 없으므로 분석 제외로 표시
                            realtimeItem.IsAnalyzing = false;
                            return;
                        }

                        var item = new AnalysisItem
                        {
                            Image = image,
                            ImageInfo = image is null
                                ? $"{eventType} · {classType}"
                                : $"{eventType} · {classType} · {image.PixelWidth}×{image.PixelHeight}",
                            AnalysisTypeDisplay = analysisTypeDisplay,
                            IsAnalyzing = true
                        };
                        realtimeItem.AnalysisItem = item;
                        realtimeItem.IsAnalyzing = true;

                        if (_allHistory.Count >= 100)
                            _allHistory.RemoveAt(_allHistory.Count - 1);

                        // 히스토리 맨 위에 추가
                        _allHistory.Insert(0, item);
                        _currentAnalysisId = item.Id;
                        IsAnalyzing = true;

                        var request = JsonSerializer.Serialize(new
                        {
                            id = item.Id.ToString(),
                            eventType = eventType,
                            classType = classType,
                            boundingBox = bbox,
                            @confidence = confidences,
                            @base64 = imageBase64
                        });

                        await _mqtt.PublishAsync(PubTopic, request);
                        StatusMessage = $"실시간 분석 진행 중...";

                        // 미응답 생략 타임아웃 시작
                        if (IsSkipNoResponse)
                            StartResponseTimeout(item.Id);

                        AnalyzeCommand.NotifyCanExecuteChanged();
                    }
                    catch (JsonException ex)
                    {
                        StatusMessage = $"JSON 파싱 오류: {ex.Message}";
                        return;
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"실시간 이벤트 처리 오류: {ex.Message}";
                        return;
                    }
                });
            }
        }

        private bool IsEventTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(_eventTopic))
                return false;

            var wildcardIndex = _eventTopic.IndexOfAny(['#', '+']);
            if (wildcardIndex < 0)
                return topic.Equals(_eventTopic, StringComparison.OrdinalIgnoreCase);

            var prefix = _eventTopic[..wildcardIndex];
            return topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static double? ParseVlmConfidence(JsonElement confidence)
        {
            double value;

            switch (confidence.ValueKind)
            {
                case JsonValueKind.Number when confidence.TryGetDouble(out value):
                    break;
                case JsonValueKind.String:
                    var text = confidence.GetString();
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                        !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                    {
                        return null;
                    }
                    break;
                default:
                    return null;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
                return null;

            return value > 1 ? value / 100 : value;
        }

        private static string GetRealtimeEventTimestamp(MqttEventDataModel response, EventItemModel eventItem)
        {
            var rawTimestamp = !string.IsNullOrWhiteSpace(eventItem.ImageInfo.Timestamp)
                ? eventItem.ImageInfo.Timestamp
                : response.CreateTime;

            if (DateTime.TryParse(rawTimestamp, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed) ||
                DateTime.TryParse(rawTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            {
                return parsed.ToString("yyyy-MM-dd HH:mm:ss");
            }

            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private EventTypeItem? FindConfiguredEventType(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                return null;

            return EventTypes.FirstOrDefault(x =>
                eventType.Contains(x.Key, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsRealtimeEventExcluded(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
                return false;

            return RealtimeEventExcludeTypes.Any(x =>
                !string.IsNullOrWhiteSpace(x.Key) &&
                eventType.Contains(x.Key, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasNegativeConfidence(EventItemModel eventItem)
        {
            return eventItem.Confidences.Any(x => x.Value < 0) ||
                   eventItem.Confidence.Any(x => x < 0);
        }

        private static Dictionary<string, double> BuildRealtimeConfidences(EventItemModel eventItem)
        {
            if (eventItem.Confidences.Count > 0)
                return new Dictionary<string, double>(eventItem.Confidences);

            if (eventItem.Confidence.Count == 0)
                return new Dictionary<string, double>();

            return eventItem.Confidence.ToDictionary(
                _ => eventItem.ObjectId.ToString(),
                x => x);
        }

        private static Dictionary<string, List<double>> BuildRealtimeBoundingBoxes(EventItemModel eventItem)
        {
            if (eventItem.BoundingBoxs.Count > 0)
                return CloneBoundingBoxes(eventItem.BoundingBoxs);

            return new Dictionary<string, List<double>>
            {
                [eventItem.ObjectId.ToString()] = new()
                {
                    eventItem.BoundingBox.X,
                    eventItem.BoundingBox.Y,
                    eventItem.BoundingBox.Width,
                    eventItem.BoundingBox.Height
                }
            };
        }

        private static void RemoveNegativeRealtimeConfidence(
            Dictionary<string, double> confidences,
            Dictionary<string, List<double>> boundingBoxes)
        {
            var invalidKeys = confidences
                .Where(x => x.Value < 0)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in invalidKeys)
            {
                confidences.Remove(key);
                boundingBoxes.Remove(key);
            }
        }

        private static Dictionary<string, List<double>> CloneBoundingBoxes(
            Dictionary<string, List<double>> source)
            => source.ToDictionary(x => x.Key, x => x.Value.ToList());

        [RelayCommand(CanExecute = nameof(CanAnalyzeRealtimeEvent))]
        private async Task AnalyzeRealtimeEventAsync(RealtimeEventItem? realtimeItem)
        {
            if (realtimeItem is null || realtimeItem.IsAnalyzing ||
                string.IsNullOrWhiteSpace(realtimeItem.ImageBase64))
            {
                return;
            }

            var connected = await EnsureConnectedAsync();
            if (!connected)
            {
                StatusMessage = "수동 VLM 분석 실패 - 서버 연결 상태를 확인해주세요.";
                return;
            }

            var item = new AnalysisItem
            {
                Image = realtimeItem.Image,
                ImageInfo = realtimeItem.Image is null
                    ? $"{realtimeItem.AnalysisEventType} · {realtimeItem.ClassType}"
                    : $"{realtimeItem.AnalysisEventType} · {realtimeItem.ClassType} · {realtimeItem.Image.PixelWidth}×{realtimeItem.Image.PixelHeight}",
                AnalysisTypeDisplay = string.IsNullOrWhiteSpace(realtimeItem.AnalysisTypeDisplay)
                    ? realtimeItem.TypeDisplay
                    : realtimeItem.AnalysisTypeDisplay,
                IsAnalyzing = true
            };

            if (_allHistory.Count >= 100)
                _allHistory.RemoveAt(_allHistory.Count - 1);

            _allHistory.Insert(0, item);
            realtimeItem.AnalysisItem = item;
            realtimeItem.IsAnalysisTarget = true;
            realtimeItem.IsAnalyzing = true;
            realtimeItem.IsAnalysisCompleted = false;
            realtimeItem.Decision = string.Empty;

            _currentAnalysisId = item.Id;
            IsAnalyzing = true;

            var request = JsonSerializer.Serialize(new
            {
                id = item.Id.ToString(),
                eventType = string.IsNullOrWhiteSpace(realtimeItem.AnalysisEventType)
                    ? realtimeItem.EventType
                    : realtimeItem.AnalysisEventType,
                classType = realtimeItem.ClassType,
                boundingBox = CloneBoundingBoxes(realtimeItem.BoundingBoxes),
                @confidence = new Dictionary<string, double>(realtimeItem.Confidences),
                @base64 = realtimeItem.ImageBase64
            });

            await _mqtt.PublishAsync(PubTopic, request);
            StatusMessage = "수동 VLM 분석 진행 중...";

            if (IsSkipNoResponse)
                StartResponseTimeout(item.Id);

            AnalyzeCommand.NotifyCanExecuteChanged();
            CancelAnalysisCommand.NotifyCanExecuteChanged();
            AnalyzeRealtimeEventCommand.NotifyCanExecuteChanged();
        }

        private bool CanAnalyzeRealtimeEvent(RealtimeEventItem? realtimeItem)
            => realtimeItem is not null &&
               !realtimeItem.IsAnalyzing &&
               !string.IsNullOrWhiteSpace(realtimeItem.ImageBase64);

        [RelayCommand]
        private async Task ConfigureEventTypesAsync()
        {
            var draft = new ObservableCollection<EventTypeItem>(
                EventTypes.Select(CloneEventTypeItem));

            while (true)
            {
                var content = new VlmEventTypeSettingsDialog(draft);
                var dialog = new ContentDialog(_dialogService.GetContentPresenter())
                {
                    Title = "실시간 이벤트 타입 설정",
                    Content = content,
                    PrimaryButtonText = "적용",
                    CloseButtonText = "취소"
                };

                var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
                if (result != ContentDialogResult.Primary)
                    return;

                content.CommitEdits();

                var validationMessage = ValidateEventTypes(draft);
                if (!string.IsNullOrEmpty(validationMessage))
                {
                    _snackbarService.Show(
                        "이벤트 타입 설정 오류",
                        validationMessage,
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24),
                        TimeSpan.FromSeconds(3));
                    continue;
                }

                var originalSignature = GetEventTypesSignature(EventTypes);
                var draftSignature = GetEventTypesSignature(draft);
                if (originalSignature == draftSignature)
                {
                    StatusMessage = "변경된 이벤트 타입 설정이 없습니다.";
                    return;
                }

                ApplyEventTypes(draft);

                var saveResult = System.Windows.MessageBox.Show(
                    "변경된 이벤트 타입 설정 저장할까요?",
                    "이벤트 타입 설정 저장",
                    System.Windows.MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (saveResult == System.Windows.MessageBoxResult.Yes)
                {
                    SaveVlmSettings();
                    _snackbarService.Show(
                        "설정 저장 완료",
                        "이벤트 타입 설정이 저장되었습니다.",
                        ControlAppearance.Success,
                        new SymbolIcon(SymbolRegular.Checkmark24),
                        TimeSpan.FromSeconds(3));
                }
                else
                {
                    StatusMessage = "이벤트 타입 설정이 현재 실행 중인 화면에만 적용되었습니다.";
                }

                return;
            }
        }

        private static EventTypeItem CloneEventTypeItem(EventTypeItem item)
        {
            return new EventTypeItem
            {
                Key = item.Key,
                Display = item.Display,
                IsLiveEnabled = item.IsLiveEnabled
            };
        }

        private static string? ValidateEventTypes(IEnumerable<EventTypeItem> items)
        {
            var normalized = items
                .Select(x => x.Key?.Trim() ?? string.Empty)
                .ToList();

            if (normalized.Count == 0)
                return "최소 1개의 이벤트 타입이 필요합니다.";

            if (normalized.Any(string.IsNullOrWhiteSpace))
                return "Key가 비어 있는 이벤트 타입이 있습니다.";

            var duplicatedKey = normalized
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Count() > 1)
                ?.Key;

            return duplicatedKey is null
                ? null
                : $"중복된 Key가 있습니다: {duplicatedKey}";
        }

        private static string? ValidateRealtimeEventExcludeTypes(IEnumerable<EventTypeItem> items)
        {
            var normalized = items
                .Select(x => x.Key?.Trim() ?? string.Empty)
                .ToList();

            if (normalized.Any(string.IsNullOrWhiteSpace))
                return "Key가 비어 있는 제외 타입이 있습니다.";

            var duplicatedKey = normalized
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Count() > 1)
                ?.Key;

            return duplicatedKey is null
                ? null
                : $"중복된 Key가 있습니다: {duplicatedKey}";
        }

        private static string GetEventTypesSignature(IEnumerable<EventTypeItem> items)
        {
            return string.Join(
                "\n",
                items.Select(x =>
                    $"{x.Key.Trim()}|{x.Display.Trim()}|{x.IsLiveEnabled}"));
        }

        private void ApplyEventTypes(IEnumerable<EventTypeItem> items)
        {
            var selectedKey = SelectedEventType?.Key;
            var nextItems = items
                .Select(x => new EventTypeItem
                {
                    Key = x.Key.Trim(),
                    Display = string.IsNullOrWhiteSpace(x.Display) ? x.Key.Trim() : x.Display.Trim(),
                    IsLiveEnabled = x.IsLiveEnabled
                })
                .ToList();

            EventTypes.Clear();
            foreach (var item in nextItems)
                EventTypes.Add(item);

            SelectedEventType = EventTypes.FirstOrDefault(x =>
                x.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase))
                ?? EventTypes.FirstOrDefault();
        }

        [RelayCommand]
        private async Task ConfigureRealtimeEventExcludeTypesAsync()
        {
            var draft = new ObservableCollection<EventTypeItem>(
                RealtimeEventExcludeTypes.Select(CloneEventTypeItem));
            var showNegativeConfidence = ShowNegativeConfidenceRealtimeEvents;
            var delayTime = _delayTime;
            var useRealtimeDelay = UseRealtimeAnalysisDelay;
            var skipAnalysisWhenBusy = SkipRealtimeAnalysisWhenBusy;

            while (true)
            {
                var content = new VlmRealtimeEventExcludeTypeSettingsDialog(
                    draft,
                    showNegativeConfidence,
                    delayTime,
                    useRealtimeDelay,
                    skipAnalysisWhenBusy);
                var dialog = new ContentDialog(_dialogService.GetContentPresenter())
                {
                    Title = "실시간 이벤트 제외 타입 설정",
                    Content = content,
                    PrimaryButtonText = "적용",
                    CloseButtonText = "취소"
                };

                var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
                if (result != ContentDialogResult.Primary)
                    return;

                content.CommitEdits();
                var selectedShowNegativeConfidence = content.ShowNegativeConfidenceEvents;
                var selectedDelayTime = content.DelayTimeMilliseconds;
                var selectedUseRealtimeDelay = content.UseRealtimeAnalysisDelay;
                var selectedSkipAnalysisWhenBusy = content.SkipRealtimeAnalysisWhenBusy;

                var validationMessage = ValidateRealtimeEventExcludeTypes(draft);
                if (!string.IsNullOrEmpty(validationMessage))
                {
                    showNegativeConfidence = selectedShowNegativeConfidence;
                    delayTime = selectedDelayTime;
                    useRealtimeDelay = selectedUseRealtimeDelay;
                    skipAnalysisWhenBusy = selectedSkipAnalysisWhenBusy;
                    _snackbarService.Show(
                        "제외 타입 설정 오류",
                        validationMessage,
                        ControlAppearance.Danger,
                        new SymbolIcon(SymbolRegular.ErrorCircle24),
                        TimeSpan.FromSeconds(3));
                    continue;
                }

                var originalSignature = GetEventTypesSignature(RealtimeEventExcludeTypes);
                var draftSignature = GetEventTypesSignature(draft);
                if (originalSignature == draftSignature)
                {
                    if (ShowNegativeConfidenceRealtimeEvents == selectedShowNegativeConfidence &&
                        _delayTime == selectedDelayTime &&
                        UseRealtimeAnalysisDelay == selectedUseRealtimeDelay &&
                        SkipRealtimeAnalysisWhenBusy == selectedSkipAnalysisWhenBusy)
                    {
                        StatusMessage = "변경된 실시간 이벤트 제외 타입 설정이 없습니다.";
                        return;
                    }
                }

                RealtimeEventExcludeTypes.Clear();
                foreach (var item in draft)
                {
                    RealtimeEventExcludeTypes.Add(new EventTypeItem
                    {
                        Key = item.Key.Trim(),
                        Display = string.IsNullOrWhiteSpace(item.Display) ? item.Key.Trim() : item.Display.Trim(),
                        IsLiveEnabled = true
                    });
                }
                ShowNegativeConfidenceRealtimeEvents = selectedShowNegativeConfidence;
                _delayTime = selectedDelayTime;
                UseRealtimeAnalysisDelay = selectedUseRealtimeDelay;
                SkipRealtimeAnalysisWhenBusy = selectedSkipAnalysisWhenBusy;

                var saveResult = System.Windows.MessageBox.Show(
                    "변경된 실시간 이벤트 제외 타입 설정을 저장할까요?",
                    "실시간 이벤트 제외 타입 설정 저장",
                    System.Windows.MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (saveResult == System.Windows.MessageBoxResult.Yes)
                {
                    SaveVlmSettings();
                    _snackbarService.Show(
                        "설정 저장 완료",
                        "실시간 이벤트 제외 타입 설정이 저장되었습니다.",
                        ControlAppearance.Success,
                        new SymbolIcon(SymbolRegular.Checkmark24),
                        TimeSpan.FromSeconds(3));
                }
                else
                {
                    StatusMessage = "실시간 이벤트 제외 타입 설정이 현재 실행 중인 화면에만 적용되었습니다.";
                }

                return;
            }
        }


        [RelayCommand]
        private async Task ShowDetailAsync(AnalysisItem item)
        {
            if (item is null || item.IsAnalyzing) return;

            var dialog = new ContentDialog(_dialogService.GetContentPresenter())
            {
                Title = "분석 결과 상세",
                Content = new VlmAnalysisDetailDialog(item, IsFireMode, _snackbarService),
                CloseButtonText = "닫기"
            };

            // 다이얼로그가 열렸을 때 외부 영역 클릭 이벤트 바인딩
            dialog.Opened += (s, e) =>
            {
                // ContentDialog 내부의 첫 번째 자식인 SmokeLayer(또는 Grid)를 찾기
                if (s is ContentDialog currentDialog &&
                    System.Windows.Media.VisualTreeHelper.GetChildrenCount(currentDialog) > 0)
                {
                    var rootGrid = System.Windows.Media.VisualTreeHelper.GetChild(currentDialog, 0) as System.Windows.Controls.Grid;
                    if (rootGrid != null)
                    {
                        // 배경(어두운 영역)을 클릭했을 때 다이얼로그를 숨김
                        rootGrid.MouseDown += (sender, args) =>
                        {
                            // 클릭된 요소가 다이얼로그 몸통이 아니라 바깥 배경일 때만 닫기
                            if (args.OriginalSource == rootGrid)
                            {
                                currentDialog.Hide(ContentDialogResult.None);
                            }
                        };
                    }
                }
            };

            await _dialogService.ShowAsync(dialog, CancellationToken.None);
        }

        // ═══════════════════════════════════════════════════════════
        // 헬퍼
        // ═══════════════════════════════════════════════════════════

        private static BitmapImage LoadBitmapImage(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            //bitmap.DecodePixelWidth = 800; // 메모리 최적화
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private BitmapImage Base64ToBitmapImage(string base64)
        {
            var commaIndex = base64.IndexOf(',');
            if (commaIndex >= 0)
                base64 = base64[(commaIndex + 1)..];

            byte[] imageBytes = Convert.FromBase64String(base64);

            using var ms = new MemoryStream(imageBytes);

            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();

            bitmap.Freeze();

            return bitmap;
        }

        public void Dispose()
        {
            CancelResponseTimeout();
            _mqtt.MessageReceived -= OnMqttMessageReceived;
            _mqtt.ConnectionStateChanged -= OnMqttConnectionStateChanged;
        }
    }
}
