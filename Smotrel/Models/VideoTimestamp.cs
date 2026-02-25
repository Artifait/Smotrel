
namespace Smotrel.Models
{
    public class VideoTimestamp : ITimestamp
    {
        public IVideo Video { get; init; }
        public TimeSpan Time { get; init; }
        public string Description { get; init; } = string.Empty;

        public VideoTimestamp(IVideo video, TimeSpan time, string description)
        {
            Video = video;
            Time = time;
            Description = description ?? string.Empty;
        }

        public override string ToString() => $"{Time:hh\\:mm\\:ss} - {Description}";
    }
}