
using Microsoft.EntityFrameworkCore;
using Smotrel.Data.Entities;
using Smotrel.Data;
using System.IO;
using System.Text.Json;
using Smotrel.Services.Interfaces;

namespace Smotrel.Services.Implementations
{
    public class CourseJsonRepository : ICourseRepository
    {
        private const string JsonFileName = "course.json";

        public string GetRepositoryFolder(string courseRootPath)
        {
            var repo = Path.Combine(courseRootPath, ".smotrel");
            Directory.CreateDirectory(repo);
            return repo;
        }

        public async Task<CourseEntity?> LoadAsync(string courseRootPath, CancellationToken ct = default)
        {
            var repo = GetRepositoryFolder(courseRootPath);
            var jsonPath = Path.Combine(repo, JsonFileName);

            // If JSON exists — load and map to entity
            if (File.Exists(jsonPath))
            {
                var json = await File.ReadAllTextAsync(jsonPath, ct);
                var dto = JsonSerializer.Deserialize<CourseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                if (dto == null) return null;
                return MapDtoToEntity(dto);
            }

            // If JSON not found but sqlite DB exists, try migrate (best-effort)
            var dbPath = Path.Combine(repo, "course.db");
            if (File.Exists(dbPath))
            {
                try
                {
                    // Try to load via EF context if available in project
                    var optionsBuilder = new DbContextOptionsBuilder<CourseDbContext>();
                    optionsBuilder.UseSqlite($"Data Source={dbPath}");
                    using var ctx = new CourseDbContext(optionsBuilder.Options);
                    var course = ctx.Courses
                        .Include(c => c.Chapters)
                            .ThenInclude(ch => ch.Parts)
                        .FirstOrDefault(c => c.RootPath == Path.GetFullPath(courseRootPath).TrimEnd(Path.DirectorySeparatorChar));
                    if (course != null)
                    {
                        // save to JSON for future runs
                        await SaveAsync(course, ct);
                        return course;
                    }
                }
                catch
                {
                    // ignore migration failures
                }
            }

            return null;
        }

        public async Task SaveAsync(CourseEntity course, CancellationToken ct = default)
        {
            if (course == null) throw new ArgumentNullException(nameof(course));
            var repo = GetRepositoryFolder(course.RootPath);
            var jsonPath = Path.Combine(repo, JsonFileName);
            var tmpPath = jsonPath + ".tmp";

            var dto = MapEntityToDto(course);
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(tmpPath, json, ct);

            // backup previous
            if (File.Exists(jsonPath))
            {
                var backups = Path.Combine(repo, "backups");
                Directory.CreateDirectory(backups);
                var ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
                var bak = Path.Combine(backups, $"course.json.bak-{ts}.json");
                File.Copy(jsonPath, bak, overwrite: true);
            }

            // atomic replace
            File.Move(tmpPath, jsonPath, overwrite: true);
        }

        public Task BackupAsync(string courseRootPath, string? reason = null, CancellationToken ct = default)
        {
            var repo = GetRepositoryFolder(courseRootPath);
            var jsonPath = Path.Combine(repo, JsonFileName);
            if (!File.Exists(jsonPath)) return Task.CompletedTask;
            var backups = Path.Combine(repo, "backups");
            Directory.CreateDirectory(backups);
            var ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
            var dst = Path.Combine(backups, $"course.json.bak-{ts}.json");
            File.Copy(jsonPath, dst, overwrite: true);
            return Task.CompletedTask;
        }

        // ---------------- mapping helpers ----------------

        private static CourseEntity MapDtoToEntity(CourseDto dto)
        {
            var course = new CourseEntity
            {
                Id = dto.Id,
                RootPath = dto.RootPath,
                Platform = dto.Platform,
                Title = dto.Title,
                CreatedAt = dto.CreatedAt,
                FsHash = dto.FsHash,
                LastScannedAt = dto.LastScannedAt,
                TotalDurationSeconds = dto.Meta?.TotalDuration,
                WatchedSeconds = dto.Meta?.WatchedSeconds ?? 0,
                Status = dto.Status ?? "inprogress",
                LastPlayedPartId = dto.LastPlayedPartId,
                LastPlayedPositionSeconds = dto.LastPlayedPositionSeconds
            };

            course.Chapters = dto.Chapters.Select(ch => new ChapterEntity
            {
                Id = ch.Id,
                Title = ch.Title,
                Order = ch.Order,
                RelPath = ch.RelPath,
                CourseEntityId = course.Id,
                Parts = ch.Parts.Select(p => new PartEntity
                {
                    Id = p.Id,
                    FileName = p.FileName,
                    Path = p.Path,
                    Index = p.Index,
                    Title = p.Title,
                    DurationSeconds = p.DurationSeconds,
                    FileSizeBytes = p.FileSizeBytes,
                    LastPositionSeconds = p.LastPositionSeconds,
                    Watched = p.Watched,
                    ChapterEntityId = ch.Id
                }).ToList()
            }).ToList();

            return course;
        }

        private static CourseDto MapEntityToDto(CourseEntity e)
        {
            var dto = new CourseDto
            {
                Id = e.Id,
                RootPath = e.RootPath,
                Platform = e.Platform,
                Title = e.Title,
                CreatedAt = e.CreatedAt,
                FsHash = e.FsHash,
                LastScannedAt = e.LastScannedAt,
                LastPlayedPartId = e.LastPlayedPartId,
                LastPlayedPositionSeconds = e.LastPlayedPositionSeconds,
                Status = e.Status,
                Meta = new CourseMetaDto
                {
                    TotalDuration = e.TotalDurationSeconds,
                    TotalParts = e.Chapters?.Sum(ch => ch.Parts.Count) ?? 0,
                    WatchedSeconds = e.WatchedSeconds
                }
            };

            if (e.Chapters != null)
            {
                foreach (var ch in e.Chapters)
                {
                    var chDto = new ChapterDto
                    {
                        Id = ch.Id,
                        Title = ch.Title,
                        Order = ch.Order,
                        RelPath = ch.RelPath
                    };
                    foreach (var p in ch.Parts)
                    {
                        chDto.Parts.Add(new PartDto
                        {
                            Id = p.Id,
                            FileName = p.FileName,
                            Path = p.Path,
                            Index = p.Index,
                            Title = p.Title,
                            DurationSeconds = p.DurationSeconds,
                            FileSizeBytes = p.FileSizeBytes,
                            LastPositionSeconds = p.LastPositionSeconds,
                            Watched = p.Watched
                        });
                    }
                    dto.Chapters.Add(chDto);
                }
            }

            return dto;
        }
    }
}
