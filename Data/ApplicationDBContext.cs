using System;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;
//using SnowmeetApi.Models.rfid;
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

            //OrderOnline
            modelBuilder.Entity<OrderOnline>().HasKey(c => c.id);

        }

        public DbSet<MaintainLive> MaintainLives {get; set;}
        public DbSet<SchoolStaff> SchoolStaffs { get; set; }
        public DbSet<SchoolLesson> SchoolLessons { get; set; }
        public DbSet<MiniSession> MiniSessons { get; set; }
        public DbSet<MToken> MTokens { get; set; }
        public DbSet<UnionId> UnionIds { get; set; }
        public DbSet<MiniAppUser> MiniAppUsers { get; set; }
        public DbSet<OfficialAccoutUser> officialAccoutUsers { get; set; }
        public DbSet<OrderOnline> OrderOnlines { get; set; }

        public DbSet<WepayKey> WepayKeys { get; set; }

        public DbSet<WepayOrder> WepayOrders { get; set; }

        public DbSet<SnowmeetApi.Models.OrderOnlineTemp> OrderOnlineTemp { get; set; }
        
        public DbSet<SnowmeetApi.Models.WepayOrderRefund> WePayOrderRefund { get; set; }
    }
}
