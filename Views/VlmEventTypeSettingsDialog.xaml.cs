using FaceSearchApp.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace FaceSearchApp.Views
{
    public partial class VlmEventTypeSettingsDialog : UserControl
    {
        private readonly ObservableCollection<EventTypeItem> _items;

        public VlmEventTypeSettingsDialog(ObservableCollection<EventTypeItem> items)
        {
            InitializeComponent();
            _items = items;
            DataContext = _items;
        }

        public void CommitEdits()
        {
            EventTypeGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            EventTypeGrid.CommitEdit(DataGridEditingUnit.Row, true);
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
            EventTypeGrid.SelectedItem = item;
            EventTypeGrid.ScrollIntoView(item);
            KeyBox.Text = string.Empty;
            DisplayBox.Text = string.Empty;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (EventTypeGrid.SelectedItem is EventTypeItem selected)
                _items.Remove(selected);
        }
    }
}
