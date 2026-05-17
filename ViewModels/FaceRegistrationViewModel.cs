using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using Wpf.Ui;
using System.Windows.Media;
using Microsoft.Win32;
using System.IO;

namespace FaceSearchApp.ViewModels
{
    public partial class FaceRegistrationViewModel : ObservableObject
    {
        private readonly IContentDialogService _dialogService;
        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private readonly CascadeClassifier _faceCascade;

        [ObservableProperty] private BitmapSource? _videoFrame;
        [ObservableProperty] private string _rtspUrl = string.Empty;
        [ObservableProperty] private double _captureProgress;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusMessage = "카메라를 연결해 주세요.";
        [ObservableProperty] private Brush _guideColor = Brushes.White;

        private DateTime? _detectionStartTime;
        private const double RequiredSeconds = 2.0;

        public FaceRegistrationViewModel(IContentDialogService dialogService)
        {
            _dialogService = dialogService;
            // 파일이 실행 경로에 있어야 함
            _faceCascade = new CascadeClassifier("haarcascade_frontalface_default.xml");
        }

        [RelayCommand]
        private async Task StartCameraAsync(CancellationToken ct)
        {
            StopCamera();
            IsLoading = true;
            StatusMessage = "카메라 소스에 연결하는 중...";

            try
            {
                await Task.Run(() =>
                {
                    _capture = string.IsNullOrWhiteSpace(RtspUrl) ? new VideoCapture(0) : new VideoCapture(RtspUrl);
                }, ct);

                if (_capture == null || !_capture.IsOpened())
                {
                    StatusMessage = "❌ 카메라 연결에 실패했습니다.";
                    IsLoading = false;
                    return;
                }

                StatusMessage = "✅ 연결됨: 안면 가이드 영역에 얼굴을 맞춰주세요.";
                IsLoading = false;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                _ = Task.Run(() => RunVideoLoop(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ 오류: {ex.Message}";
                IsLoading = false;
            }
        }

        private void RunVideoLoop(CancellationToken token)
        {
            using Mat frame = new();
            while (!token.IsCancellationRequested && _capture != null && _capture.Read(frame))
            {
                if (frame.Empty()) continue;

                ProcessDetection(frame);

                App.Current.Dispatcher.Invoke(() => {
                    VideoFrame = frame.ToWriteableBitmap();
                });
            }
        }

        private void ProcessDetection(Mat frame)
        {
            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

            // OpenCvSharp4 Size 문법 수정
            // HaarDetectionTypes.ScaleImage를 네 번째 인수로 추가하고, 그 뒤에 minSize를 입력합니다.
            var faces = _faceCascade.DetectMultiScale(
                gray,
                1.3,
                5,
                HaarDetectionTypes.ScaleImage, // 추가
                new OpenCvSharp.Size(100, 100)
            );

            //var guideRect = new Rect(frame.Width / 2 - 120, frame.Height / 2 - 150, 240, 300);
            // 영상의 실제 크기를 기준으로 중앙 가이드 영역 설정 (영상 크기의 약 30~40%)
            int guideWidth = (int)(frame.Width * 0.4);
            int guideHeight = (int)(frame.Height * 0.5);
            var guideRect = new Rect(
                (frame.Width - guideWidth) / 2,
                (frame.Height - guideHeight) / 2,
                guideWidth,
                guideHeight
            );
            bool foundInZone = false;

            foreach (var face in faces)
            {
                //var faceCenter = new Point(face.X + face.Width / 2, face.Y + face.Height / 2);
                if (guideRect.Contains(face))
                {
                    foundInZone = true;
                    if (_detectionStartTime == null) _detectionStartTime = DateTime.Now;

                    var elapsed = (DateTime.Now - _detectionStartTime.Value).TotalSeconds;
                    CaptureProgress = Math.Min(100, (elapsed / RequiredSeconds) * 100);
                    GuideColor = Brushes.LimeGreen;

                    if (elapsed >= RequiredSeconds)
                    {
                        _detectionStartTime = null;
                        CaptureProgress = 0;
                        CaptureAndConfirm(frame, face);
                    }
                    break;
                }
            }

            if (!foundInZone)
            {
                _detectionStartTime = null;
                CaptureProgress = 0;
                GuideColor = Brushes.White;
            }
        }

        private async void CaptureAndConfirm(Mat frame, Rect faceRect)
        {
            // 1. 얼굴 영역 크롭 및 비트맵 변환
            using Mat cropped = new Mat(frame, faceRect);
            var faceBitmap = cropped.ToWriteableBitmap();

            // 비트맵을 변경 불가능하게 얼려서(Freeze) 스레드 간 안전하게 전달합니다.
            faceBitmap.Freeze();

            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                var dialog = new ContentDialog(_dialogService.GetContentPresenter())
                {
                    Title = "안면 등록 확인",
                    Content = new System.Windows.Controls.Image
                    {
                        Source = faceBitmap,
                        Width = 200,
                        Margin = new System.Windows.Thickness(10)
                    },
                    PrimaryButtonText = "저장",
                    CloseButtonText = "다시 시도",
                    PrimaryButtonAppearance = ControlAppearance.Primary
                };

                var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);

                if (result == ContentDialogResult.Primary)
                {
                    // 2. 파일 저장 다이얼로그 설정
                    SaveFileDialog saveFileDialog = new SaveFileDialog
                    {
                        Title = "안면 이미지 저장",
                        Filter = "JPEG Image (*.jpg)|*.jpg|All Files (*.*)|*.*",
                        // 기본 파일명을 타임스탬프 형식으로 지정 (예: Face_20260514_0100.jpg)
                        FileName = $"Face_{DateTime.Now:yyyyMMdd_HHmmss}.jpg",
                        DefaultExt = "jpg"
                    };

                    // 3. 사용자가 경로를 지정하고 '확인'을 누른 경우
                    if (saveFileDialog.ShowDialog() == true)
                    {
                        try
                        {
                            SaveBitmapAsJpg(faceBitmap, saveFileDialog.FileName);
                            StatusMessage = $"✅ 저장 완료: {Path.GetFileName(saveFileDialog.FileName)}";
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = $"❌ 저장 실패: {ex.Message}";
                        }
                    }
                }
            });
        }

