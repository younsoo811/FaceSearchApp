using CommunityToolkit.Mvvm.ComponentModel;
using FaceSearchApp.Views;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FaceSearchApp.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<object> _menuItems =
        [
            new NavigationViewItem("얼굴 검색",   SymbolRegular.PersonSearch24, typeof(SearchPage)),
            new NavigationViewItem("이미지 등록", SymbolRegular.PersonAdd24,    typeof(RegisterPage)),
            new NavigationViewItem("등록 관리",   SymbolRegular.PeopleList24,   typeof(ManagePage)),
            new NavigationViewItem("얼굴 촬영", SymbolRegular.VideoPersonCall24, typeof(FaceRegistrationPage)),
            new NavigationViewItem("VLM", SymbolRegular.ImageAltText24, typeof(VlmPage)),
            new NavigationViewItem("세부속성 분석", SymbolRegular.PersonStanding16, typeof(DetailAttributePage))
        ];

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems = 
            [
                new NavigationViewItem("설정", SymbolRegular.Settings24, typeof(SettingPage))
            ];

        public MainWindowViewModel()
        {
        }
    }
}
