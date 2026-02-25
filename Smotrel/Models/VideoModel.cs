
using System.ComponentModel.DataAnnotations.Schema;

namespace Smotrel.Models
{
    public class VideoModel : IVideo
    {
        public int Id { get; set; }
        public int RelativeIndex { get; set; }
        public int AbsoluteIndex { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty
;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public bool IsWatched { get; set; } = false;
        public TimeSpan LastPosition { get; set; } = TimeSpan.Zero;

        [NotMapped]
        public List<VideoTimestamp> Timestamps { get; set; } = [];
    }
}