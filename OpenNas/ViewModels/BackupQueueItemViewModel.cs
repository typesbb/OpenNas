using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenNas.Core.Models;

namespace OpenNas.ViewModels;

public class BackupQueueItemViewModel : INotifyPropertyChanged
{
    private string _key = "";
    private string _fileName = "";
    private string _contentUri = "";
    private string _statusText = "";
    private double _rowProgress;
    private bool _showRowProgress = true;
    private bool _showStatusText;
    private bool _isActive;

    public string Key
    {
        get => _key;
        private set
        {
            if (_key == value) return;
            _key = value;
            OnPropertyChanged();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
        }
    }

    public string FileName
    {
        get => _fileName;
        private set
        {
            if (_fileName == value) return;
            _fileName = value;
            OnPropertyChanged();
        }
    }

    public string ContentUri
    {
        get => _contentUri;
        private set
        {
            if (_contentUri == value) return;
            _contentUri = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public double RowProgress
    {
        get => _rowProgress;
        private set
        {
            if (Math.Abs(_rowProgress - value) < 0.005) return;
            _rowProgress = value;
            OnPropertyChanged();
        }
    }

    public bool ShowRowProgress
    {
        get => _showRowProgress;
        private set
        {
            if (_showRowProgress == value) return;
            _showRowProgress = value;
            OnPropertyChanged();
        }
    }

    public bool ShowStatusText
    {
        get => _showStatusText;
        private set
        {
            if (_showStatusText == value) return;
            _showStatusText = value;
            OnPropertyChanged();
        }
    }

    public void UpdateFrom(BackupQueueItem item)
    {
        Key = item.Key;
        FileName = item.FileName;
        if (_contentUri != item.ContentUri)
            ContentUri = item.ContentUri;
        StatusText = item.StatusText;
        ShowStatusText = item.ShowStatusText;
        RowProgress = item.Progress;
        ShowRowProgress = item.ShowRowProgress;
        IsActive = true;
    }

    public void Clear()
    {
        Key = "";
        FileName = "";
        ContentUri = "";
        StatusText = "";
        RowProgress = 0;
        ShowRowProgress = true;
        ShowStatusText = false;
        IsActive = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
