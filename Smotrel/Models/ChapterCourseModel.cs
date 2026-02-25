
namespace Smotrel.Models
{
    public class ChapterCourseModel
    {
        public int Id { get; set; }
        public int RelativeIndex { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;

        public int? ParentId { get; set; }
        public ChapterCourseModel? Parent { get; set; }
        public List<ChapterCourseModel> Chapters { get; set; } = new List<ChapterCourseModel>();
        public List<VideoModel> Videos { get; set; } = new List<VideoModel>();
    }
}
