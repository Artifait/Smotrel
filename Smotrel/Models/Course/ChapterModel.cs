
namespace Smotrel.Models.Course
{
    public class ChapterModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? Title { get; set; }
        public int? Order { get; set; }
        /// <summary>Relative path from course root; "." for root</summary>
        public string RelPath { get; set; } = ".";
        public List<PartModel> Parts { get; set; } = new List<PartModel>();
    }
}
