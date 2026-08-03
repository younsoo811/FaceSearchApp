using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FaceSearchApp.Models
{
    public partial class AnalysisItem : ObservableObject
    {
        public Guid Id { get; } = Guid.NewGuid();

        private DispatcherTimer? _typingTimer;
        private DispatcherTimer? _confidenceTimer;
        private int _typingIndex;

        [ObservableProperty]
        private BitmapImage? _image;

        [ObservableProperty]
        private string _timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        [ObservableProperty]
        private bool _isAnalyzing = true;

        [ObservableProperty]
        private string _description = string.Empty;

        // 타이핑 표시용
        [ObservableProperty]
        private string _animatedDescription = string.Empty;

        [ObservableProperty]
        private string _result = string.Empty;

        [ObservableProperty]
        private string _analysisTypeDisplay = string.Empty;

        [ObservableProperty]
        private string _decision = string.Empty;

        [ObservableProperty]
        private bool _hasConfidence;

        [ObservableProperty]
        private double _confidencePercent;

        [ObservableProperty]
        private double _animatedConfidencePercent;

        [ObservableProperty]
        private string _imageInfo = string.Empty;

        partial void OnDecisionChanged(string value)
        {
            OnPropertyChanged(nameof(FireLabel));
            OnPropertyChanged(nameof(FireBackground));
            OnPropertyChanged(nameof(FireBorderBrush));
        }

        partial void OnConfidencePercentChanged(double value)
        {
            OnPropertyChanged(nameof(ConfidenceBrush));
            OnPropertyChanged(nameof(ConfidenceTrackBrush));
        }

        partial void OnAnimatedConfidencePercentChanged(double value)
        {
            OnPropertyChanged(nameof(ConfidenceDisplay));
        }

        partial void OnAnalysisTypeDisplayChanged(string value)
        {
            OnPropertyChanged(nameof(ResultTitle));
        }

        // Description 값 변경 시 자동 타이핑 시작
        partial void OnDescriptionChanged(string value)
        {
            StartTypingAnimation(value);
        }

        public void SetConfidenceScore(double? score)
        {
            HasConfidence = score.HasValue;

            if (!score.HasValue)
            {
                ConfidencePercent = 0;
                AnimatedConfidencePercent = 0;
                _confidenceTimer?.Stop();
                return;
            }

            ConfidencePercent = Math.Clamp(score.Value, 0, 1) * 100;
            StartConfidenceAnimation(ConfidencePercent);
        }


        private void StartTypingAnimation(string text)
        {
            _typingTimer?.Stop();

            AnimatedDescription = string.Empty;
            _typingIndex = 0;

            if (string.IsNullOrWhiteSpace(text))
                return;

            _typingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(5)
            };

            _typingTimer.Tick += (s, e) =>
            {
                if (_typingIndex >= text.Length)
                {
                    _typingTimer.Stop();
                    return;
                }

                AnimatedDescription += text[_typingIndex];
                _typingIndex++;
            };

            _typingTimer.Start();
        }

        private void StartConfidenceAnimation(double targetPercent)
        {
            _confidenceTimer?.Stop();
            AnimatedConfidencePercent = 0;

            const int durationMs = 900;
            const int intervalMs = 15;
            var elapsedMs = 0;

            _confidenceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs)
            };

            _confidenceTimer.Tick += (s, e) =>
            {
                elapsedMs += intervalMs;
                var progress = Math.Min(1, elapsedMs / (double)durationMs);
                var eased = 1 - Math.Pow(1 - progress, 3);

                AnimatedConfidencePercent = targetPercent * eased;

                if (progress >= 1)
                {
                    AnimatedConfidencePercent = targetPercent;
                    _confidenceTimer.Stop();
                }
            };

            _confidenceTimer.Start();
        }

        public string ConfidenceDisplay => $"{AnimatedConfidencePercent:0}%";

        public string ResultTitle => string.IsNullOrWhiteSpace(AnalysisTypeDisplay)
            ? "분석 결과"
            : $"{AnalysisTypeDisplay} 분석 결과";

        public Brush ConfidenceBrush
        {
            get
            {
                var color = ConfidencePercent >= 80 ? Color.FromRgb(76, 175, 80)
                          : ConfidencePercent >= 65 ? Color.FromRgb(255, 167, 38)
                                                    : Color.FromRgb(239, 83, 80);

                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }

        public Brush ConfidenceTrackBrush
        {
            get
            {
                var color = ConfidencePercent >= 80 ? Color.FromRgb(232, 245, 233)
                          : ConfidencePercent >= 65 ? Color.FromRgb(255, 243, 224)
                                                    : Color.FromRgb(255, 235, 238);

                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }

        public string FireLabel => Decision switch
        {
            "0" => "오경보",
            "1" => "정상",
            _ => string.Empty
        };

        // 배경색
        public Brush FireBackground => Decision switch
        {
            "0" => new SolidColorBrush(Color.FromRgb(255, 235, 238)), // 연한 빨강
            "1" => new SolidColorBrush(Color.FromRgb(232, 245, 233)), // 연한 초록
            _ => Brushes.Transparent
        };

        // 테두리색
        public Brush FireBorderBrush => Decision switch
        {
            "0" => new SolidColorBrush(Color.FromRgb(244, 67, 54)), // 빨강
            "1" => new SolidColorBrush(Color.FromRgb(76, 175, 80)), // 초록
            _ => Brushes.Transparent
        };

        // 글자색
        public Brush FireForeground => Decision switch
        {
            "0" => new SolidColorBrush(Color.FromRgb(198, 40, 40)),
            "1" => new SolidColorBrush(Color.FromRgb(46, 125, 50)),
            _ => Brushes.Black
        };
    }
}
