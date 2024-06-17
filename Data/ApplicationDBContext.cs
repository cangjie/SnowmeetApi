using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;
using SnowmeetApi.Models.UTV;
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

            modelBuilder.Entity<SnowmeetApi.Models.Users.UnionId>().HasKey(u => new { u.union_id, u.open_id });

            modelBuilder.Entity<SnowmeetApi.Models.DD.ExtendedProperties>().HasNoKey();

            modelBuilder.Entity<SnowmeetApi.Models.DD.SysColumn>().HasNoKey();

            modelBuilder.Entity<SnowmeetApi.Models.OldWeixinReceive>().HasNoKey();

            modelBuilder.Entity<SnowmeetApi.Models.Maintain.MaintainReport>().HasNoKey();

            modelBuilder.Entity<Models.Order.SaleReport>().HasNoKey();

            modelBuilder.Entity<Models.Order.EPaymentDailyReport>().HasKey(e => new { e.biz_date, e.mch_id, e.pay_method });
            

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

        public DbSet<SnowmeetApi.Models.Order.OrderPaymentRefund> OrderPaymentRefund { get; set; }

        public DbSet<SnowmeetApi.Models.Product.SkiPass> SkiPass { get; set; }

        public DbSet<SnowmeetApi.Models.OAReceive> oAReceive { get; set; }

        public DbSet<SnowmeetApi.Models.Ticket.TicketLog> ticketLog { get; set; }

        public DbSet<SnowmeetApi.Models.ServiceMessage> ServiceMessage { get; set; }

        public DbSet<SnowmeetApi.Models.TemplateMessage> templateMessage { get; set; }

        public DbSet<SnowmeetApi.Models.Rent.RentOrder> RentOrder { get; set; }

        public DbSet<SnowmeetApi.Models.Rent.RentItem> RentItem { get; set; }

        public DbSet<SnowmeetApi.Models.Rent.RentOrderDetail> RentOrderDetail { get; set; }

        public DbSet<SnowmeetApi.Models.Recept> Recept { get; set; }

        public DbSet<SnowmeetApi.Models.DD.SysObject> sysObject { get; set; }

        public DbSet<SnowmeetApi.Models.DD.SysType> sysType { get; set; }

        public DbSet<SnowmeetApi.Models.DD.ExtendedProperties> extendedProperties { get; set; }

        public DbSet<SnowmeetApi.Models.DD.SysColumn> sysColumn { get; set; }

        public DbSet<UTVTrip> utvTrip { get; set; }

        public DbSet<UTVUsers> utvUser { get; set; }

        public DbSet<UTVVehicleSchedule> utvVehicleSchedule { get; set; }

        public DbSet<Vehicle> vehicle { get; set; }

        public DbSet<UTVReserve> utvReserve { get; set; }

        public DbSet<UTVRentItem> utvrentItem { get; set;}

        public DbSet<UTVUserGroup> uTVUserGroups { get; set; }

        //public DbSet<WepayTransaction> wepayTransaction { get; set; }

        public DbSet<BusinessReport> businessReport { get; set; }

        public DbSet<Models.Order.OldWeixinPaymentOrder> oldWeixinPaymentOrder { get; set; }

        public DbSet<Models.OldWeixinReceive> oldWxReceive { get; set; }

        public DbSet<Models.Order.WepayBalance> wepayBalance { get; set; }

        public DbSet<Models.Order.WepaySummary> wepaySummary { get; set; }

        public DbSet<Vip> vip { get; set; }


        public DbSet<Models.Maintain.MaintainReport> maintainReport { get; set; }


        public DbSet<SnowmeetApi.Models.Printer> Printer { get; set; }

        public DbSet<Models.Order.SaleReport> saleReport { get; set; }

        public DbSet<Models.IdList> idList { get; set; }

        public DbSet<Models.Rent.RentOrderDetailLog> rentOrderDetailLog { get; set; }

        public DbSet<Models.Order.WepayFlowBill> wepayFlowBill { get; set; }

        public DbSet<Models.Order.EPaymentDailyReport> ePaymentDailyReport { get; set; }

        public DbSet<Models.Order.AlipayMchId> alipayMchId { get; set; }

        public DbSet<Models.Order.Kol> kol {get; set;}

        public DbSet<Models.Order.PaymentShare> paymentShare {get; set;}

        public DbSet<Models.Order.AliDownloadFlowBill> aliDownloadFlowBill {get; set; }

    }
}
