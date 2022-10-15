using System;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Ticket;
using SnowmeetApi.Models.Card;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Maintain;
using SnowmeetApi.Models.Background;
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

            modelBuilder.Entity<SnowmeetApi.Models.Maintain.Brand>().HasNoKey();

            //OrderOnline
            //modelBuilder.Entity<OrderOnline>().HasKey(c => c.id);

            //modelBuilder.Entity<Experience>().HasOne<OrderOnline>(e=>e.order).WithOne(e=>e.)

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
        
        public DbSet<SnowmeetApi.Models.Product.Product> Product { get; set; }

        public DbSet<OrderOnlineDetail> OrderOnlineDetails { get; set; }

        public DbSet<SnowmeetApi.Models.Experience> Experience { get; set; }

        public DbSet<SnowmeetApi.Models.Ticket.Ticket> Ticket { get; set; }

        public DbSet<SnowmeetApi.Models.Ticket.TicketTemplate> TicketTemplate { get; set; }

        public DbSet<SnowmeetApi.Models.Card.Card> Card { get; set; }

        public DbSet<SnowmeetApi.Models.BltDevice> BltDevice { get; set; }

        public DbSet<SnowmeetApi.Models.Users.Point> Point { get; set; }

        public DbSet<SnowmeetApi.Models.SummerMaintain> SummerMaintain { get; set; }

        public DbSet<SnowmeetApi.Models.Order.Mi7Order> mi7Order { get; set; }

        public DbSet<SnowmeetApi.Models.Shop> Shop { get; set; }

        public DbSet<SnowmeetApi.Models.Order.ShopSaleInteract> ShopSaleInteract { get; set; }

        public DbSet<SnowmeetApi.Models.Order.OrderPayment> OrderPayment { get; set; }

        public DbSet<SnowmeetApi.Models.Maintain.Brand> Brand { get; set; }

        public DbSet<SnowmeetApi.Models.UploadFile> UploadFile { get; set; }

        public DbSet<SnowmeetApi.Models.Maintain.Serial> Serial { get; set; }

        public DbSet<SnowmeetApi.Models.Maintain.MaintainLog> MaintainLog { get; set; }

        public DbSet<SnowmeetApi.Models.Background.BackgroundLoginSession> BackgroundLoginSession { get; set; }

        public DbSet<SnowmeetApi.Models.Order.Mi7OrderDetail> mi7OrderDetail { get; set; }

   
    }
}
