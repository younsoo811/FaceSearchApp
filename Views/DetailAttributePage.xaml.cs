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
    /// DetailAttributePage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class DetailAttributePage : Page
    {
        private DetailAttributeViewModel? _vm;

        public DetailAttributePage(DetailAttributeViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;
        }

        private void QueryZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0) _vm?.LoadImageFromPath(files[0]);
            }
            QueryDropZone.Opacity = 1.0;
        }

        private void QueryZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            QueryDropZone.Opacity = e.Effects == DragDropEffects.Copy ? 0.7 : 1.0;
            e.Handled = true;
        }

        private void QueryZone_DragLeave(object sender, DragEventArgs e) =>
            QueryDropZone.Opacity = 1.0;

        private void QueryZone_Click(object sender, MouseButtonEventArgs e) =>
            _vm?.SelectImageCommand.Execute(null);

        private void SelectFile_Click(object sender, RoutedEventArgs e) =>
            _vm?.SelectImageCommand.Execute(null);
    }
}
