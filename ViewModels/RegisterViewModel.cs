using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceSearchApp.Models;
using FaceSearchApp.Services;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace FaceSearchApp.ViewModels
{
    public partial class RegisterViewModel : ObservableObject
    {
        private readonly FaceApiService _api;

        [ObservableProperty] private BitmapImage? _previewImage;
        [ObservableProperty] private string _base64 = string.Empty;
        [ObservableProperty] private string _imageInfo = string.Empty;
        [ObservableProperty] private bool _isNormalType = true;
        [ObservableProperty] private bool _isTargetType;
        [ObservableProperty] private bool _isUploading;
        [ObservableProperty] private bool _isSuccess;
        [ObservableProperty] private string _statusMessage = "이미지를 드래그하거나 클릭해서 선택하세요.";
        [ObservableProperty] private string _lastResult = string.Empty;

        public bool HasImage => !string.IsNullOrEmpty(Base64);

        public RegisterViewModel(FaceApiService api) => _api = api;

        public bool TryLoadImage(string filePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);
                var (valid, format, error) = ValidateImageBytes(bytes);
                if (!valid)
                {
                    StatusMessage = $"❌ {error}";
                    IsSuccess = false;
                    return false;
                }

                var bmp = new BitmapImage();
                using var ms = new MemoryStream(bytes);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                PreviewImage = bmp;
                Base64 = Convert.ToBase64String(bytes);
                ImageInfo = $"{format}  ·  {bmp.PixelWidth}×{bmp.PixelHeight}  ·  {bytes.Length / 1024} KB";
                StatusMessage = $"✅ {Path.GetFileName(filePath)} 로드 완료";
                IsSuccess = false;
                LastResult = string.Empty;
                OnPropertyChanged(nameof(HasImage));
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ {ex.Message}";
                return false;
            }
        }

        [RelayCommand]
        private async Task UploadAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(Base64))
            {
                StatusMessage = "⚠️ 먼저 이미지를 선택해주세요.";
                return;
            }

            IsUploading = true;
            IsSuccess = false;
            StatusMessage = "📤 업로드 중...";

            try
            {
                var imageType = IsTargetType ? ImageType.Target : ImageType.Normal;
                var response = await _api.UploadAsync(Base64, imageType, ct);

                if (response?.Success == true && response.Data is not null)
                {
                    var d = response.Data;
                    LastResult = $"ID: {d.ImageId}";
                    StatusMessage = "✅ 등록이 완료되었습니다!";
                    IsSuccess = true;
                }
                else
                {
                    StatusMessage = $"❌ {response?.Msg ?? "등록에 실패했습니다."}";
                    IsSuccess = false;
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "업로드가 취소되었습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ 오류: {ex.Message}";
            }
            finally
            {
                IsUploading = false;
            }
        }

        [RelayCommand]
        private void Clear()
        {
            PreviewImage = null;
            Base64 = string.Empty;
            ImageInfo = string.Empty;
            IsSuccess = false;
            LastResult = string.Empty;
            StatusMessage = "이미지를 드래그하거나 클릭해서 선택하세요.";
            OnPropertyChanged(nameof(HasImage));
        }

        private static (bool, string?, string?) ValidateImageBytes(byte[] bytes)
        {
            if (bytes.Length < 8) return (false, null, "파일이 너무 작습니다.");
            if (bytes[0] == 0xFF && bytes[1] == 0xD8) return (true, "JPEG", null);
            if (bytes[0] == 0x89 && bytes[1] == 0x50) return (true, "PNG", null);
            if (bytes[0] == 0x42 && bytes[1] == 0x4D) return (true, "BMP", null);
            if (bytes[0] == 0x52 && bytes[1] == 0x49) return (true, "WEBP", null);
            if (bytes[0] == 0x47 && bytes[1] == 0x49) return (true, "GIF", null);
            return (false, null, "지원하지 않는 형식입니다. (JPEG, PNG, BMP, WEBP, GIF)");
        }
    }
}
