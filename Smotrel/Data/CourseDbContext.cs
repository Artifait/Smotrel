using Microsoft.EntityFrameworkCore;
using Smotrel.Data.Entities;

namespace Smotrel.Data
{
    public class CourseDbContext : DbContext
    {
        public CourseDbContext(DbContextOptions<CourseDbContext> opts) : base(opts) { }

        public DbSet<CourseEntity> Courses { get; set; } = null!;
        public DbSet<ChapterEntity> Chapters { get; set; } = null!;
        public DbSet<PartEntity> Parts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Indexes
            modelBuilder.Entity<CourseEntity>()
                .HasIndex(c => c.RootPath)
                .IsUnique();

            modelBuilder.Entity<PartEntity>()
                .HasIndex(p => p.Path)
                .IsUnique(false);

            modelBuilder.Entity<PartEntity>()
                .HasIndex(p => new { p.FileName, p.FileSizeBytes });
        }
    }
}
