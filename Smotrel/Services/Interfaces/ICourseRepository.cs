using Smotrel.Data.Entities;
using Smotrel.Models.Course;

namespace Smotrel.Services.Interfaces
{
    public interface ICourseRepository
    {
        Task<CourseEntity?> LoadAsync(string courseRootPath, CancellationToken ct = default);
        Task SaveAsync(CourseEntity course, CancellationToken ct = default);
        Task BackupAsync(string courseRootPath, string? reason = null, CancellationToken ct = default);
        string GetRepositoryFolder(string courseRootPath);
    }
}
