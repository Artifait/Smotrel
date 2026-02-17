
namespace Smotrel.Models
{
    public class CourseModel
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;

        public ChapterCourseModel MainChapter { get; set; } = null!;
    }
}
