using NSynology.Foto;

namespace OpenNas.Helpers;

public sealed class SelectablePhoto : BindableObject
{
    public SelectablePhoto(Photo photo) => Photo = photo;

    public Photo Photo { get; }

    public int Id => Photo.Id;

    bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CheckState));
        }
    }

    public SelectionCheckState CheckState =>
        _isSelected ? SelectionCheckState.Checked : SelectionCheckState.Unchecked;
}
