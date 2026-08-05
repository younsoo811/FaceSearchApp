using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceSearchApp.Models;
using FaceSearchApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace FaceSearchApp.ViewModels
{
    public partial class SearchViewModel : ObservableObject
    {
        private readonly FaceApiService _api;

        // ── Query image ──────────────────────────────────────────────
        [ObservableProperty] private BitmapImage? _queryImage;
        [ObservableProperty] private string _queryBase64 = string.Empty;
        [ObservableProperty] private string _imageInfo = string.Empty;

        // ── Search options ───────────────────────────────────────────
        [ObservableProperty] private bool _isNormalType = true;
        [ObservableProperty] private bool _isTargetType;
        [ObservableProperty] private float _minScore = 0.5f;
        [ObservableProperty] private DateTime? _startDate;
        [ObservableProperty] private DateTime? _endDate;

        // ── State ────────────────────────────────────────────────────
        [ObservableProperty] private bool _isSearching;
        [ObservableProperty] private string _statusMessage = "이미지를 불러오면 검색을 시작할 수 있습니다.";
        [ObservableProperty] private bool _hasResults;
        [ObservableProperty] private string _resultHeader = "검색 결과";

        public ObservableCollection<SearchResultItemViewModel> Results { get; } = [];

        public SearchViewModel(FaceApiService api) => _api = api;

        // ── Image loading ─────────────────────────────────────────────
        public bool TryLoadImage(string filePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);
                var base64 = Convert.ToBase64String(bytes);

                // Validate magic bytes
                var (valid, format, error) = ValidateImageBytes(bytes);
                if (!valid)
                {
                    StatusMessage = $"❌ {error}";
                    return false;
                }

                var bitmap = CreateBitmap(bytes);
                QueryImage = bitmap;
                QueryBase64 = base64;
                ImageInfo = $"{format}  ·  {bitmap.PixelWidth}×{bitmap.PixelHeight}  ·  {bytes.Length / 1024} KB";
                StatusMessage = "✅ 이미지 로드 완료 — 검색 버튼을 눌러주세요.";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ 파일 읽기 오류: {ex.Message}";
                return false;
            }
        }

        // ── Search ────────────────────────────────────────────────────
        [RelayCommand]
        private async Task SearchAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(QueryBase64))
            {
                StatusMessage = "⚠️ 먼저 이미지를 선택해주세요.";
                return;
            }

            IsSearching = true;
            HasResults = false;
            Results.Clear();
            StatusMessage = "🔍 검색 중...";

            try
            {
                var searchType = IsTargetType ? SearchType.Target : SearchType.Normal;
                var response = await _api.SearchAsync(QueryBase64, searchType,
                                                        StartDate, EndDate, MinScore, ct);

                if (response?.Success != true || response.Data is null)
                {
                    StatusMessage = $"❌ {response?.Msg ?? "검색에 실패했습니다."}";
                    return;
                }

                var hits = response.Data;
                if (hits.Count == 0)
                {
                    StatusMessage = "검색 결과가 없습니다. 최소 유사도를 낮춰보세요.";
                    ResultHeader = "검색 결과 (0건)";
                    return;
                }

                var imageMap = await GetImagesSkippingInvalidIdsAsync(hits, ct);

                foreach (var hit in hits)
                {
                    var vm = new SearchResultItemViewModel
                    {
                        ImageId = hit.ImageId,
                        SubId = hit.SubId,
                        Score = hit.Score
                    };

                    if (imageMap.TryGetValue(hit.ImageId, out var b64))
                        vm.LoadImage(b64);

                    Results.Add(vm);
                }

                HasResults = true;
                ResultHeader = $"검색 결과 ({Results.Count}건)";
                var missingImageCount = Results.Count(r => r.Image is null);
                StatusMessage = missingImageCount > 0
                    ? $"✅ {Results.Count}개의 유사 얼굴을 찾았습니다. 이미지 {missingImageCount}건은 불러오지 못했습니다."
                    : $"✅ {Results.Count}개의 유사 얼굴을 찾았습니다.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "검색이 취소되었습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ 오류: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        [RelayCommand]
        private void ClearDates()
        {
            StartDate = null;
            EndDate = null;
        }

        [RelayCommand]
        private void ClearQuery()
        {
            QueryImage = null;
            QueryBase64 = string.Empty;
            ImageInfo = string.Empty;
            StatusMessage = "이미지를 불러오면 검색을 시작할 수 있습니다.";
            Results.Clear();
            HasResults = false;
            ResultHeader = "검색 결과";
        }

        // ── Helpers ───────────────────────────────────────────────────
        private async Task<Dictionary<string, string>> GetImagesSkippingInvalidIdsAsync(
            List<SearchResultItem> hits,
            CancellationToken ct)
        {
            var imageMap = new Dictionary<string, string>();
            var imageIds = hits.Select(h => h.ImageId)
                               .Where(id => !string.IsNullOrWhiteSpace(id))
                               .Distinct()
                               .ToList();

            if (imageIds.Count == 0)
                return imageMap;

            var imgResp = await _api.GetImagesAsync(imageIds, ct);
            if (imgResp?.Success == true)
            {
                imageMap = imgResp.Data?.Images
                            .GroupBy(i => i.Id)
                            .ToDictionary(g => g.Key, g => g.First().Base64)
                           ?? imageMap;
                return imageMap;
            }

            if (!IsInvalidIdFormatError(imgResp?.Msg))
                return imageMap;

            foreach (var imageId in imageIds)
            {
                ct.ThrowIfCancellationRequested();

                var singleResp = await _api.GetImagesAsync([imageId], ct);
                if (singleResp?.Success == true)
                {
                    var image = singleResp.Data?.Images.FirstOrDefault(i => i.Id == imageId);
                    if (image is not null)
                        imageMap[image.Id] = image.Base64;
                }
            }

            return imageMap;
        }

        private static bool IsInvalidIdFormatError(string? message)
            => !string.IsNullOrWhiteSpace(message)
               && message.Contains("유효하지 않은 ID 형식", StringComparison.OrdinalIgnoreCase);

        private static BitmapImage CreateBitmap(byte[] bytes)
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static (bool, string?, string?) ValidateImageBytes(byte[] bytes)
        {
            if (bytes.Length < 8) return (false, null, "파일이 너무 작습니다.");

            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return (true, "JPEG", null);
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E) return (true, "PNG", null);
            if (bytes[0] == 0x42 && bytes[1] == 0x4D) return (true, "BMP", null);
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46) return (true, "WEBP", null);
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return (true, "GIF", null);

            return (false, null, "지원하지 않는 형식입니다. (JPEG, PNG, BMP, WEBP, GIF)");
        }
    }

    // ── Result card ViewModel ────────────────────────────────────────
    public partial class SearchResultItemViewModel : ObservableObject
    {
        public string ImageId { get; set; } = string.Empty;
        public string SubId { get; set; } = string.Empty;
        public float Score { get; set; }

        [ObservableProperty] private BitmapImage? _image;

        public string ScoreText => Score.ToString("P1");
        public int ScorePercent => (int)(Score * 100);

        public void LoadImage(string base64)
        {
            try
            {
                var raw = base64.Contains(',') ? base64[(base64.IndexOf(',') + 1)..] : base64;
                var bytes = Convert.FromBase64String(raw);
                var bmp = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                Image = bmp;
            }
            catch { /* keep Image null */ }
        }
    }
}
