using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Smotrel.Data.Entities
{
    public class CourseEntity
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required] public string RootPath { get; set; } = string.Empty;
        public string? Platform { get; set; }
        public string? Title { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Meta
        public long? TotalDurationSeconds { get; set; }
        public long WatchedSeconds { get; set; }
        public string? FsHash { get; set; }
        public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "inprogress";

        public List<ChapterEntity> Chapters { get; set; } = new();
    }

    public class ChapterEntity
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required] public string Title { get; set; } = string.Empty;
        public int? Order { get; set; }
        public string RelPath { get; set; } = ".";

        // FK
        public Guid CourseEntityId { get; set; }
        public CourseEntity? Course { get; set; }

        public List<PartEntity> Parts { get; set; } = new();
    }

    public class PartEntity
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? FileName { get; set; }
        [Required] public string Path { get; set; } = string.Empty; // absolute
        public int? Index { get; set; }
        public string? Title { get; set; }
        public long? DurationSeconds { get; set; }
        public long FileSizeBytes { get; set; } = 0;
        public long LastPositionSeconds { get; set; } = 0;
        public bool Watched { get; set; } = false;

        // FK
        public Guid ChapterEntityId { get; set; }
        public ChapterEntity? Chapter { get; set; }
    }
}
