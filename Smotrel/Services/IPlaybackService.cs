
namespace Smotrel.Services
{
    public interface IPlaybackService
    {
        Task NotifyPositionAsync(string filePath, long seconds);
        Task SavePositionByPartIdAsync(string courseRootPath, Guid partId, long seconds);
        Task MarkWatchedByPartIdAsync(string courseRootPath, Guid partId);
        Task FlushAsync();
    }
}
