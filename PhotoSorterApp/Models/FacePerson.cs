#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace PhotoSorterApp.Models;

public class FacePerson
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastConfirmedUtc { get; set; }

    public ICollection<DetectedFace> ConfirmedFaces { get; set; } = new List<DetectedFace>();

    // Not mapped — populated on demand
    [NotMapped]
    public int FaceCount { get; set; }
}
