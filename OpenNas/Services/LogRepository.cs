using OpenNas.Core.Data;
using SQLite;

namespace OpenNas.Services;

public class LogRepository
{
    private static readonly Lazy<LogRepository> _instance = new(() => new LogRepository());
    public static LogRepository Instance => _instance.Value;

    private SQLiteAsyncConnection? _db;
    private string? _dbPath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    private const int MaxEntries = 200;
    private const int TrimThreshold = 300;

    private LogRepository()
    {
        // Delay path resolution to first use — avoids touching FileSystem at construction time.
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            _dbPath = Path.Combine(FileSystem.AppDataDirectory, "opennas_log.db");
            _db = new SQLiteAsyncConnection(_dbPath);
            await _db.CreateTableAsync<LogEntry>();
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<SQLiteAsyncConnection> GetDbAsync()
    {
        await EnsureInitializedAsync();
        return _db!;
    }

    public void AppendOperation(string message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var db = await GetDbAsync();
                var entry = new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Category = "操作",
                    Message = message
                };
                await db.InsertAsync(entry);
                await TrimIfNeededAsync(db);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogRepository] AppendOperation 失败: {ex}");
            }
        });
    }

    public void AppendError(string message, Exception? ex)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var db = await GetDbAsync();
                var entry = new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Category = "异常",
                    Message = message,
                    ExceptionType = ex?.GetType().FullName,
                    StackTrace = ex?.StackTrace
                };
                await db.InsertAsync(entry);
                await TrimIfNeededAsync(db);
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[LogRepository] AppendError 失败: {ex2}");
            }
        });
    }

    public async Task<List<LogEntry>> GetPageAsync(int skip, int take)
    {
        var db = await GetDbAsync();
        return await db.Table<LogEntry>()
            .OrderByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> GetCountAsync()
    {
        var db = await GetDbAsync();
        return await db.Table<LogEntry>().CountAsync();
    }    public async Task ClearAllAsync()
    {
        var db = await GetDbAsync();
        await db.DeleteAllAsync<LogEntry>();
    }

    private async Task TrimIfNeededAsync(SQLiteAsyncConnection db)
    {
        var count = await db.Table<LogEntry>().CountAsync();
        if (count <= TrimThreshold) return;
        var excess = count - MaxEntries;
        await db.ExecuteAsync(
            "DELETE FROM log_entries WHERE Id IN (SELECT Id FROM log_entries ORDER BY Id ASC LIMIT ?)",
            excess);
    }
}
