
namespace Smotrel.Models
{
    public interface ITimestamp
    {
        IVideo Video { get; }
        TimeSpan Time { get; }
        string Description { get; }
    }
}