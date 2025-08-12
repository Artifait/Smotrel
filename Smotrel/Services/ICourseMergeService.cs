
using Smotrel.Data.Entities;

namespace Smotrel.Services
{
    public interface ICourseMergeService
    {
        /// <summary>
        /// Merge existing course metadata (previously saved JSON) into newly scanned CourseModel.
        /// Возвращает merged CourseModel (на основе scanned, но с перенесёнными id/positions/watched) и MergeResult отчёт.
        /// </summary>
        MergeResult Merge(CourseEntity existing, CourseEntity scanned);
    }
}
