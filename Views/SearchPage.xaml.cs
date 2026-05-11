using FaceSearchApp.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FaceSearchApp.Views
{
    /// <summary>
    /// SearchPage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class SearchPage : Page
    {
        private readonly SearchViewModel _vm;

        public SearchPage(SearchViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        // ── Drop Zone events ─────────────────────────────────────────
        private void QueryZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
            SetDropZoneHighlight(true);
        }

        private void QueryZone_DragLeave(object sender, DragEventArgs e)
        {
            SetDropZoneHighlight(false);
        }

        private void QueryZone_Drop(object sender, DragEventArgs e)
        {
            SetDropZoneHighlight(false);
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            var file = files.FirstOrDefault(f => IsImageFile(f));
            if (file is not null) _vm.TryLoadImage(file);
        }

        private void QueryZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
            QueryDropZone.BorderBrush = on
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
