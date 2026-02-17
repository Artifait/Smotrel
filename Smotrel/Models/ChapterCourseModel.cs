
namespace Smotrel.Models
{
    public class ChapterCourseModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public List<ChapterCourseModel> Chapters { get; set; } = [];
        public List<VideoModel> Videos { get; set; } = [];
    }
}
