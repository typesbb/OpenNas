using SQLite;

namespace OpenNas.Core.Data;

[Table("log_entries")]
public class LogEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>"操作" 或 "异常"</summary>
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";

    /// <summary>异常类型名，仅 Category="异常" 时可能有值</summary>
    public string? ExceptionType { get; set; }

    /// <summary>完整堆栈，仅 Category="异常" 时可能有值</summary>
    public string? StackTrace { get; set; }

    public string TimeText => Timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss");

    public bool IsError => Category == "异常";
    public bool IsWarning => Category == "警告";
}