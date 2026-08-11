using FaceSearchApp.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace FaceSearchApp.Views
{
    public partial class VlmRealtimeEventExcludeTypeSettingsDialog : UserControl
    {
        private readonly ObservableCollection<EventTypeItem> _items;

        public VlmRealtimeEventExcludeTypeSettingsDialog(
            ObservableCollection<EventTypeItem> items,
            bool showNegativeConfidenceEvents,
            int delayTimeMilliseconds,
            bool useRealtimeAnalysisDelay,
            bool skipRealtimeAnalysisWhenBusy)
        {
            InitializeComponent();
            _items = items;
            DataContext = _items;
            ShowNegativeConfidenceBox.IsChecked = showNegativeConfidenceEvents;
            DelaySecondsBox.Value = Math.Max(0, delayTimeMilliseconds) / 1000.0;
            UseRealtimeDelayBox.IsChecked = useRealtimeAnalysisDelay;
            SkipAnalysisWhenBusyBox.IsChecked = skipRealtimeAnalysisWhenBusy;
        }

        public bool ShowNegativeConfidenceEvents => ShowNegativeConfidenceBox.IsChecked == true;
        public int DelayTimeMilliseconds => (int)Math.Round(Math.Max(0.0, DelaySecondsBox.Value ?? 0.0) * 1000);
        public bool UseRealtimeAnalysisDelay => UseRealtimeDelayBox.IsChecked == true;
        public bool SkipRealtimeAnalysisWhenBusy => SkipAnalysisWhenBusyBox.IsChecked == true;

        public void CommitEdits()
        {
            ExcludeTypeGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ExcludeTypeGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var key = KeyBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                return;

            var item = new EventTypeItem
            {
                Key = key,
                Display = string.IsNullOrWhiteSpace(DisplayBox.Text) ? key : DisplayBox.Text.Trim(),
                IsLiveEnabled = true
            };

            _items.Add(item);
            ExcludeTypeGrid.SelectedItem = item;
            ExcludeTypeGrid.ScrollIntoView(item);
            KeyBox.Text = string.Empty;
            DisplayBox.Text = string.Empty;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ExcludeTypeGrid.SelectedItem is EventTypeItem selected)
                _items.Remove(selected);
        }
    }
}
