using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenNas.Data;
using OpenNas.Models;
using OpenNas.Services;

namespace OpenNas.ViewModels;

public class BackupTaskViewModel : INotifyPropertyChanged
{
    private readonly BackupDatabase _db;
    private readonly BackupEngine _engine;
    private string _progressText = "空闲";

    public BackupTaskViewModel(BackupDatabase db, BackupEngine engine)
    {
        _db = db;
        _engine = engine;
        Records = new ObservableCollection<BackupRecord>();
        _engine.ProgressChanged += (_, _) =>
            MainThread.BeginInvokeOnMainThread(UpdateFromEngine);
    }

    public ObservableCollection<BackupRecord> Records { get; }

    public string ProgressText
    {
        get => _progressText;
        private set { _progressText = value; OnPropertyChanged(); }
    }

    public async Task RefreshAsync()
    {
        var items = await _db.GetRecordsAsync(limit: 100);
        Records.Clear();
        foreach (var item in items)
            Records.Add(item);
        UpdateFromEngine();
    }

    private void UpdateFromEngine()
    {
        var p = _engine.Progress;
        ProgressText = p.IsRunning
            ? $"备份中 {p.Completed}/{p.Total}，失败 {p.Failed} — {p.CurrentFileName}"
            : $"空闲。已完成 {p.Completed}，失败 {p.Failed}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
