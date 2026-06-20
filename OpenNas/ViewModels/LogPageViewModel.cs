using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using OpenNas.Data;
using OpenNas.Services;

namespace OpenNas.ViewModels;

public class LogPageViewModel : INotifyPropertyChanged
{
    private const int PageSize = 30;

    private readonly LogRepository _repo;
    private int _loadedCount;
    private bool _isLoading;
    private bool _hasMore = true;

    public LogPageViewModel(LogRepository repo)
    {
        _repo = repo;
        Entries = [];
        LoadMoreCommand = new Command(async () => await LoadMoreAsync());
        RefreshCommand = new Command(async () => await RefreshAsync());
    }

    public ObservableCollection<LogEntry> Entries { get; }

    public bool IsLoading
    {
        get => _isLoading;
        set { if (_isLoading == value) return; _isLoading = value; OnPropertyChanged(); }
    }

    public bool HasMore
    {
        get => _hasMore;
        set { if (_hasMore == value) return; _hasMore = value; OnPropertyChanged(); }
    }

    public ICommand LoadMoreCommand { get; }
    public ICommand RefreshCommand { get; }

    public async Task LoadInitialAsync()
    {
        await _repo.EnsureInitializedAsync();
        Entries.Clear();
        _loadedCount = 0;
        HasMore = true;
        await LoadMoreAsync();
    }

    private async Task LoadMoreAsync()
    {
        if (IsLoading || !HasMore) return;
        IsLoading = true;
        try
        {
            var page = await _repo.GetPageAsync(_loadedCount, PageSize);
            foreach (var entry in page)
                Entries.Add(entry);
            _loadedCount += page.Count;
            HasMore = page.Count == PageSize;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"日志加载失败: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshAsync()
    {
        await LoadInitialAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
