
namespace Smotrel.Models.Course
{
    public class PartModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? FileName { get; set; }
        public string Path { get; set; } = string.Empty; // absolute path
        public int? Index { get; set; }
        public string? Title { get; set; }
        public long? Duration { get; set; }    // seconds
        public long LastPosition { get; set; } // seconds
        public bool Watched { get; set; } = false;
    }
}
