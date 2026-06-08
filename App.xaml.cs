using FaceSearchApp.Services;
using FaceSearchApp.ViewModels;
using FaceSearchApp.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace FaceSearchApp
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        private readonly IHost _host;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(cfg =>
                    cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true))
                .ConfigureServices((ctx, services) =>
                {
                    var baseUrl = ctx.Configuration["ApiSettings:BaseUrl"]
                                  ?? "http://localhost:18885/api/face/";

                    // ── API HttpClient ──────────────────────────────────
                    services.AddHttpClient<FaceApiService>(c =>
                        c.BaseAddress = new Uri(baseUrl));

                    services.AddNavigationViewPageProvider();

                    // ── WPF-UI 서비스 ───                    
                    services.AddSingleton<ISnackbarService, SnackbarService>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<IContentDialogService, ContentDialogService>();
                    services.AddSingleton<MqttClientService>();
                    services.AddSingleton<AttributeAnalysisService>();
                    services.AddSingleton<BenchmarkService>();

                    // ── ViewModels ──────────────────────────────────────
                    services.AddTransient<SearchViewModel>();
                    services.AddTransient<RegisterViewModel>();
                    services.AddTransient<ManageViewModel>();
                    services.AddTransient<SettingViewModel>();
                    services.AddTransient<FaceRegistrationViewModel>();
                    services.AddSingleton<VlmViewModel>();
                    services.AddSingleton<DetailAttributeViewModel>();
                    services.AddSingleton<BenchmarkViewModel>();

                    // ── Pages ───────────────────────────────────────────
                    services.AddTransient<SearchPage>();
                    services.AddTransient<RegisterPage>();
                    services.AddTransient<ManagePage>();
                    services.AddTransient<SettingPage>();
                    services.AddTransient<FaceRegistrationPage>();
                    services.AddSingleton<VlmPage>();
                    services.AddSingleton<DetailAttributePage>();
                    services.AddSingleton<BenchmarkPage>();

                    // ── Shell ───────────────────────────────────────────
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainWindowViewModel>();

                })
                .Build();

            Services = _host.Services;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await _host.StartAsync();
            Services.GetRequiredService<MainWindow>().Show();
            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await _host.StopAsync();
            _host.Dispose();
            base.OnExit(e);
        }
    }
}
