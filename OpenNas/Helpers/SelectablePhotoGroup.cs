using System.Collections.ObjectModel;
using System.ComponentModel;

namespace OpenNas.Helpers;

public class SelectablePhotoGroup : ObservableCollection<SelectablePhoto>
{
    public SelectablePhotoGroup(string dateLabel, IEnumerable<SelectablePhoto>? photos = null)
    {
        DateLabel = dateLabel;
        if (photos != null)
        {
            foreach (var photo in photos)
                Add(photo);
        }

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
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CheckState)));
    }
}
