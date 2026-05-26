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

            PasswordBox.PasswordChanged += (s, e) =>
            {
                if (_vm is not null)
                    _vm.Password = PasswordBox.Password;
            };
        }
    }
}
