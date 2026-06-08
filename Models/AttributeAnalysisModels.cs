using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FaceSearchApp.Models
{
    // Sense model
    // ── 로그인 ────────────────────────────────────────────────────────

    public class LoginRequest
    {
        [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
        [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
        [JsonPropertyName("accountType")] public string AccountType { get; set; } = "2";
    }

    public class LoginResponse
    {
        [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
        [JsonPropertyName("msg")] public string Msg { get; set; } = string.Empty;
        [JsonPropertyName("data")] public string Data { get; set; } = string.Empty; // token
        [JsonPropertyName("success")] public bool Success { get; set; }
    }

    // ── 속성 분석 ─────────────────────────────────────────────────────

    public class AttributeApiResponse
    {
        [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
        [JsonPropertyName("msg")] public string Msg { get; set; } = string.Empty;
        [JsonPropertyName("data")] public AttributeApiData? Data { get; set; }
        [JsonPropertyName("success")] public bool Success { get; set; }
    }

    public class AttributeApiData
    {
        [JsonPropertyName("pedestrian")]
        public List<PedestrianData>? Pedestrian { get; set; }
    }

    public class PedestrianData
    {
        [JsonPropertyName("detect")] public List<List<int>>? Detect { get; set; }
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("attribute")] public Dictionary<string, Dictionary<string, double>>? Attribute { get; set; }
    }

    /// <summary>
    /// AttributeResultItem
    /// </summary>
    public partial class AttributeResultItem : ObservableObject
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Timestamp { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        [ObservableProperty] private BitmapImage? _image;       // 크롭된 인물 이미지
        [ObservableProperty] private BitmapImage? _fullImage;   // 원본 이미지
        [ObservableProperty] private string _imageInfo = string.Empty;
        [ObservableProperty] private string _personLabel = string.Empty; 
        [ObservableProperty] private bool _isAnalyzing = true;

        public List<AttributeDisplayGroup> AttributeGroups { get; set; } = new();
    }

    // ── 표시용 계층 모델 ─────────────────────────────────────────────

    public class AttributeDisplayGroup
    {
        public string GroupName { get; set; } = string.Empty;
        public List<AttributeDisplayItem> Items { get; set; } = new();
    }

    public class AttributeDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string ConfidenceText => $"{Confidence * 100:F0}%";
        public bool IsColor { get; set; }
        public List<ColorChip> TopColors { get; set; } = new();

        // 색상 속성 전용: 지배적 색상 브러시
        public SolidColorBrush? DominantBrush => TopColors.FirstOrDefault()?.Brush;
        public string DominantColorName => TopColors.FirstOrDefault()?.KoreanName ?? string.Empty;
    }

    public class ColorChip
    {
        public string KoreanName { get; set; } = string.Empty;
        public string Hex { get; set; } = string.Empty;
        public double Value { get; set; }
        public string PercentText => $"{Value * 100:F0}%";

        private SolidColorBrush? _brush;
        public SolidColorBrush Brush
        {
            get
            {
                if (_brush != null) return _brush;
                try { _brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex)); _brush.Freeze(); }
                catch { _brush = Brushes.Transparent; }
                return _brush;
            }
        }
    }
}
