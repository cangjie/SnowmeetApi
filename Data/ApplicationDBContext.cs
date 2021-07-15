using System;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;

namespace SnowmeetApi.Data
{
    public class ApplicationDBContext : DbContext
    {
        public ApplicationDBContext(DbContextOptions<ApplicationDBContext> options)
            : base(options)
        {

        }

        

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //MaintainLive
            modelBuilder.Entity<MaintainLive>().HasKey(c => c.id);

            //SchoolStaff
            modelBuilder.Entity<SchoolStaff>().HasKey(c => c.open_id);

            //SchoolLesson
            modelBuilder.Entity<SchoolLesson>().HasKey(c => c.id);

            //MiniSession
            modelBuilder.Entity<MiniSession>().HasKey(c => c.session_key);
        }

        public DbSet<MaintainLive> MaintainLives {get; set;}

        public DbSet<SchoolStaff> SchoolStaffs { get; set; }

        public DbSet<SchoolLesson> SchoolLessons { get; set; }

        public DbSet<MiniSession> MiniSessons { get; set; }
    }
}
