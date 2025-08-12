
using Smotrel.Data.Entities;
using Smotrel.Models.Course;

namespace Smotrel.Services
{
    public interface ICourseScanner
    {
        /// <summary>Сканирует корень курса и возвращает CourseEntity (без сохранения в БД).</summary>
        Task<CourseEntity> ScanAsync(string rootPath, bool tryGetDurations = false, CancellationToken ct = default);
    }
}
