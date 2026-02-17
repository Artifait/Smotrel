using Microsoft.EntityFrameworkCore;

namespace Smotrel.Models
{
    public class AppContext : DbContext
    {
        public DbSet<CourseCardModel> CourseCards { get; set; }
        public DbSet<CourseCardModel> Courses { get; set; }

        public AppContext() 
        {
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Filename=SmotrelData.db");
        }
    }
}
