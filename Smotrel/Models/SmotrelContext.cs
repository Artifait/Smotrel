using Microsoft.EntityFrameworkCore;

namespace Smotrel.Models
{
    public class SmotrelContext : DbContext
    {
        public DbSet<CourseCardModel> CourseCards { get; set; }
        public DbSet<CourseCardModel> Courses { get; set; }

        public SmotrelContext() 
        {
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Filename=SmotrelData.db");
        }

        public async Task<bool> CourseCardPathExistsAsync(string path, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalized = path.Trim();

            return await CourseCards
                .AsNoTracking()
                .AnyAsync(x => x.Path == normalized, ct);
        }
    }
}
