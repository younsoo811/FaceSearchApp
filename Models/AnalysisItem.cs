using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        private string _dicision = "0";

        [ObservableProperty]
        private string _imageInfo = string.Empty;
    }
}
