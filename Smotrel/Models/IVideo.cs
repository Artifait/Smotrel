
namespace Smotrel.Models
{
    public interface IVideo
    {
        int Id { get; }
        int RelativeIndex { get; }
        int AbsoluteIndex { get; }
        string Title { get; }
        string Path { get; }
        TimeSpan Duration { get; }
    }
}
