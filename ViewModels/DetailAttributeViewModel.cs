using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceSearchApp.Models;
using FaceSearchApp.Services;
using FaceSearchApp.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FaceSearchApp.ViewModels
{
    public partial class DetailAttributeViewModel : ObservableObject, IDisposable
    {
        private readonly ISnackbarService _snackbarService;
        private readonly AttributeAnalysisService _service = new();

        // ── 서버 연결 설정 ────────────────────────────────────────────
        [ObservableProperty] private string _baseUrl = "https://studio";
        [ObservableProperty] private string _serverUsername = "local";
        [ObservableProperty] private string _serverPassword = "gN4spw9+zhwwspkTSX6fpA==";
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private string _connectionStatus = "연결되지 않음";
        [ObservableProperty] private string _connectButtonText = "연결";

        // ── 이미지 선택 ───────────────────────────────────────────────
        [ObservableProperty] private BitmapImage? _queryImage;
        [ObservableProperty] private string _imageInfo = string.Empty;
        private string? _selectedImagePath;

        // ── 분석 상태 ─────────────────────────────────────────────────
        [ObservableProperty] private bool _isAnalyzing;
        [ObservableProperty] private string _statusMessage = "세부속성 서버 연결 상태를 확인해주세요.";

        // ── 분석 히스토리 ─────────────────────────────────────────────
        public ObservableCollection<AttributeResultItem> AnalysisHistory { get; } = new();

        public DetailAttributeViewModel(ISnackbarService snackbarService)
        {
            _snackbarService = snackbarService;
        }

        // ═══════════════════════════════════════════════════════════════
        // 서버 연결 / 해제
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

            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                StatusMessage = "서버 주소를 입력하세요.";
                return;
            }

            ConnectionStatus = "연결 중...";
            StatusMessage = string.Empty;

            var (success, error) = await _service.ConnectAsync(BaseUrl, ServerUsername, ServerPassword);

            if (success)
            {
                IsConnected = true;
                ConnectionStatus = $"연결됨 · {BaseUrl}";
                ConnectButtonText = "연결 해제";
                StatusMessage = "서버 연결 성공.";
                _snackbarService.Show("연결 성공", "세부속성 서버에 연결되었습니다.",
                    ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), TimeSpan.FromSeconds(2));
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "연결 실패";
                ConnectButtonText = "연결";
                StatusMessage = $"연결 실패: {error}";
                _snackbarService.Show("연결 실패", error,
                    ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 이미지 선택
        // ═══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void SelectImage()
        {
            var dialog = new OpenFileDialog
            {
                Title = "분석할 이미지 선택",
                Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.webp|모든 파일|*.*",
            };
            if (dialog.ShowDialog() == true)
                LoadImageFromPath(dialog.FileName);
        }

        public void LoadImageFromPath(string path)
        {
            if (!File.Exists(path)) { StatusMessage = "파일을 찾을 수 없습니다."; return; }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" }.Contains(ext))
            {
                StatusMessage = "지원하지 않는 이미지 형식입니다.";
                return;
            }

            try
            {
                _selectedImagePath = path;
                QueryImage = LoadBitmapImage(path);

                var fi = new FileInfo(path);
                var size = fi.Length < 1024 * 1024
                    ? $"{fi.Length / 1024.0:F1} KB"
                    : $"{fi.Length / 1024.0 / 1024.0:F2} MB";

                ImageInfo = $"{Path.GetFileName(path)} · {QueryImage.PixelWidth}×{QueryImage.PixelHeight} · {size}";
                Debug.WriteLine($"Pixel : {QueryImage.PixelWidth} x {QueryImage.PixelHeight}");
                Debug.WriteLine($"Width : {QueryImage.Width}");
                Debug.WriteLine($"Height: {QueryImage.Height}");
                Debug.WriteLine($"DpiX  : {QueryImage.DpiX}");
                Debug.WriteLine($"DpiY  : {QueryImage.DpiY}");
                StatusMessage = "이미지가 선택되었습니다.";
            }
            catch (Exception ex) { StatusMessage = $"이미지 로드 실패: {ex.Message}"; }
        }

        [RelayCommand]
        private void ClearQuery()
        {
            QueryImage = null;
            ImageInfo = string.Empty;
            _selectedImagePath = null;
            StatusMessage = "이미지가 초기화되었습니다.";
        }

        // ═══════════════════════════════════════════════════════════════
        // 속성 분석
        // ═══════════════════════════════════════════════════════════════

        [RelayCommand]
        private async Task AnalyzeAsync()
        {
            if (!_service.IsConnected)
            {
                _snackbarService.Show("분석 요청 실패", "서버 연결 상태를 확인해주세요.",
                    ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
                return;
            }
            if (_selectedImagePath is null || QueryImage is null)
            {
                StatusMessage = "분석할 이미지를 선택하세요.";
                return;
            }

            IsAnalyzing = true;
            StatusMessage = "속성 분석 중...";

            // 로딩 플레이스홀더 아이템 추가
            var placeholder = new AttributeResultItem
            {
                Image = QueryImage,
                ImageInfo = ImageInfo,
                IsAnalyzing = true,
            };
            AnalysisHistory.Insert(0, placeholder);

            try
            {
                var bytes = await File.ReadAllBytesAsync(_selectedImagePath);
                var base64 = Convert.ToBase64String(bytes);

                var (success, error, data) = await _service.AnalyzeAsync(base64);

                if (!success || data is null)
                {
                    AnalysisHistory.Remove(placeholder);
                    StatusMessage = $"분석 실패: {error}";
                    _snackbarService.Show("분석 실패", error,
                        ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
                    return;
                }

                // 플레이스홀더 제거 후 인원별 결과 삽입
                AnalysisHistory.Remove(placeholder);

                var items = AttributeParser.ParseAll(data, QueryImage, ImageInfo);
                if (items.Count == 0)
                {
                    StatusMessage = "감지된 인물이 없습니다.";
                    _snackbarService.Show("분석 완료", "이미지에서 인물을 감지하지 못했습니다.",
                        ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(3));
                    return;
                }

                // 최신이 맨 위 → 역순 삽입
                foreach (var item in Enumerable.Reverse(items))
                    AnalysisHistory.Insert(0, item);

                StatusMessage = $"분석 완료 · {items.Count}명 감지";
                _snackbarService.Show("분석 완료",
                    $"{items.Count}명의 인물 속성을 분석하였습니다.",
                    ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                AnalysisHistory.Remove(placeholder);
                StatusMessage = $"오류: {ex.Message}";
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 히스토리 관리
        // ═══════════════════════════════════════════════════════════════

        [RelayCommand]
        private void DeleteItem(AttributeResultItem item)
        {
            if (item is not null)
                AnalysisHistory.Remove(item);
        }

        [RelayCommand]
        private void ClearHistory()
        {
            AnalysisHistory.Clear();
            StatusMessage = "분석 기록이 초기화되었습니다.";
        }

        // ═══════════════════════════════════════════════════════════════
        // 헬퍼
        // ═══════════════════════════════════════════════════════════════

        private static BitmapImage LoadBitmapImage(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            //bmp.DecodePixelWidth = 1200;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        public void Dispose() => _service.Dispose();
    }
}
