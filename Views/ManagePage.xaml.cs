using FaceSearchApp.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FaceSearchApp.Views
{
    /// <summary>
    /// ManagePage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class ManagePage : Page
    {
        private readonly ManageViewModel _vm;

        public ManagePage(ManageViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // Wire up confirmation dialog
            _vm.ConfirmDelete = count =>
                MessageBox.Show(
                    $"선택한 {count}건을 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                    "삭제 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) == MessageBoxResult.Yes;
        }
    }
}
