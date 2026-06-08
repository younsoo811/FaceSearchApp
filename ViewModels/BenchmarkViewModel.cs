using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceSearchApp.Models;
using FaceSearchApp.Services;
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FaceSearchApp.ViewModels
{
    public partial class BenchmarkViewModel : ObservableObject, IDisposable
    {
        private readonly ISnackbarService _snackbar;
        private readonly BenchmarkService _service = new();

        private const int MaxLogItems = 500;
        private const int TpsHistorySize = 60;   // 차트 바 개수 (초 단위)

        // ── 서버 설정 ─────────────────────────────────────────────────
        [ObservableProperty] private string _baseUrl = "https://studio";
        [ObservableProperty] private string _serverUsername = "local";
        [ObservableProperty] private string _serverPassword = "gN4spw9+zhwwspkTSX6fpA==";
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private string _connectionStatus = "연결되지 않음";
        [ObservableProperty] private string _connectButtonText = "연결";

        // ── 테스트 설정 ───────────────────────────────────────────────
        [ObservableProperty] private int _targetTps = 2;    // 초당 요청 수
        [ObservableProperty] private int _durationSeconds = 60;   // 테스트 시간 (0 = 무제한)
        [ObservableProperty] private int _maxConcurrency = 10;   // 최대 동시 요청 수

        // ── 이미지 목록 ───────────────────────────────────────────────
        public ObservableCollection<BenchmarkImageItem> Images { get; } = new();

        // ── 실행 상태 ─────────────────────────────────────────────────
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _isCompleted;
        [ObservableProperty] private string _runButtonText = "▶  테스트 시작";

        // ── 실시간 지표 ───────────────────────────────────────────────
        [ObservableProperty] private double _currentTps;
        [ObservableProperty] private double _averageTps;
        [ObservableProperty] private double _peakTps;
        [ObservableProperty] private long _totalRequests;
        [ObservableProperty] private long _successCount;
        [ObservableProperty] private long _errorCount;
        [ObservableProperty] private long _skippedCount;
        [ObservableProperty] private double _avgResponseMs;
        [ObservableProperty] private long _minResponseMs;
        [ObservableProperty] private long _maxResponseMs;
        [ObservableProperty] private double _errorRate;
        [ObservableProperty] private string _elapsedTime = "00:00";
        [ObservableProperty] private string _remainingTime = "--:--";
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _statusMessage = "서버에 연결 후 이미지를 추가하세요.";

        // ── TPS 차트 / 로그 ───────────────────────────────────────────
        public ObservableCollection<TpsBarItem> TpsHistory { get; } = new();
        public ObservableCollection<BenchmarkLogItem> LogItems { get; } = new();

        // ── 최종 요약 ─────────────────────────────────────────────────
        [ObservableProperty] private BenchmarkSummary? _summary;

        // ── 내부 상태 ─────────────────────────────────────────────────
        private CancellationTokenSource? _cts;
        private DispatcherTimer? _uiTimer;
        private readonly Stopwatch _stopwatch = new();

        // 스레드 안전 카운터 (Interlocked 사용)
        private long _totalSent;
        private long _successReqs;
        private long _errorReqs;
        private long _skipped;
        private long _inFlight;
        private long _totalRespMs;
        private long _minRespMs = long.MaxValue;
        private long _maxRespMs;

        // 슬라이딩 윈도우 TPS 계산 (최근 3초)
        private readonly ConcurrentQueue<DateTime> _completionWindow = new();
        private double _peakTpsInternal;

        // 초당 TPS 차트 업데이트용
        private DateTime _lastChartUpdate = DateTime.MinValue;
        private List<BenchmarkImageItem>? _snapshotImages;

        public BenchmarkViewModel(ISnackbarService snackbar) => _snackbar = snackbar;

        // ═══════════════════════════════════════════════════════════════
        // 서버 연결
        // ═══════════════════════════════════════════════════════════════

        [RelayCommand]
        private async Task ToggleConnectionAsync()
        {
            if (_service.IsConnected)
            {
                _service.Disconnect();
                IsConnected = false;
                ConnectionStatus = "연결 해제됨";
                ConnectButtonText = "연결";
                StatusMessage = "서버 연결이 해제되었습니다.";
                return;
            }

            if (string.IsNullOrWhiteSpace(BaseUrl)) { StatusMessage = "서버 주소를 입력하세요."; return; }

            ConnectionStatus = "연결 중...";

            var (ok, err) = await _service.ConnectAsync(BaseUrl, ServerUsername, ServerPassword);
            if (ok)
            {
                IsConnected = true;
                ConnectionStatus = $"연결됨 · {BaseUrl}";
                ConnectButtonText = "연결 해제";
                StatusMessage = "서버 연결 성공. 이미지를 추가하고 테스트를 시작하세요.";
                _snackbar.Show("연결 성공", "벤치마크 서버에 연결되었습니다.",
                    ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), TimeSpan.FromSeconds(2));
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "연결 실패";
                ConnectButtonText = "연결";
                StatusMessage = $"연결 실패: {err}";
                _snackbar.Show("연결 실패", err,
                    ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 이미지 관리
        // ═══════════════════════════════════════════════════════════════

        [RelayCommand]
        private async Task AddImagesAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "이미지 파일 선택 (복수 선택 가능)",
                Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.webp|모든 파일|*.*",
                Multiselect = true,
            };
            if (dialog.ShowDialog() != true) return;

            foreach (var path in dialog.FileNames)
            {
                if (Images.Any(x => x.FilePath == path)) continue;

                var fi = new FileInfo(path);
                var item = new BenchmarkImageItem
                {
                    FilePath = path,
                    FileName = fi.Name,
                    FileSizeText = fi.Length < 1024 * 1024
                        ? $"{fi.Length / 1024.0:F1} KB"
                        : $"{fi.Length / 1024.0 / 1024.0:F2} MB",
                    LoadStatus = "로딩 중",
                };

                // 썸네일
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 60;
                    bmp.EndInit();
                    bmp.Freeze();
                    item.Thumbnail = bmp;
                }
                catch { /* 썸네일 실패 무시 */ }

                Images.Add(item);

                // Base64 비동기 로드
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(path);
                        var base64 = Convert.ToBase64String(bytes);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.Base64 = base64;
                            item.IsLoaded = true;
                            item.LoadStatus = "완료";
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() => item.LoadStatus = $"오류: {ex.Message}");
                    }
                });
            }

            StatusMessage = $"{Images.Count}개 이미지 등록됨 (base64 로딩 중...)";
        }

        [RelayCommand]
        private void RemoveImage(BenchmarkImageItem item)
        {
            if (item is not null) Images.Remove(item);
        }

        [RelayCommand]
        private void ClearImages()
        {
            Images.Clear();
            StatusMessage = "이미지 목록이 초기화되었습니다.";
        }

        // ═══════════════════════════════════════════════════════════════
        // 테스트 시작 / 중지
        // ═══════════════════════════════════════════════════════════════

        [RelayCommand]
        private async Task StartBenchmarkAsync()
        {
            if (!_service.IsConnected)
            {
                _snackbar.Show("오류", "서버에 먼저 연결하세요.",
                    ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(2));
                return;
            }

            var loaded = Images.Where(x => x.IsLoaded && x.Base64 is not null).ToList();
            if (loaded.Count == 0)
            {
                _snackbar.Show("오류", "로딩 완료된 이미지가 없습니다.",
                    ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(2));
                return;
            }

            // 초기화
            ResetCounters();
            LogItems.Clear();
            TpsHistory.Clear();
            Summary = null;
            IsRunning = true;
            IsCompleted = false;
            RunButtonText = "■  테스트 중지";

            _snapshotImages = loaded;
            _cts = new CancellationTokenSource();
            _stopwatch.Restart();
            _lastChartUpdate = DateTime.UtcNow;

            StartUiTimer();

            try
            {
                await RunBenchmarkAsync(_cts.Token);
            }
            finally
            {
                _stopwatch.Stop();
                StopUiTimer();
                FinalizeResults();
            }
        }

        [RelayCommand]
        private void StopBenchmark()
        {
            _cts?.Cancel();
            StatusMessage = "중지 요청됨 — 진행 중인 요청 완료 대기 중...";
        }

        // ═══════════════════════════════════════════════════════════════
        // 벤치마크 실행 루프
        // ═══════════════════════════════════════════════════════════════

        private async Task RunBenchmarkAsync(CancellationToken token)
        {
            var images = _snapshotImages!;
            var endDuration = DurationSeconds > 0
                ? TimeSpan.FromSeconds(DurationSeconds)
                : TimeSpan.MaxValue;

            var intervalMs = Math.Max(1.0, 1000.0 / TargetTps);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // 시간 종료 체크
                    if (DurationSeconds > 0 && _stopwatch.Elapsed >= endDuration)
                        break;

                    if (!await timer.WaitForNextTickAsync(token))
                        break;

                    // 동시 요청 한도 체크
                    if (Interlocked.Read(ref _inFlight) >= MaxConcurrency)
                    {
                        Interlocked.Increment(ref _skipped);
                        continue;
                    }

                    Interlocked.Increment(ref _inFlight);
                    var img = images[Random.Shared.Next(images.Count)];
                    var reqNum = Interlocked.Increment(ref _totalSent);

                    _ = ExecuteRequestAsync(img, reqNum, token)
                        .ContinueWith(_ => Interlocked.Decrement(ref _inFlight),
                                      TaskContinuationOptions.ExecuteSynchronously);
                }
            }
            catch (OperationCanceledException) { }

            // 진행 중 요청 드레인 (최대 10초)
            StatusMessage = "진행 중인 요청 완료 대기 중...";
            using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (Interlocked.Read(ref _inFlight) > 0 && !drainCts.IsCancellationRequested)
                await Task.Delay(100, CancellationToken.None);
        }

        private async Task ExecuteRequestAsync(BenchmarkImageItem img, long reqNum, CancellationToken token)
        {
            var (success, error, rawJson, responseMs) =
                await _service.AnalyzeRawAsync(img.Base64!, token);

            if (token.IsCancellationRequested && !success) return;

            // 카운터 업데이트
            if (success) Interlocked.Increment(ref _successReqs);
            else Interlocked.Increment(ref _errorReqs);

            Interlocked.Add(ref _totalRespMs, responseMs);
            UpdateMinMax(responseMs);
            _completionWindow.Enqueue(DateTime.UtcNow);

            // UI 로그 추가 (비블로킹)
            var logItem = new BenchmarkLogItem
            {
                RequestNumber = reqNum,
                ImageName = img.FileName,
                IsSuccess = success,
                ResponseTimeMs = responseMs,
                ErrorMessage = error,
                RawPreview = rawJson.Length > 200 ? rawJson[..200] + " …" : rawJson,
            };

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (LogItems.Count >= MaxLogItems)
                    LogItems.RemoveAt(LogItems.Count - 1);
                LogItems.Insert(0, logItem);
            }, DispatcherPriority.Background);
        }

        // ═══════════════════════════════════════════════════════════════
        // UI 타이머 — 500ms마다 지표 갱신
        // ═══════════════════════════════════════════════════════════════

        private void StartUiTimer()
        {
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();
        }

        private void StopUiTimer()
        {
            _uiTimer?.Stop();
            _uiTimer = null;
        }

        private void OnUiTimerTick(object? sender, EventArgs e)
        {
            var elapsed = _stopwatch.Elapsed;

            // 경과 / 남은 시간
            ElapsedTime = elapsed.ToString(@"mm\:ss");
            if (DurationSeconds > 0)
            {
                var remaining = TimeSpan.FromSeconds(DurationSeconds) - elapsed;
                RemainingTime = remaining.TotalSeconds > 0
                    ? remaining.ToString(@"mm\:ss")
                    : "00:00";
                Progress = Math.Min(100, elapsed.TotalSeconds / DurationSeconds * 100);
            }
            else
            {
                RemainingTime = "∞";
                Progress = 0;
            }

            // 카운터 스냅샷
            long total = Interlocked.Read(ref _totalSent);
            long success = Interlocked.Read(ref _successReqs);
            long errors = Interlocked.Read(ref _errorReqs);
            long skip = Interlocked.Read(ref _skipped);
            long totalMs = Interlocked.Read(ref _totalRespMs);
            long minMs = Interlocked.Read(ref _minRespMs);
            long maxMs = Interlocked.Read(ref _maxRespMs);

            TotalRequests = total;
            SuccessCount = success;
            ErrorCount = errors;
            SkippedCount = skip;
            MinResponseMs = minMs == long.MaxValue ? 0 : minMs;
            MaxResponseMs = maxMs;
            AvgResponseMs = success > 0 ? totalMs / (double)success : 0;
            ErrorRate = total > 0 ? errors * 100.0 / total : 0;

            // 슬라이딩 윈도우 TPS (최근 3초)
            var cutoff = DateTime.UtcNow.AddSeconds(-3);
            while (_completionWindow.TryPeek(out var oldest) && oldest < cutoff)
                _completionWindow.TryDequeue(out _);

            CurrentTps = _completionWindow.Count / 3.0;
            AverageTps = elapsed.TotalSeconds > 0 ? total / elapsed.TotalSeconds : 0;

            if (CurrentTps > _peakTpsInternal) _peakTpsInternal = CurrentTps;
            PeakTps = _peakTpsInternal;

            StatusMessage = IsRunning
                ? $"테스트 중 — {total:N0}건 처리 · 성공 {success:N0} · 오류 {errors:N0} · 건너뜀 {skip:N0}"
                : StatusMessage;

            // 차트 — 1초마다 바 추가
            if ((DateTime.UtcNow - _lastChartUpdate).TotalSeconds >= 1.0)
            {
                _lastChartUpdate = DateTime.UtcNow;
                if (TpsHistory.Count >= TpsHistorySize)
                    TpsHistory.RemoveAt(0);
                TpsHistory.Add(TpsBarItem.Create(CurrentTps, TargetTps));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 완료 처리
        // ═══════════════════════════════════════════════════════════════

        private void FinalizeResults()
        {
            // 마지막 UI 갱신
            OnUiTimerTick(null, EventArgs.Empty);

            long total = Interlocked.Read(ref _totalSent);
            long success = Interlocked.Read(ref _successReqs);
            long errors = Interlocked.Read(ref _errorReqs);
            long skip = Interlocked.Read(ref _skipped);
            long totalMs = Interlocked.Read(ref _totalRespMs);
            long minMs = Interlocked.Read(ref _minRespMs);
            long maxMs = Interlocked.Read(ref _maxRespMs);
            double elapsed = _stopwatch.Elapsed.TotalSeconds;

            Summary = new BenchmarkSummary
            {
                ActualDurationSec = elapsed,
                TotalRequests = total,
                SuccessCount = success,
                ErrorCount = errors,
                SkippedCount = skip,
                AverageTps = elapsed > 0 ? total / elapsed : 0,
                PeakTps = _peakTpsInternal,
                AvgResponseMs = success > 0 ? totalMs / (double)success : 0,
                MinResponseMs = minMs == long.MaxValue ? 0 : minMs,
                MaxResponseMs = maxMs,
            };

            IsRunning = false;
            IsCompleted = true;
            RunButtonText = "▶  테스트 시작";
            StatusMessage = $"테스트 완료 · 평균 TPS {Summary.AverageTps:F2} · 오류율 {Summary.ErrorRate:F1}%";

            _snackbar.Show(
                "테스트 완료",
                $"총 {total:N0}건 · 평균 TPS {Summary.AverageTps:F2}",
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(4));
        }

        // ═══════════════════════════════════════════════════════════════
        // 헬퍼
        // ═══════════════════════════════════════════════════════════════

        private void ResetCounters()
        {
            _totalSent = _successReqs = _errorReqs = _skipped = _inFlight = _totalRespMs = _maxRespMs = 0;
            _minRespMs = long.MaxValue;
            _peakTpsInternal = 0;
            while (_completionWindow.TryDequeue(out _)) { }
        }

        private void UpdateMinMax(long ms)
        {
            // Min
            long prev = Interlocked.Read(ref _minRespMs);
            while (ms < prev)
            {
                long old = Interlocked.CompareExchange(ref _minRespMs, ms, prev);
                if (old == prev) break;
                prev = old;
            }
            // Max
            prev = Interlocked.Read(ref _maxRespMs);
            while (ms > prev)
            {
                long old = Interlocked.CompareExchange(ref _maxRespMs, ms, prev);
                if (old == prev) break;
                prev = old;
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            StopUiTimer();
            _service.Dispose();
        }
    }
}
