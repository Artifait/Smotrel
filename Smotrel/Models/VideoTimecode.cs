using Smotrel.Interfaces;

namespace Smotrel.Models
{
    public class VideoTimecode : ITimecode
    {
        public TimeSpan Position { get; set; }
        public string Label { get; set; } = string.Empty;

        public static VideoTimecode Create(TimeSpan position, string label)
            => new VideoTimecode { Position = position, Label = label };
    }
}