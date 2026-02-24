
namespace Smotrel.Models
{
    public class CourseCardModel
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;

        public int CourseId { get; set; }
        public CourseModel Course { get; set; } = null!;

        public override string ToString()
        {
            return Label;
        }
    }
}
