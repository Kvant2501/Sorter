namespace PhotoSorterApp.Models;

public class SortingOptions
{
    public string SourceFolder { get; set; } = "";
    public bool IsRecursive { get; set; } = true;
    public bool SplitByMonth { get; set; } = true;
    public bool CreateBackup { get; set; } = false;
}