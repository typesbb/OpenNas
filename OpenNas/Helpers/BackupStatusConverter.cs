using System.Globalization;
using OpenNas.Core.Models;

namespace OpenNas.Helpers;

public class BackupStatusDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BackupItemStatus status ? status switch
        {
            BackupItemStatus.Pending => "等待中",
            BackupItemStatus.Uploading => "上传中",
            BackupItemStatus.Uploaded => "已完成",
            BackupItemStatus.Failed => "失败",
            BackupItemStatus.LocalDeleted => "已删本地",
            BackupItemStatus.DeleteFailed => "删除失败",
            _ => status.ToString()
        } : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BackupStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BackupItemStatus status)
            return Colors.Gray;

        return status switch
        {
            BackupItemStatus.Uploaded or BackupItemStatus.LocalDeleted =>
                Application.Current?.Resources.TryGetValue("Success", out var ok) == true ? ok : Colors.Green,
            BackupItemStatus.Failed or BackupItemStatus.DeleteFailed =>
                Application.Current?.Resources.TryGetValue("Error", out var err) == true ? err : Colors.Red,
            BackupItemStatus.Uploading =>
                Application.Current?.Resources.TryGetValue("BrandPrimary", out var primary) == true ? primary : Colors.Teal,
            _ =>
                Application.Current?.Resources.TryGetValue("TextSecondary", out var muted) == true ? muted : Colors.Gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
