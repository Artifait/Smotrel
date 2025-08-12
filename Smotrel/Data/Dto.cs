using System;
using System.Collections.Generic;

namespace Smotrel.Data
{
    public class CourseDto
    {
        public Guid Id { get; set; }
        public string RootPath { get; set; } = "";
        public string? Platform { get; set; }
        public string? Title { get; set; }
        public List<ChapterDto> Chapters { get; set; } = new();
        public CourseMetaDto? Meta { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? FsHash { get; set; }
        public DateTime LastScannedAt { get; set; }
        public Guid? LastPlayedPartId { get; set; }
        public long LastPlayedPositionSeconds { get; set; }
        public string Status { get; set; } = "inprogress";
    }

    public class ChapterDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public int? Order { get; set; }
        public string RelPath { get; set; } = ".";
        public List<PartDto> Parts { get; set; } = new();
    }

    public class PartDto
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = "";
        public string Path { get; set; } = "";
        public int? Index { get; set; }
        public string? Title { get; set; }
        public long? DurationSeconds { get; set; }
        public long FileSizeBytes { get; set; }
        public long LastPositionSeconds { get; set; }
        public bool Watched { get; set; }
    }

    public class CourseMetaDto
    {
        public long? TotalDuration { get; set; }
        public int TotalParts { get; set; }
        public long WatchedSeconds { get; set; }
    }
}
