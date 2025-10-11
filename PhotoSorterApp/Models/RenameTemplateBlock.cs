using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoSorterApp.Models;

public partial class RenameTemplateBlock : ObservableObject
{
    public enum BlockType
    {
        Text,
        Date,
        Index,
        Name,
        Year,
        Month,
        Day
    }

    [ObservableProperty]
    private string _displayText = "";

    [ObservableProperty]
    private BlockType _type;

    public static RenameTemplateBlock CreateText(string text)
        => new() { DisplayText = text, Type = BlockType.Text };

    public static RenameTemplateBlock CreateDate()
        => new() { DisplayText = "{date}", Type = BlockType.Date };

    public static RenameTemplateBlock CreateIndex()
        => new() { DisplayText = "{index}", Type = BlockType.Index };

    public static RenameTemplateBlock CreateName()
        => new() { DisplayText = "{name}", Type = BlockType.Name };

    public static RenameTemplateBlock CreateYear()
        => new() { DisplayText = "{year}", Type = BlockType.Year };

    public static RenameTemplateBlock CreateMonth()
        => new() { DisplayText = "{month}", Type = BlockType.Month };

    public static RenameTemplateBlock CreateDay()
        => new() { DisplayText = "{day}", Type = BlockType.Day };
}