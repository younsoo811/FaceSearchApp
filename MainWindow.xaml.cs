using FaceSearchApp.ViewModels;
using FaceSearchApp.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FaceSearchApp
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindowViewModel ViewModel { get; }

        private readonly ISnackbarService _snackbarService;
        private readonly INavigationService _navigationService;
        private NavigationViewItem? _activeNav;

        public MainWindow(MainWindowViewModel viewModel, ISnackbarService snackbarService, INavigationService navigationService, IContentDialogService dialogService)
        {
            InitializeComponent();

            ViewModel = viewModel;
            DataContext = this;

            _snackbarService = snackbarService;
            _navigationService = navigationService;
            dialogService.SetContentPresenter(RootContentDialogPresenter);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // NavigationView 연결
            _navigationService.SetNavigationControl(SideNav);

            // Snackbar 연결
            _snackbarService.SetSnackbarPresenter(SnackbarPresenter);

            // 첫 페이지 이동
            _navigationService.Navigate(typeof(SearchPage));
        }
    }
}
