
namespace Smotrel.Models
{
    class ChapterCourseModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<ChapterCourseModel> Chapters { get; set; } = [];
        public List<VideoModel> Videos { get; set; } = [];
    }
}
