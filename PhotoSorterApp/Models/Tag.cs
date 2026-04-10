#nullable enable

using System.Collections.Generic;

namespace PhotoSorterApp.Models;

public enum TagKind
{
    Person = 0,
    Scene = 1,
    Object = 2,
    Event = 3,
    User = 4,
    System = 5
}

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TagKind Kind { get; set; } = TagKind.User;

    public ICollection<PhotoTag> Photos { get; set; } = new List<PhotoTag>();
}
