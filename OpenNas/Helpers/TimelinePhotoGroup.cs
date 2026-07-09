using NSynology.Foto;

namespace OpenNas.Helpers;

public class TimelinePhotoGroup : List<Photo>
{
    public TimelinePhotoGroup(string dateLabel, int year, int month, int day, int expectedCount)
    {
        DateLabel = dateLabel;
        Year = year;
        Month = month;
        Day = day;
        ExpectedCount = expectedCount;
    }

    public string DateLabel { get; }
    public int Year { get; }
    public int Month { get; }
    public int Day { get; }
    public int ExpectedCount { get; }
    public bool IsLoaded { get; set; }
}
