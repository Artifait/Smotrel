using Smotrel.Data.Entities;
using Smotrel.Models.Course;

namespace Smotrel.Services.Interfaces
{
    public interface ICourseScanner
    {
        Task<CourseEntity> ScanAsync(string rootPath, bool tryGetDurations = false, CancellationToken ct = default);
    }
}
