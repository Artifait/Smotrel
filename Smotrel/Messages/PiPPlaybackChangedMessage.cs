
namespace Smotrel.Messages
{
    public record PiPPlaybackChangedMessage(string FilePath, Guid? PartId, long PositionSeconds, double Speed, double Volume, bool IsPlaying);
}
