
using Microsoft.EntityFrameworkCore;
using Smotrel.Data.Entities;
using Smotrel.Data;
using System.IO;
using System.Text.Json;

namespace Smotrel.Services
{
    public class EfCourseRepository : ICourseRepository
    {
        // use local SQLite file per course inside .smotrel
        private const string DbFileName = "course.db";
        private const string CourseJsonBackupPrefix = "course.json.bak-";
        private const string DbBackupPrefix = "course.db.bak-";

        public string GetRepositoryFolder(string courseRootPath)
        {
            var repo = Path.Combine(courseRootPath, ".smotrel");
            Directory.CreateDirectory(repo);
            return repo;
        }

        private DbContextOptions<CourseDbContext> BuildOptions(string courseRootPath)
        {
            var repo = GetRepositoryFolder(courseRootPath);
            var dbPath = Path.Combine(repo, DbFileName);
            var builder = new DbContextOptionsBuilder<CourseDbContext>();
            builder.UseSqlite($"Data Source={dbPath}");
            return builder.Options;
        }

        public async Task<CourseEntity?> LoadAsync(string courseRootPath, CancellationToken ct = default)
        {
            var opts = BuildOptions(courseRootPath);
            await using var db = new CourseDbContext(opts);
            await db.Database.EnsureCreatedAsync(ct);

            var fullRoot = Path.GetFullPath(courseRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var course = await db.Courses
                .Include(c => c.Chapters)
                    .ThenInclude(ch => ch.Parts)
                .Where(c => c.RootPath == fullRoot)
                .FirstOrDefaultAsync(ct);

            return course;
        }

        public async Task SaveAsync(CourseEntity course, CancellationToken ct = default)
        {
            if (course == null) throw new ArgumentNullException(nameof(course));
            var opts = BuildOptions(course.RootPath);
            await using var db = new CourseDbContext(opts);
            await db.Database.EnsureCreatedAsync(ct);

            // Use a transaction for atomicity
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var fullRoot = Path.GetFullPath(course.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // find existing by rootPath
                var existing = await db.Courses
                    .Include(c => c.Chapters).ThenInclude(ch => ch.Parts)
                    .Where(c => c.RootPath == fullRoot)
                    .FirstOrDefaultAsync(ct);

                if (existing != null)
                {
                    // For simplicity, we delete existing chapters/parts and re-insert from incoming object
                    // Alternatively we could do upsert per part — but deletion ensures consistency.
                    db.Parts.RemoveRange(existing.Chapters.SelectMany(ch => ch.Parts));
                    db.Chapters.RemoveRange(existing.Chapters);
                    db.Courses.Remove(existing);
                    await db.SaveChangesAsync(ct);
                }

                // Normalize root path before save
                course.RootPath = fullRoot;
                // detach IDs? keep incoming IDs if present (we prefer to preserve GUIDs if merge assigned them)
                db.Courses.Add(course);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        public async Task BackupAsync(string courseRootPath, string? reason = null, CancellationToken ct = default)
        {
            var repo = GetRepositoryFolder(courseRootPath);
            var dbPath = Path.Combine(repo, DbFileName);
            var backups = Path.Combine(repo, "backups");
            Directory.CreateDirectory(backups);

            var ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
            if (File.Exists(dbPath))
            {
                var dbBak = Path.Combine(backups, DbBackupPrefix + ts + ".db");
                File.Copy(dbPath, dbBak, overwrite: true);
            }

            // Also dump current course into JSON backup for easy inspection (if DB exists and course loaded)
            try
            {
                var course = await LoadAsync(courseRootPath, ct);
                if (course != null)
                {
                    var json = JsonSerializer.Serialize(course, new JsonSerializerOptions { WriteIndented = true });
                    var jsonBak = Path.Combine(backups, CourseJsonBackupPrefix + ts + ".json");
                    await File.WriteAllTextAsync(jsonBak, json, ct);
                }
            }
            catch
            {
                // ignore backup errors (do not throw)
            }
        }
    }
}
