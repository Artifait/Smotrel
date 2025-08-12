
namespace Smotrel.Models.Course
{
    public class CourseModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? Platform { get; set; }
        public string? Title { get; set; }
        public string RootPath { get; set; } = string.Empty;
        public List<ChapterModel> Chapters { get; set; } = new List<ChapterModel>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public CourseMeta Meta { get; set; } = new CourseMeta();
        public string Status { get; set; } = "inprogress";
    }

    public class CourseMeta
    {
        /// <summary>Total duration in seconds (nullable if unknown)</summary>
        public long? TotalDuration { get; set; }
        public long WatchedSeconds { get; set; }
        /// <summary>Filesystem hash for folder state</summary>
        public string? FsHash { get; set; }
        public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;
    }

}
