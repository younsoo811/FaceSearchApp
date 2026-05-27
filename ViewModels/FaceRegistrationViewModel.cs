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

        private bool _isDialogOpen = false;
        private int _noDetectionFrameCount = 0;
        private const int NoDetectionGraceFrames = 8; // 약 8프레임 유예 (≈ 0.25초)

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

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var capture = _capture; // 로컬 복사로 중간에 null 되어도 안전
                    if (capture == null || !capture.Read(frame) || frame.Empty())
                        break;

                    ProcessDetection(frame);

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        // 취소 후 Dispatcher 콜백이 늦게 실행될 경우 방어
                        if (!token.IsCancellationRequested)
                            VideoFrame = frame.ToWriteableBitmap();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 취소 — 무시
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() =>
                    StatusMessage = $"❌ 스트림 오류: {ex.Message}");
            }
        }

        private void ProcessDetection(Mat frame)
        {
            if (_isDialogOpen || _cts == null || _cts.IsCancellationRequested) return;

            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

            var faces = _faceCascade.DetectMultiScale(
                gray,
                1.3,
                5,
                HaarDetectionTypes.ScaleImage,
                new OpenCvSharp.Size(80, 80)  // 최소 크기를 살짝 낮춰 더 잘 잡히게
            );

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
                // Contains 대신 겹침 비율로 판단 (60% 이상이면 통과)
                if (GetOverlapRatio(face, guideRect) >= 0.6)
                {
                    foundInZone = true;
                    _noDetectionFrameCount = 0; // 유예 카운터 리셋

                    if (_detectionStartTime == null)
                        _detectionStartTime = DateTime.Now;

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
                _noDetectionFrameCount++;

                // 유예 프레임 초과 시에만 진행상황 리셋
                if (_noDetectionFrameCount >= NoDetectionGraceFrames)
                {
                    _detectionStartTime = null;
                    CaptureProgress = 0;
                    GuideColor = Brushes.White;
                }
            }
        }

        // 두 Rect의 겹침 비율 계산 (face 기준)
        private static double GetOverlapRatio(Rect face, Rect zone)
        {
            int interX = Math.Max(face.X, zone.X);
            int interY = Math.Max(face.Y, zone.Y);
            int interW = Math.Min(face.X + face.Width, zone.X + zone.Width) - interX;
            int interH = Math.Min(face.Y + face.Height, zone.Y + zone.Height) - interY;

            if (interW <= 0 || interH <= 0) return 0.0;

            double intersectArea = interW * interH;
            double faceArea = face.Width * face.Height;
            return intersectArea / faceArea;
        }

        private async void CaptureAndConfirm(Mat frame, Rect faceRect)
        {
            _isDialogOpen = true; // 다이얼로그 열기 전 차단

            // 얼굴 사각형 주변에 여백 추가 (20% 패딩)
            int padX = (int)(faceRect.Width * 0.2);
            int padY = (int)(faceRect.Height * 0.2);

            var paddedRect = new Rect(
                Math.Max(0, faceRect.X - padX),
                Math.Max(0, faceRect.Y - padY),
                Math.Min(frame.Width - Math.Max(0, faceRect.X - padX), faceRect.Width + padX * 2),
                Math.Min(frame.Height - Math.Max(0, faceRect.Y - padY), faceRect.Height + padY * 2)
            );

            using Mat cropped = new Mat(frame, paddedRect);
            var faceBitmap = cropped.ToWriteableBitmap();
            faceBitmap.Freeze();

            await App.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
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
                        SaveFileDialog saveFileDialog = new SaveFileDialog
                        {
                            Title = "안면 이미지 저장",
                            Filter = "JPEG Image (*.jpg)|*.jpg|All Files (*.*)|*.*",
                            FileName = $"Face_{DateTime.Now:yyyyMMdd_HHmmss}.jpg",
                            DefaultExt = "jpg"
                        };

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
                }
                finally
                {
                    // 저장/다시시도 어느 버튼을 눌러도 반드시 해제
                    _isDialogOpen = false;
                    _noDetectionFrameCount = 0;
                    GuideColor = Brushes.White;
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

        [RelayCommand]
        private void StopCamera()
        {
            _cts?.Cancel();
            _cts = null;

            // 루프가 완전히 빠져나올 시간을 잠깐 줌
            Thread.Sleep(100);

            var capture = _capture;
            _capture = null;
            capture?.Release();
            capture?.Dispose();

            App.Current.Dispatcher.Invoke(() =>
            {
                VideoFrame = null;
                StatusMessage = "카메라가 중지되었습니다.";
                CaptureProgress = 0;
                GuideColor = Brushes.White;
                _isDialogOpen = false;
                _detectionStartTime = null;
            });
        }
    }
}
