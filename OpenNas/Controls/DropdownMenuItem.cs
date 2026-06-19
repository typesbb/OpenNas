namespace OpenNas.Controls;

public sealed class DropdownMenuItem(string key, string text, bool isSelected = false, string? trailingIcon = null)
{
    public string Key { get; } = key;
    public string Text { get; } = text;
    public bool IsSelected { get; } = isSelected;
    public string? TrailingIcon { get; } = trailingIcon;
}
