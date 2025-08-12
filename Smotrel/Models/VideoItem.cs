
namespace Smotrel.Models
{
    public class VideoItem
    {
        public string? Title { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string? PartId { get; set; }       // связка с course.json
        public string? ChapterId { get; set; }
        public string? CourseId { get; set; }
        public long? Duration { get; set; }
        public long LastPosition { get; set; }
        public bool Watched { get; set; }
    }
}
