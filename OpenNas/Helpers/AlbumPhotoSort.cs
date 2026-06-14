namespace OpenNas.Helpers;

public static class AlbumPhotoSort
{
    public static bool UsesGroups(string field) => field is "time" or "size";

    public static bool UsesSizeGroups(string field) => field is "size";
}