        // 4. 비트맵 이미지를 JPG 파일로 저장하는 헬퍼 메서드
        private void SaveBitmapAsJpg(BitmapSource bitmap, string filePath)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                JpegBitmapEncoder encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 90 // 이미지 품질 설정 (1-100)
                };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fileStream);
            }
        }
        //private async void CaptureAndConfirm(Mat frame, Rect faceRect)
        //{
        //    using Mat cropped = new Mat(frame, faceRect);
        //    var faceBitmap = cropped.ToWriteableBitmap();

        //    await App.Current.Dispatcher.InvokeAsync(async () =>
        //    {
        //        var dialog = new ContentDialog(_dialogService.GetContentPresenter())
        //        {
        //            Title = "안면 등록 확인",
        //            Content = new System.Windows.Controls.Image { Source = faceBitmap, Width = 200 },
        //            PrimaryButtonText = "저장",
        //            CloseButtonText = "다시 시도",
        //            // 다이얼로그 전체가 아닌 버튼의 외형을 지정해야 합니다.
        //            PrimaryButtonAppearance = ControlAppearance.Primary
        //        };

        //        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        //        if (result == ContentDialogResult.Primary)
        //        {
        //            StatusMessage = "✅ 안면 이미지가 성공적으로 캡처되었습니다.";
        //            // TODO: 저장 로직 구현
        //        }
        //    });
        //}

        [RelayCommand]
        private void StopCamera()
        {
            _cts?.Cancel();
            _capture?.Release();
            _capture = null;
            VideoFrame = null;
            StatusMessage = "카메라가 중지되었습니다.";
            CaptureProgress = 0;
        }
    }
}
