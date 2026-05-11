using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace FaceSearchApp.ViewModels
{
    public partial class SettingViewModel : ObservableObject
    {
        private readonly ISnackbarService _snackbarService;
        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        [ObservableProperty] private string _ipAddress = "localhost";
        [ObservableProperty] private string _port = "18885";

        public SettingViewModel(ISnackbarService snackbarService)
        {
            _snackbarService = snackbarService;
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                using var doc = JsonDocument.Parse(json);
                var baseUrl = doc.RootElement.GetProperty("ApiSettings").GetProperty("BaseUrl").GetString() ?? "";

                // URL 파싱 (http://localhost:18885/api/face/ 형태 가정)
                var uri = new Uri(baseUrl);
                IpAddress = uri.Host;
                Port = uri.Port.ToString();
            }
            catch { /* 파일이 없거나 형식이 다를 경우 기본값 유지 */ }
        }

        [RelayCommand]
        private void OnSave()
        {
            try
            {
                // 1. 새 URL 생성
                string newUrl = $"http://{IpAddress}:{Port}/api/face/";

                // 2. JSON 파일 업데이트
                var json = File.ReadAllText(_configPath);
                var jsonObj = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                var apiSettings = new Dictionary<string, string> { { "BaseUrl", newUrl } };
                jsonObj["ApiSettings"] = apiSettings;

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_configPath, JsonSerializer.Serialize(jsonObj, options));

                _snackbarService.Show("설정 저장 완료", "서버 주소가 성공적으로 변경되었습니다.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                _snackbarService.Show("저장 실패", ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            }
        }
    }
}
