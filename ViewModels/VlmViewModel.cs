using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceSearchApp.Models;
using FaceSearchApp.Services;
using Microsoft.Win32;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FaceSearchApp.ViewModels
{
    public class EventTypeItem
    {
        public string Key { get; set; } = string.Empty;     // 실제 값
        public string Display { get; set; } = string.Empty; // 화면 표시
    }

    public partial class VlmViewModel : ObservableObject, IDisposable
    {
        private readonly ISnackbarService _snackbarService;
        private readonly IContentDialogService _dialogService;

        private CancellationTokenSource? _analysisLoopCts;

        private readonly string _configPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        private readonly MqttClientService _mqtt = new();
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

        // ── 이미지 선택 ────────────────────────────────────────────
        [ObservableProperty] private BitmapImage? _queryImage;
        [ObservableProperty] private string _imageInfo = string.Empty;
        private string? _selectedImagePath;

        [ObservableProperty]
        private bool _isFireMode = true;
        [ObservableProperty]
        private bool _isContinuousMode;
        private bool _isContinuousModeBackup;

        public ObservableCollection<EventTypeItem> EventTypes { get; } =
        [
            new() { Key = "Fire", Display = "화재" },
            new() { Key = "Fall", Display = "쓰러짐" }            
        ];

        [ObservableProperty]
        private EventTypeItem? _selectedEventType;

        [ObservableProperty]
        private bool _isLiveEventMode = false;
        [ObservableProperty]
        private bool _isLiveFireEventMode = true;
        [ObservableProperty]
        private bool _isLiveFallEventMode = true;

        // ── 분석 상태 ──────────────────────────────────────────────
        [ObservableProperty] private bool _isAnalyzing;
        [ObservableProperty] private string _statusMessage = "VLM 서버 연결 상태를 확인해주세요.";

        // ── 분석 히스토리 ──────────────────────────────────────────
        public ObservableCollection<AnalysisItem> AnalysisHistory { get; } = new();

        public VlmViewModel(ISnackbarService snackbarService, IContentDialogService dialogService)
        {
            _snackbarService = snackbarService;
            _dialogService = dialogService;

            _selectedEventType = EventTypes.FirstOrDefault();

            LoadVlmSettings();

            _mqtt.MessageReceived += OnMqttMessageReceived;
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
            }
            catch
            {
                // 실패 시 기본값 유지
            }
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

                    ["UseCredentials"] = UseCredentials
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
            if (_mqtt.IsConnected)
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

                // 연결 설정 저장
                SaveVlmSettings();

                if (!string.IsNullOrWhiteSpace(SubTopic))
                {
                    await _mqtt.SubscribeAsync(SubTopic);
                    StatusMessage = $"구독 중: {SubTopic} ";
                }
                if (!string.IsNullOrEmpty(_eventTopic))
                {
                    await _mqtt.SubscribeAsync(_eventTopic);
                    StatusMessage += $"구독 중: {_eventTopic}";
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
                if(IsContinuousMode)
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
                    IsAnalyzing = true
                };

                if(AnalysisHistory.Count > 99)
                    AnalysisHistory.RemoveAt(AnalysisHistory.Count - 1);

                // 히스토리 맨 위에 추가
                AnalysisHistory.Insert(0, item);
                _currentAnalysisId = item.Id;
                IsAnalyzing = true;

                // MQTT Publish (ID 포함)
                var payload = JsonSerializer.Serialize(new
                {
                    id = item.Id.ToString(),
                    eventType = SelectedEventType?.Key ?? "Unknown",
                    classType = "Unknown",
                    @base64 = base64
                });

                await _mqtt.PublishAsync(PubTopic, payload);
                StatusMessage = $"분석 요청 완료 (분석 진행 중...)";

                AnalyzeCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"전송 오류: {ex.Message}";
                if (_currentAnalysisId.HasValue)
                {
                    var item = AnalysisHistory.FirstOrDefault(x => x.Id == _currentAnalysisId.Value);
                    if (item is not null)
                        AnalysisHistory.Remove(item);
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

                StatusMessage = "10초 간격 자동 전송 시작";

                while (!token.IsCancellationRequested)
                {
                    // 새 분석 아이템 생성
                    var item = new AnalysisItem
                    {
                        Image = QueryImage,
                        ImageInfo = ImageInfo,
                        IsAnalyzing = true
                    };

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AnalysisHistory.Insert(0, item);
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

                    // 10초 대기
                    await Task.Delay(TimeSpan.FromSeconds(10), token);
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
        private void CancelAnalysis()
        {
            if (_isContinuousModeBackup)
            {
                ContinuousModeCancelAnalysis();
                return;
            }

            if (IsLiveEventMode)
            {
                LiveEventModeCancelAnalysis();
                return;
            }

            if (!_currentAnalysisId.HasValue) return;

            var item = AnalysisHistory.FirstOrDefault(x => x.Id == _currentAnalysisId.Value);
            if (item is not null)
                AnalysisHistory.Remove(item);

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
        private void LiveEventModeCancelAnalysis()
        {
            IsAnalyzing = false;
            _currentAnalysisId = null;
            StatusMessage = "실시간 이벤트 분석이 중지되었습니다.";
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

        [RelayCommand]
        private void ClearHistory()
        {
            CancelAnalysis();
            AnalysisHistory.Clear();
            StatusMessage = "분석 기록이 초기화되었습니다.";
        }

        // ═══════════════════════════════════════════════════════════
        // MQTT 수신
        // ═══════════════════════════════════════════════════════════

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
                        if(!string.IsNullOrEmpty(response.Id) && Guid.TryParse(response.Id, out var guid))
                        {
                            targetItem = AnalysisHistory.FirstOrDefault(x => x.Id == guid);
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

                        // ID 매칭 실패 시 현재 분석 중인 항목 찾기
                        targetItem ??= AnalysisHistory.FirstOrDefault(x => x.IsAnalyzing);

                        if (targetItem is null)
                        {
                            StatusMessage = "분석 결과를 매칭할 항목이 없습니다.";
                            return;
                        }

                        // 결과 업데이트
                        targetItem.Description = response.Desc ?? string.Empty;
                        targetItem.Result = response.Result ?? string.Empty;
                        targetItem.Decision = response.Decision ?? string.Empty;
                        targetItem.IsAnalyzing = false;

                        if (!_isContinuousModeBackup)
                        {
                            if (IsLiveEventMode)
                            {
                                // 실시간 이벤트 모드에서는 분석 완료 후 5초간 추가 이벤트 수신 대기 (쿨다운)
                                _isRealtimeCooldown = true;

                                Task.Run(async () =>
                                {
                                    await Task.Delay(5000);

                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        IsAnalyzing = false;
                                        _isRealtimeCooldown = false;

                                        StatusMessage = "실시간 이벤트 대기 중...";
                                    });
                                });
                            }
                            else
                            {
                                IsAnalyzing = false;
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
            else if (topic.StartsWith("infer_app/events/", StringComparison.OrdinalIgnoreCase) && IsLiveEventMode)
            {
                // 분석 중에는 실시간 이벤트 무시
                if (IsAnalyzing || _isRealtimeCooldown)
                    return;

                Application.Current.Dispatcher.Invoke(async () =>
                {
                    try
                    {
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        var response = JsonSerializer.Deserialize<MqttEventDataModel>(
                            payload,
                            options);
                        if (response is null)
                        {
                            StatusMessage = "응답 파싱 실패: null 응답";
                            return;
                        }

                        var eventType = response.EventItem?.EventType ?? "Unknown";
                        var classType = response.EventItem?.ClassType ?? "Unknown";
                        var imageBase64 = response.EventItem?.ImageInfo.FullFrameBase64Data ?? string.Empty;                        

                        if ((!eventType.Contains("Fall") && !eventType.Contains("Fire")) || string.IsNullOrEmpty(imageBase64))
                        {
                            StatusMessage = $"실시간 이벤트 수신 (분석 생략): {eventType} ({classType})";
                            return;
                        }

                        eventType = eventType.Contains("Fall", StringComparison.OrdinalIgnoreCase)
                            ? "Fall"
                            : eventType.Contains("Fire", StringComparison.OrdinalIgnoreCase)
                                ? "Fire"
                                : eventType;

                        if (!IsLiveFireEventMode && eventType.Equals("Fire"))
                            return;
                        if (!IsLiveFallEventMode && eventType.Equals("Fall"))
                            return;

                        // 새 분석 아이템 생성
                        BitmapImage image = Base64ToBitmapImage(imageBase64);
                        //QueryImage = image;
                        //ImageInfo = $"{eventType} · {classType} · {image.PixelWidth}×{image.PixelHeight}";
                        var item = new AnalysisItem
                        {
                            Image = image,
                            ImageInfo = $"{eventType} · {classType} · {image.PixelWidth}×{image.PixelHeight}",
                            IsAnalyzing = true
                        };

                        if (AnalysisHistory.Count > 99)
                            AnalysisHistory.RemoveAt(AnalysisHistory.Count - 1);

                        // 히스토리 맨 위에 추가
                        AnalysisHistory.Insert(0, item);
                        _currentAnalysisId = item.Id;
                        IsAnalyzing = true;

                        var request = JsonSerializer.Serialize(new
                        {
                            id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                            eventType = eventType,
                            classType = classType,
                            @base64 = imageBase64
                        });

                        await _mqtt.PublishAsync(PubTopic, request);
                        StatusMessage = $"실시간 분석 요청 완료 (분석 진행 중...)";

                        AnalyzeCommand.NotifyCanExecuteChanged();
                    }
                    catch (JsonException ex)
                    {
                        StatusMessage = $"JSON 파싱 오류: {ex.Message}";
                        return;
                    }
                });
            }
        }


        [RelayCommand]
        private async Task ShowDetailAsync(AnalysisItem item)
        {
            if (item is null || item.IsAnalyzing) return;

            var dialog = new ContentDialog(_dialogService.GetContentPresenter())
            {
                Title = "분석 결과 상세",
                Content = BuildDetailContent(item),
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

        private UIElement BuildDetailContent(AnalysisItem item)
        {
            var root = new StackPanel { Width = 500, Margin = new Thickness(0, 4, 0, 0) };

            // ── 이미지 + FireLabel 배지 ──────────────────────────
            var imgBorder = new Border
            {
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8),
                MaxHeight = 340,
                Margin = new Thickness(0, 0, 0, 14),
                ClipToBounds = true
            };

            var imgGrid = new Grid();

            var img = new System.Windows.Controls.Image
            {
                Source = item.Image,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(6)
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            // 우클릭 컨텍스트 메뉴
            img.ContextMenu = BuildImageContextMenu(item);
            imgGrid.Children.Add(img);

            // FireLabel 배지 (이미지 우상단)
            if (IsFireMode && !string.IsNullOrEmpty(item.FireLabel))
            {
                var badge = new Border
                {
                    Background = item.FireBorderBrush,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 6, 12, 6),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 10, 10, 0)
                };
                badge.Child = new Wpf.Ui.Controls.TextBlock
                {
                    Text = item.FireLabel,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                };
                imgGrid.Children.Add(badge);
            }

            imgBorder.Child = imgGrid;
            root.Children.Add(imgBorder);

            // ── 타임스탬프 · 이미지 정보 ─────────────────────────
            root.Children.Add(new Wpf.Ui.Controls.TextBlock
            {
                Text = $"{item.Timestamp}  ·  {item.ImageInfo}",
                FontSize = 11,
                Opacity = 0.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // ── Result 배지 ──────────────────────────────────────
            if (!string.IsNullOrEmpty(item.Result))
            {
                var resultBorder = new Border
                {
                    Background = (Brush)Application.Current.Resources["SystemAccentColorPrimaryBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 7, 14, 7),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                resultBorder.Child = new Wpf.Ui.Controls.TextBlock
                {
                    Text = item.Result,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                };
                root.Children.Add(resultBorder);
            }

            // ── 이미지 설명 ──────────────────────────────────────
            if (!string.IsNullOrEmpty(item.Description))
            {
                var descBorder = new Border
                {
                    Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 10, 14, 10)
                };

                var descStack = new StackPanel();
                descStack.Children.Add(new Wpf.Ui.Controls.TextBlock
                {
                    Text = "이미지 설명",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Opacity = 0.6,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                descStack.Children.Add(new Wpf.Ui.Controls.TextBlock
                {
                    Text = item.Description,
                    FontSize = 13,
                    LineHeight = 22,
                    TextWrapping = TextWrapping.Wrap
                });

                descBorder.Child = descStack;
                root.Children.Add(descBorder);
            }

            return new ScrollViewer
            {
                Content = root,
                MaxHeight = 620,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }
        private ContextMenu BuildImageContextMenu(AnalysisItem item)
        {
            var menu = new ContextMenu();

            // ── 이미지 저장 ──────────────────────────────────────
            var saveItem = new Wpf.Ui.Controls.MenuItem
            {
                Header = "이미지 저장...",
                Icon = new SymbolIcon(SymbolRegular.ArrowDownload24)
            };
            saveItem.Click += (_, _) => SaveDialogImage(item);
            menu.Items.Add(saveItem);

            // ── 클립보드 복사 ────────────────────────────────────
            var copyItem = new Wpf.Ui.Controls.MenuItem
            {
                Header = "클립보드에 복사",
                Icon = new SymbolIcon(SymbolRegular.Copy24)
            };
            copyItem.Click += (_, _) =>
            {
                if (item.Image is not null)
                {
                    Clipboard.SetImage(item.Image);
                    _snackbarService.Show(
                        "복사 완료",
                        "이미지가 클립보드에 복사되었습니다.",
                        ControlAppearance.Success,
                        new SymbolIcon(SymbolRegular.Checkmark24),
                        TimeSpan.FromSeconds(2));
                }
            };
            menu.Items.Add(copyItem);

            return menu;
        }

        private void SaveDialogImage(AnalysisItem item)
        {
            if (item.Image is null) return;

            // 기본 파일명: 이벤트타입_타임스탬프 형식
            var defaultName = string.IsNullOrWhiteSpace(item.ImageInfo)
                ? $"Image_{DateTime.Now:yyyyMMdd_HHmmss}"
                : $"{SanitizeFileName(item.ImageInfo.Split('·')[0].Trim())}_{DateTime.Now:yyyyMMdd_HHmmss}";

            var dialog = new SaveFileDialog
            {
                Title = "이미지 저장",
                Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png|BMP (*.bmp)|*.bmp|모든 파일 (*.*)|*.*",
                FilterIndex = 1,
                FileName = defaultName,
                DefaultExt = "jpg"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();

                BitmapEncoder encoder = ext switch
                {
                    ".png" => new PngBitmapEncoder(),
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new JpegBitmapEncoder { QualityLevel = 95 }
                };

                encoder.Frames.Add(BitmapFrame.Create(item.Image));

                using var fs = new FileStream(dialog.FileName, FileMode.Create);
                encoder.Save(fs);

                _snackbarService.Show(
                    "저장 완료",
                    $"{Path.GetFileName(dialog.FileName)}",
                    ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.Checkmark24),
                    TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                _snackbarService.Show(
                    "저장 실패",
                    ex.Message,
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24),
                    TimeSpan.FromSeconds(3));
            }
        }

        // 파일명에 사용 불가한 문자 제거
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
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
            bitmap.DecodePixelWidth = 800; // 메모리 최적화
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private BitmapImage Base64ToBitmapImage(string base64)
        {
            //// data:image/png;base64,... 형태 제거
            //var commaIndex = base64.IndexOf(',');
            //if (commaIndex >= 0)
            //    base64 = base64[(commaIndex + 1)..];

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
            _mqtt.MessageReceived -= OnMqttMessageReceived;
            _mqtt.Dispose();
        }
    }
}
