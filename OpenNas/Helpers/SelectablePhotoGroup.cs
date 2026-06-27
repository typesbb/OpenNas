using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenNas.Helpers;

public class SelectablePhotoGroup : List<SelectablePhoto>, INotifyPropertyChanged
{
    public SelectablePhotoGroup(string dateLabel, IEnumerable<SelectablePhoto> photos)
        : base(photos)
    {
        DateLabel = dateLabel;
        RefreshCheckState();
    }

    public string DateLabel { get; }

    public SelectionCheckState CheckState { get; private set; }

    public void RefreshCheckState()
    {
        var total = Count;
        if (total == 0)
        {
            SetCheckState(SelectionCheckState.Unchecked);
            return;
        }

        var selected = this.Count(p => p.IsSelected);
        SetCheckState(
            selected == 0 ? SelectionCheckState.Unchecked :
            selected == total ? SelectionCheckState.Checked :
            SelectionCheckState.Partial);
    }

    private void SetCheckState(SelectionCheckState state)
    {
        if (CheckState == state)
            return;
        CheckState = state;
        OnPropertyChanged(nameof(CheckState));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
