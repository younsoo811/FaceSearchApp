using FaceSearchApp.ViewModels;
using FaceSearchApp.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    /// VlmPage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class VlmPage : Page
    {
        private VlmViewModel? _vm;

        public VlmPage(VlmViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;
            _vm.RealtimeEvents.CollectionChanged += RealtimeEvents_CollectionChanged;
        }

        // ══════════════════════════════════════════════════════
        // 드래그 & 드롭 이벤트 핸들러
        // ══════════════════════════════════════════════════════

        private void QueryZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0)
                    _vm?.LoadImageFromPath(files[0]);
            }

            // 드래그 상태 초기화
            QueryDropZone.Opacity = 1.0;
        }

        private void QueryZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                QueryDropZone.Opacity = 0.7;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void QueryZone_DragLeave(object sender, DragEventArgs e)
        {
            QueryDropZone.Opacity = 1.0;
        }

        private void QueryZone_Click(object sender, MouseButtonEventArgs e)
        {
            _vm?.SelectImageCommand.Execute(null);
        }

        private void SelectFile_Click(object sender, RoutedEventArgs e)
        {
            _vm?.SelectImageCommand.Execute(null);
        }

        private void AnalyzeRealtimeEventMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_vm is null || sender is not MenuItem menuItem ||
                menuItem.DataContext is not RealtimeEventItem eventItem)
            {
                return;
            }

            if (_vm.AnalyzeRealtimeEventCommand.CanExecute(eventItem))
                _vm.AnalyzeRealtimeEventCommand.Execute(eventItem);
        }

        private bool _isRealtimeItemPressed;

        private void RealtimeEvents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_isRealtimeItemPressed && e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == 0)
                Dispatcher.BeginInvoke(() => RealtimeEventsScrollViewer.ScrollToTop());
        }

        private void RealtimeEventItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_vm is null || sender is not FrameworkElement element || element.DataContext is not RealtimeEventItem eventItem)
                return;

            _isRealtimeItemPressed = true;

            var analysisItem = _vm.SelectRealtimeEvent(eventItem);
            if (analysisItem is null)
            {
                _isRealtimeItemPressed = false;
                return;
            }

            ScrollAnalysisItemIntoView(analysisItem);
        }

        private void RealtimeEventItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isRealtimeItemPressed = true;

            if (_vm is null || sender is not FrameworkElement element || element.DataContext is not RealtimeEventItem eventItem)
                return;

            element.Focus();
            _vm.SelectRealtimeEvent(eventItem);
        }

        private void ScrollAnalysisItemIntoView(AnalysisItem analysisItem)
        {
            AnalysisHistoryItemsControl.UpdateLayout();

            var container = AnalysisHistoryItemsControl.ItemContainerGenerator.ContainerFromItem(analysisItem) as FrameworkElement;
            if (container is null)
            {
                Dispatcher.BeginInvoke(() => ScrollAnalysisItemIntoView(analysisItem));
                return;
            }

            container.BringIntoView();
        }
    }
}
