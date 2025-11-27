using System.Collections.Generic;

namespace PhotoSorterApp.Models;

public class DuplicateGroup
{
    public List<string> Files { get; }

    public DuplicateGroup(List<string> files)
    {
        Files = files;
    }
}