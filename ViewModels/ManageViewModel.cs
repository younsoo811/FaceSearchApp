using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceSearchApp.Models;
using FaceSearchApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FaceSearchApp.ViewModels
{
    public partial class ManageViewModel : ObservableObject
    {
        private readonly FaceApiService _api;

        // ── Confirm dialog callback (set by View) ─────────────────────
        public Func<int, bool>? ConfirmDelete { get; set; }

        // ── Filters ──────────────────────────────────────────────────
        [ObservableProperty] private bool _isNormalType = true;
        [ObservableProperty] private bool _isTargetType;
        [ObservableProperty] private DateTime? _startDate;
        [ObservableProperty] private DateTime? _endDate;
        [ObservableProperty] private int _pageSize = 20;

        // ── Paging ───────────────────────────────────────────────────
        [ObservableProperty] private int _currentPage = 1;
        [ObservableProperty] private int _totalPages = 1;
        [ObservableProperty] private long _totalCount;
        [ObservableProperty] private bool _hasPrevious;
        [ObservableProperty] private bool _hasNext;

        // ── State ────────────────────────────────────────────────────
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isDeleting;
        [ObservableProperty] private bool _hasItems;
        [ObservableProperty] private string _statusMessage = "조회 버튼을 눌러 데이터를 불러오세요.";
        [ObservableProperty] private string _pageInfo = string.Empty;
        [ObservableProperty] private int _selectedCount;

        public ObservableCollection<VectorItemViewModel> Items { get; } = [];

        public ManageViewModel(FaceApiService api) => _api = api;

        // ── Load ─────────────────────────────────────────────────────
        [RelayCommand]
        private async Task LoadAsync(CancellationToken ct)
        {
            IsLoading = true;
            Items.Clear();
            HasItems = false;
            StatusMessage = "📋 데이터 조회 중...";

            try
            {
                var imageType = IsTargetType ? ImageType.Target : ImageType.Normal;
                var response = await _api.GetVectorPageAsync(
                                    imageType, StartDate, EndDate, CurrentPage, PageSize, ct);

                if (response?.Success == true && response.Data is not null)
                {
                    var d = response.Data;
                    TotalCount = d.TotalCount;
                    TotalPages = d.TotalPages;
                    HasPrevious = d.HasPrevious;
                    HasNext = d.HasNext;
                    PageInfo = $"{CurrentPage} / {TotalPages} 페이지  (총 {TotalCount:N0}건)";

                    foreach (var v in d.Vectors)
                    {
                        var vm = new VectorItemViewModel(this)
                        {
                            Id = v.Id,
                            ImageId = v.ImageId,
                            CreatedAt = v.CreatedAt
                        };
                        Items.Add(vm);
                    }

                    HasItems = Items.Count > 0;
                    StatusMessage = HasItems
                        ? $"✅ {Items.Count}건 표시 중"
                        : "조회된 데이터가 없습니다.";
                }
                else
                {
                    TotalCount = 0;
                    TotalPages = 1;
                    HasPrevious = false;
                    HasNext = false;
                    PageInfo = string.Empty;
                    StatusMessage = response?.Msg ?? "데이터가 없습니다.";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "조회가 취소되었습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                UpdateSelected();
            }
        }

        // ── Delete selected ──────────────────────────────────────────
        [RelayCommand]
        private async Task DeleteSelectedAsync(CancellationToken ct)
        {
            var selected = Items.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0) return;

            // Confirmation via callback set by View
            if (ConfirmDelete is not null && !ConfirmDelete(selected.Count)) return;

            IsDeleting = true;
            StatusMessage = $"🗑️ {selected.Count}건 삭제 중...";

            try
            {
                var ids = selected.Select(x => x.Id).ToList();
                var imageType = IsTargetType ? ImageType.Target : ImageType.Normal;
                var response = await _api.DeleteAsync(imageType, ids, ct);

                if (response?.Success == true)
                {
                    var d = response.Data;
                    StatusMessage = $"✅ {d?.DeletedCount}건 삭제 완료";

                    // Reset to page 1 and reload
                    CurrentPage = 1;
                    await LoadAsync(ct);
                }
                else
                {
                    StatusMessage = $"❌ {response?.Msg ?? "삭제에 실패했습니다."}";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "삭제가 취소되었습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ {ex.Message}";
            }
            finally
            {
                IsDeleting = false;
            }
        }

        // ── Paging ───────────────────────────────────────────────────
        [RelayCommand]
        private async Task PreviousPageAsync(CancellationToken ct)
        {
            if (CurrentPage <= 1) return;
            CurrentPage--;
            await LoadAsync(ct);
        }

        [RelayCommand]
        private async Task NextPageAsync(CancellationToken ct)
        {
            if (CurrentPage >= TotalPages) return;
            CurrentPage++;
            await LoadAsync(ct);
        }

        [RelayCommand]
        private void ClearDates()
        {
            StartDate = null;
            EndDate = null;
        }

        // ── Select all / none ────────────────────────────────────────
        [RelayCommand]
        private void SelectAll()
        {
            foreach (var item in Items) item.IsSelected = true;
            UpdateSelected();
        }

        [RelayCommand]
        private void SelectNone()
        {
            foreach (var item in Items) item.IsSelected = false;
            UpdateSelected();
        }

        public void UpdateSelected()
            => SelectedCount = Items.Count(x => x.IsSelected);
    }

    // ── Row ViewModel ────────────────────────────────────────────────
    public partial class VectorItemViewModel : ObservableObject
    {
        private readonly ManageViewModel _parent;

        [ObservableProperty] private bool _isSelected;

        public string Id { get; set; } = string.Empty;
        public string ImageId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public string ShortId => Id.Length > 12 ? Id[..12] + "…" : Id;
        public string ShortImageId => ImageId.Length > 12 ? ImageId[..12] + "…" : ImageId;
        public string CreatedAtStr => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

        public VectorItemViewModel(ManageViewModel parent) => _parent = parent;

        partial void OnIsSelectedChanged(bool value) => _parent.UpdateSelected();
    }
}
