using OpenNas.Models;

namespace OpenNas.Services;

public sealed class BackupQueueTracker
{
    public const int SlotCount = 3;
    private const double MinProgressDelta = 0.01;

    private readonly SlotState[] _slots = new SlotState[SlotCount];
    private readonly object _lock = new();

    public BackupQueueTracker()
    {
        for (var i = 0; i < SlotCount; i++)
            _slots[i] = new SlotState();
    }

    public void Reset()
    {
        lock (_lock)
        {
            foreach (var slot in _slots)
                slot.Clear();
        }
    }

    public void AssignSlot(int slotIndex, BackupQueueItem item)
    {
        lock (_lock)
        {
            var slot = _slots[slotIndex];
            slot.Active = true;
            slot.Key = item.Key;
            slot.FileName = item.FileName;
            slot.ContentUri = item.ContentUri;
            slot.RuleId = item.RuleId;
            slot.Stage = BackupQueueStage.Loading;
            slot.Progress = 0;
        }
    }

    public bool UpdateSlotProgress(int slotIndex, double progress)
    {
        lock (_lock)
        {
            var slot = _slots[slotIndex];
            if (!slot.Active || slot.Stage != BackupQueueStage.Loading)
                return false;

            var clamped = Math.Clamp(progress, 0, 1);
            if (!ShouldUpdate(slot.Progress, clamped))
                return false;

            slot.Progress = clamped;
            return true;
        }
    }

    public bool SetSlotReady(int slotIndex)
    {
        lock (_lock)
        {
            var slot = _slots[slotIndex];
            if (!slot.Active)
                return false;

            slot.Stage = BackupQueueStage.Ready;
            slot.Progress = 0;
            return true;
        }
    }

    public bool SetSlotUploading(int slotIndex)
    {
        lock (_lock)
        {
            var slot = _slots[slotIndex];
            if (!slot.Active)
                return false;

            slot.Stage = BackupQueueStage.Uploading;
            slot.Progress = 0;
            return true;
        }
    }

    public bool UpdateSlotUploadProgress(int slotIndex, double progress)
    {
        lock (_lock)
        {
            var slot = _slots[slotIndex];
            if (!slot.Active || slot.Stage != BackupQueueStage.Uploading)
                return false;

            var clamped = Math.Clamp(progress, 0, 1);
            if (!ShouldUpdate(slot.Progress, clamped))
                return false;

            slot.Progress = clamped;
            return true;
        }
    }

    public void ClearSlot(int slotIndex)
    {
        lock (_lock)
        {
            _slots[slotIndex].Clear();
        }
    }

    public IReadOnlyList<BackupQueueItem> GetVisibleSnapshot(int? ruleId = null)
    {
        lock (_lock)
        {
            return _slots
                .Select((s, index) => (Slot: s, Index: index))
                .Where(x => x.Slot.Active)
                .Where(x => !ruleId.HasValue || x.Slot.RuleId == ruleId.Value)
                .Select(x => x.Slot.ToItem(x.Index))
                .OrderBy(i => i.StatusOrder)
                .ThenByDescending(i => i.Progress)
                .ThenBy(i => i.SlotIndex)
                .ToList();
        }
    }

    private static bool ShouldUpdate(double current, double next)
    {
        if (next <= 0 || next >= 1)
            return true;
        return Math.Abs(current - next) >= MinProgressDelta;
    }

    private sealed class SlotState
    {
        public bool Active { get; set; }
        public string Key { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ContentUri { get; set; } = "";
        public int RuleId { get; set; }
        public BackupQueueStage Stage { get; set; }
        public double Progress { get; set; }

        public void Clear()
        {
            Active = false;
            Key = "";
            FileName = "";
            ContentUri = "";
            RuleId = 0;
            Stage = BackupQueueStage.Loading;
            Progress = 0;
        }

        public BackupQueueItem ToItem(int slotIndex) => new()
        {
            SlotIndex = slotIndex,
            Key = Key,
            FileName = FileName,
            ContentUri = ContentUri,
            RuleId = RuleId,
            Stage = Stage,
            Progress = Progress
        };
    }
}
