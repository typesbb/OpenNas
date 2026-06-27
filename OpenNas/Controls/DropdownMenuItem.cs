namespace OpenNas.Controls;

public sealed class DropdownMenuItem
{
    public string Key { get; }
    public string Text { get; }
    public bool IsSelected { get; }
    public bool IsEnabled { get; }
    public string? TrailingIcon { get; }

    public DropdownMenuItem(string key, string text, bool isSelected = false, string? trailingIcon = null, bool isEnabled = true)
    {
        Key = key;
        Text = text;
        IsSelected = isSelected;
        TrailingIcon = trailingIcon;
        IsEnabled = isEnabled;
    }
}