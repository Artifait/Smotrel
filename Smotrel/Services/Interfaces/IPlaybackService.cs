namespace Smotrel.Services.Interfaces
{
    public interface IPlaybackService
    {
        Task NotifyPositionAsync(string filePath, long seconds); // debounced resume update
        Task SavePositionByPartIdAsync(string courseRootPath, Guid partId, long seconds); // immediate save
        Task MarkWatchedByPartIdAsync(string courseRootPath, Guid partId);
        Task ClearResumeAsync(string courseRootPath); // remove resume marker
        Task FlushAsync();
    }
}
