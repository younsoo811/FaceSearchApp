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
    public partial class AnalysisItem : ObservableObject
    {
        public Guid Id { get; } = Guid.NewGuid();

        [ObservableProperty]
        private BitmapImage? _image;

        [ObservableProperty]
        private string _timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        [ObservableProperty]
        private bool _isAnalyzing = true;

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private string _result = string.Empty;

        [ObservableProperty]
        private string _decision = string.Empty;

        [ObservableProperty]
        private string _imageInfo = string.Empty;

        partial void OnDecisionChanged(string value)
        {
            OnPropertyChanged(nameof(FireLabel));
            OnPropertyChanged(nameof(FireBackground));
            OnPropertyChanged(nameof(FireBorderBrush));
        }
        public string FireLabel => Decision switch
        {
            "0" => "오탐",
            "1" => "정탐",
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
