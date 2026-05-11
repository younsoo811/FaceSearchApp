using FaceSearchApp.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FaceSearchApp.Views
{
    /// <summary>
    /// RegisterPage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class RegisterPage : Page
    {
        private readonly RegisterViewModel _vm;

        public RegisterPage(RegisterViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        // ── Drop Zone events ─────────────────────────────────────────
        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
            SetDropZoneHighlight(true);
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            SetDropZoneHighlight(false);
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            SetDropZoneHighlight(false);
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            var file = files.FirstOrDefault(f => IsImageFile(f));
            if (file is not null) _vm.TryLoadImage(file);
        }

        private void DropZone_Click(object sender, MouseButtonEventArgs e)
            => SelectImageFile();

        private void SelectFile_Click(object sender, RoutedEventArgs e)
            => SelectImageFile();

        private void SelectImageFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "이미지 파일 선택",
                Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|모든 파일|*.*"
            };
            if (dlg.ShowDialog() == true)
                _vm.TryLoadImage(dlg.FileName);
        }

        private void SetDropZoneHighlight(bool on)
        {
            DropZone.BorderBrush = on
                ? new SolidColorBrush(Color.FromRgb(0x60, 0xC0, 0xFF))
                : (SolidColorBrush)FindResource("ControlStrokeColorDefaultBrush");
        }

        private static bool IsImageFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" }.Contains(ext);
        }
    }
}
