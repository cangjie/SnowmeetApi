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
            modelBuilder.Entity<MaintainLive>().HasKey(c => c.id);
            modelBuilder.Entity<SchoolStaff>().HasKey(c => c.open_id);

            //SchoolLesson
          
            modelBuilder.Entity<SchoolLesson>().HasKey(c => c.id);
            
            /*
            modelBuilder.Entity<SchoolLesson>().Property(b => b.open_id)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.cell_number)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.name)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.gender)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.student_name)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.student_cell_number)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.student_gender)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.student_relation)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.demand)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.resort)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.lesson_date)
                .HasDefaultValue(DateTime.Now.AddDays(3));
            modelBuilder.Entity<SchoolLesson>().Property(b => b.training_plan)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.pay_method)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.order_id)
                .HasDefaultValue(0);
            modelBuilder.Entity<SchoolLesson>().Property(b => b.pay_state)
                .HasDefaultValue(0);
            modelBuilder.Entity<SchoolLesson>().Property(b => b.memo)
                .HasDefaultValueSql("''");
            modelBuilder.Entity<SchoolLesson>().Property(b => b.instructor_open_id)
                .HasDefaultValueSql("''");
            */
        }

        public DbSet<MaintainLive> MaintainLives {get; set;}

        public DbSet<SchoolStaff> SchoolStaffs { get; set; }

        public DbSet<SchoolLesson> SchoolLessons { get; set; }
    }
}
