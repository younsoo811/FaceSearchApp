using FaceSearchApp.Models;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FaceSearchApp.Views
{
    public partial class VlmAnalysisDetailDialog : UserControl
    {
        private readonly ISnackbarService _snackbarService;

        public static readonly DependencyProperty IsFireModeProperty =
            DependencyProperty.Register(
                nameof(IsFireMode),
                typeof(bool),
                typeof(VlmAnalysisDetailDialog),
                new PropertyMetadata(true));

        public bool IsFireMode
        {
            get => (bool)GetValue(IsFireModeProperty);
            set => SetValue(IsFireModeProperty, value);
        }

        public VlmAnalysisDetailDialog(AnalysisItem item, bool isFireMode, ISnackbarService snackbarService)
        {
            InitializeComponent();
            _snackbarService = snackbarService;
            IsFireMode = isFireMode;
            DataContext = item;
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AnalysisItem item || item.Image is null)
                return;

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

            if (dialog.ShowDialog() != true)
                return;

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

        private void CopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AnalysisItem item || item.Image is null)
                return;

            Clipboard.SetImage(item.Image);
            _snackbarService.Show(
                "복사 완료",
                "이미지가 클립보드에 복사되었습니다.",
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(2));
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        }
    }
}
