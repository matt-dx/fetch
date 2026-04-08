namespace Fetcher.Core.Utilities;

public static class ByteFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes == 0)
            return "0 B";

        var magnitude = (int)Math.Floor(Math.Log(Math.Abs(bytes), 1024));
        magnitude = Math.Min(magnitude, Units.Length - 1);

        var adjusted = bytes / Math.Pow(1024, magnitude);
        return $"{adjusted:0.##} {Units[magnitude]}";
    }
}
