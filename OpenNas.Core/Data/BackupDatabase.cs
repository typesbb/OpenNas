using OpenNas.Core.Models;
using SQLite;

namespace OpenNas.Core.Data;

public class BackupDatabase
{
    private SQLiteAsyncConnection? _db;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _tablesReady;

    public BackupDatabase(string dbPath)
    {
        _dbPath = dbPath;
    }

    [Obsolete("Use BackupDatabase(string dbPath) instead.")]
    public BackupDatabase()
    { _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opennas_backup.db");
    }

    /// <summary>应用启动时调用，避免多页面并发访问时 CreateTable 尚未完成。</summary>
    public Task EnsureInitializedAsync() => GetDbAsync();

    private async Task<SQLiteAsyncConnection> GetDbAsync()
    {
        if (_tablesReady && _db != null)
            return _db;

        await _initLock.WaitAsync();
        try
        {
            _db ??= new SQLiteAsyncConnection(_dbPath);
            if (!_tablesReady)
            {
                await _db.CreateTableAsync<BackupRecord>();
                await _db.CreateTableAsync<BackupRuleRecord>();
                await _db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_backup_items_rule_id ON backup_items(RuleId)");
                _tablesReady = true;
            }

            return _db;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<List<BackupRule>> GetRulesAsync()
    {
        var db = await GetDbAsync();
        var rows = await db.Table<BackupRuleRecord>().ToListAsync();
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task SaveRuleAsync(BackupRule rule)
    {
        var db = await GetDbAsync();
        var row = BackupRuleRecord.FromModel(rule);
        if (rule.Id == 0)
        {
            var id = await db.InsertAsync(row);
            rule.Id = id;
        }
        else
            await db.UpdateAsync(row);
    }

    public async Task DeleteRuleAsync(int id)
    {
        var db = await GetDbAsync();
        await db.DeleteAsync<BackupRuleRecord>(id);
    }

    public async Task<BackupRecord?> FindByLocalMediaAsync(string localMediaId, long size, long dateModified)
    {
        var db = await GetDbAsync();
        return await db.Table<BackupRecord>()
            .Where(r => r.LocalMediaId == localMediaId && r.Size == size && r.DateModified == dateModified
                && (r.Status == BackupItemStatus.Uploaded || r.Status == BackupItemStatus.LocalDeleted))
            .FirstOrDefaultAsync();
    }

    /// <summary>已备份成功的本地媒体键（避免扫描时逐条查库）。</summary>
    public async Task<HashSet<string>> GetCompletedMediaKeysAsync()
    {
        var db = await GetDbAsync();
        var rows = await db.Table<BackupRecord>()
            .Where(r => r.Status == BackupItemStatus.Uploaded || r.Status == BackupItemStatus.LocalDeleted)
            .ToListAsync();
        return rows.Select(BackupMediaKey).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<HashSet<string>> GetCompletedMediaKeysForRuleAsync(int ruleId)
    {
        var db = await GetDbAsync();
        var rows = await db.Table<BackupRecord>()
            .Where(r => r.RuleId == ruleId
                && (r.Status == BackupItemStatus.Uploaded || r.Status == BackupItemStatus.LocalDeleted))
            .ToListAsync();
        return rows.Select(BackupMediaKey).ToHashSet(StringComparer.Ordinal);
    }

    public static string BackupMediaKey(BackupRecord r) =>
        $"{r.LocalMediaId}|{r.Size}|{r.DateModified}";

    public static string BackupMediaKey(string localMediaId, long size, long dateModified) =>
        $"{localMediaId}|{size}|{dateModified}";

    public async Task<BackupRecord?> FindOpenRecordAsync(string localMediaId, long size, long dateModified)
    {
        var db = await GetDbAsync();
        return await db.Table<BackupRecord>()
            .Where(r => r.LocalMediaId == localMediaId
                && r.Size == size
                && r.DateModified == dateModified
                && r.Status != BackupItemStatus.Uploaded
                && r.Status != BackupItemStatus.LocalDeleted)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();
    }

    public async Task UpsertRecordAsync(BackupRecord record)
    {
        var db = await GetDbAsync();
        if (record.Id == 0)
            record.Id = await db.InsertAsync(record);
        else
            await db.UpdateAsync(record);
    }

    public async Task<List<BackupRecord>> GetRecordsAsync(BackupItemStatus? status = null, int limit = 200)
    {
        var db = await GetDbAsync();
        var query = db.Table<BackupRecord>().OrderByDescending(r => r.Id);
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);
        return await query.Take(limit).ToListAsync();
    }

    public async Task<int> CountByStatusAsync(params BackupItemStatus[] statuses)
    {
        var db = await GetDbAsync();
        return await db.Table<BackupRecord>().CountAsync(r => statuses.Contains(r.Status));
    }

    public async Task<List<BackupRecord>> GetFailedRecordsAsync(int limit = 500)
    {
        var db = await GetDbAsync();
        return await db.Table<BackupRecord>()
            .Where(r => r.Status == BackupItemStatus.Failed || r.Status == BackupItemStatus.DeleteFailed)
            .OrderByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<BackupRecord>> GetRecentFailuresAsync(int limit = 3)
    {
        var db = await GetDbAsync();
        return await db.Table<BackupRecord>()
            .Where(r => r.Status == BackupItemStatus.Failed || r.Status == BackupItemStatus.DeleteFailed)
            .OrderByDescending(r => r.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task ResetFailedForRetryAsync(int? ruleId = null)
    {
        var db = await GetDbAsync();
        var failed = await GetFailedRecordsAsync();
        foreach (var r in failed)
        {
            if (ruleId is int id && r.RuleId != id) continue;
            r.Status = BackupItemStatus.Pending;
            r.LastError = null;
            await db.UpdateAsync(r);
        }
    }

    /// <summary>删除已从手机本地删除的文件记录（Status = LocalDeleted），这些文件不会再被扫描到。</summary>
    public async Task DeleteLocalDeletedForRuleAsync(int ruleId)
    {
        var db = await GetDbAsync();
        await db.ExecuteAsync(
            "DELETE FROM backup_items WHERE RuleId = ? AND Status = ?",
            ruleId, (int)BackupItemStatus.LocalDeleted);
    }

    public async Task<int> CountFailedByRuleAsync(int ruleId)
    {
        var db = await GetDbAsync();
        return await db.Table<BackupRecord>().CountAsync(r =>
            r.RuleId == ruleId &&
            (r.Status == BackupItemStatus.Failed || r.Status == BackupItemStatus.DeleteFailed));
    }

    public async Task<BackupRecord?> GetRecordByIdAsync(int id)
    {
        var db = await GetDbAsync();
        return await db.Table<BackupRecord>().Where(r => r.Id == id).FirstOrDefaultAsync();
    }
}

[Table("backup_rules")]
public class BackupRuleRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string LocalAlbumId { get; set; } = "";
    public string LocalAlbumName { get; set; } = "";
    public int RemoteAlbumId { get; set; }
    public string RemoteAlbumName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool DeleteAfterBackup { get; set; }

    public BackupRule ToModel() => new()
    {
        Id = Id,
        LocalAlbumId = LocalAlbumId,
        LocalAlbumName = LocalAlbumName,
        RemoteAlbumId = RemoteAlbumId,
        RemoteAlbumName = RemoteAlbumName,
        Enabled = Enabled,
        DeleteAfterBackup = DeleteAfterBackup
    };

    public static BackupRuleRecord FromModel(BackupRule r) => new()
    {
        Id = r.Id,
        LocalAlbumId = r.LocalAlbumId,
        LocalAlbumName = r.LocalAlbumName,
        RemoteAlbumId = r.RemoteAlbumId,
        RemoteAlbumName = r.RemoteAlbumName,
        Enabled = r.Enabled,
        DeleteAfterBackup = r.DeleteAfterBackup
    };
}
