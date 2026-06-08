using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FaceSearchApp.Models
{
    // ── 이미지 항목 ──────────────────────────────────────────────────

    public partial class BenchmarkImageItem : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileSizeText { get; set; } = string.Empty;
        public BitmapImage? Thumbnail { get; set; }

        [ObservableProperty] private string _loadStatus = "대기";   // 대기 / 로딩 중 / 완료 / 오류
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private string? _base64;                // 로드 완료 후 채워짐
    }

    // ── 요청 로그 항목 ──────────────────────────────────────────────

    public class BenchmarkLogItem
    {
        private static readonly Brush SuccessBrush = MakeBrush("#2ECC71");
        private static readonly Brush ErrorBrush = MakeBrush("#E74C3C");
        private static readonly Brush WarnBrush = MakeBrush("#F39C12");

        public long RequestNumber { get; init; }
        public string ImageName { get; init; } = string.Empty;
        public bool IsSuccess { get; init; }
        public long ResponseTimeMs { get; init; }
        public string Timestamp { get; init; } = DateTime.Now.ToString("HH:mm:ss.fff");
        public string ErrorMessage { get; init; } = string.Empty;
        public string RawPreview { get; init; } = string.Empty;   // 200자 미리보기

        public string StatusIcon => IsSuccess ? "✓" : "✗";
        public Brush StatusBrush => IsSuccess ? SuccessBrush : ErrorBrush;
        public Brush TimeBrush => ResponseTimeMs > 3000 ? WarnBrush
                                   : ResponseTimeMs > 1000 ? WarnBrush
                                   : SuccessBrush;

        private static SolidColorBrush MakeBrush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }

    // ── TPS 히스토리 차트 바 ────────────────────────────────────────

    public class TpsBarItem
    {
        private static readonly Brush GreenBrush = MakeBrush("#2ECC71");
        private static readonly Brush YellowBrush = MakeBrush("#F39C12");
        private static readonly Brush RedBrush = MakeBrush("#E74C3C");

        public double TpsValue { get; set; }
        public double BarHeight { get; set; }   // 0–80 px
        public Brush BarBrush { get; set; } = GreenBrush;

        public static TpsBarItem Create(double tps, double targetTps)
        {
            double ratio = targetTps > 0 ? tps / targetTps : 0;
            double barH = Math.Min(80, ratio * 80);
            Brush brush = ratio >= 0.9 ? GreenBrush
                            : ratio >= 0.5 ? YellowBrush
                            : RedBrush;
            return new TpsBarItem { TpsValue = tps, BarHeight = barH, BarBrush = brush };
        }

        private static SolidColorBrush MakeBrush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }

    // ── 최종 요약 결과 ──────────────────────────────────────────────

    public class BenchmarkSummary
    {
        public double ActualDurationSec { get; set; }
        public long TotalRequests { get; set; }
        public long SuccessCount { get; set; }
        public long ErrorCount { get; set; }
        public long SkippedCount { get; set; }
        public double AverageTps { get; set; }
        public double PeakTps { get; set; }
        public double AvgResponseMs { get; set; }
        public long MinResponseMs { get; set; }
        public long MaxResponseMs { get; set; }
        public double ErrorRate => TotalRequests > 0 ? ErrorCount * 100.0 / TotalRequests : 0;
    }
}
