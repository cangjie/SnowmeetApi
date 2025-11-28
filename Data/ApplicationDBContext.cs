using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using wechat_miniapp_base.Models;
using System;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SnowmeetApi.Models.UTV;
using SnowmeetApi.Models.Rent;

//using Aop.Api.Domain;
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
            //modelBuilder.Entity<Brand>().HasNoKey();
            modelBuilder.Entity<Models.Users.UnionId>().HasKey(u => new { u.union_id, u.open_id });
            modelBuilder.Entity<Models.DD.ExtendedProperties>().HasNoKey();
            modelBuilder.Entity<Models.DD.SysColumn>().HasNoKey();
            modelBuilder.Entity<OldWeixinReceive>().HasNoKey();
            modelBuilder.Entity<SnowmeetApi.Models.Maintain.MaintainReport>().HasNoKey();
            modelBuilder.Entity<Models.SaleReport>().HasNoKey();
            modelBuilder.Entity<Models.EPaymentDailyReport>().HasKey(e => new { e.biz_date, e.mch_id, e.pay_method });


            //modelBuilder.Entity<SkipassDailyPrice>().HasOne<Models.SkiPassProduct>().WithMany(s => s.dailyPrice).HasForeignKey(s => s.product_id);
            
            modelBuilder.Entity<Brand>().HasKey(b => new { b.brand_name, b.brand_type });
            modelBuilder.Entity<Member>().HasMany<RentOrderLog>().WithOne(m => m.member).HasForeignKey(r => r.oper_member_id);
            
            modelBuilder.Entity<Mi7ExportedSaleDetail>().HasNoKey();
            

            /////////////new season
            modelBuilder.Entity<MiniSession>().HasKey(m => new { m.session_key, m.session_type });
            modelBuilder.Entity<Brand>().HasKey(m => new { m.brand_name, m.brand_type });
            modelBuilder.Entity<RentPackageCategory>().HasKey(e => new { e.package_id, e.category_id });
            modelBuilder.Entity<RentProductDetailInfo>().HasKey(i => new { i.field_id, i.product_id });
            modelBuilder.Entity<RentProductDetailInfo>().HasKey(i => new { i.product_id, i.field_id });
            modelBuilder.Entity<GuarantyPayment>().HasKey(g => new { g.guaranty_id, g.payment_id });
            //modelBuilder.Entity<CareImage>().HasKey(c => new { c.care_id, c.image_id });
        }
        public DbSet<MaintainLive> MaintainLives { get; set; }
        public DbSet<Models.Users.MToken> MTokens { get; set; }
        public DbSet<Models.Users.UnionId> UnionIds { get; set; }
        public DbSet<Models.Users.MiniAppUser> MiniAppUsers { get; set; }
        public DbSet<Models.Users.OfficialAccoutUser> officialAccoutUsers { get; set; }
        public DbSet<OrderOnline> OrderOnlines { get; set; }
        public DbSet<WepayKey> WepayKeys { get; set; }
        public DbSet<WepayOrder> WepayOrders { get; set; }
        //public DbSet<OrderOnlineTemp> OrderOnlineTemp { get; set; }
        public DbSet<WepayOrderRefund> WePayOrderRefund { get; set; }

        public DbSet<OrderOnlineDetail> OrderOnlineDetails { get; set; }
        public DbSet<Experience> Experience { get; set; }

        public DbSet<BltDevice> BltDevice { get; set; }

        public DbSet<SummerMaintain> SummerMaintain { get; set; }
        public DbSet<Mi7Order> mi7Order { get; set; }

        public DbSet<ShopSaleInteract> ShopSaleInteract { get; set; }
        public DbSet<OrderPayment> OrderPayment { get; set; }

        public DbSet<UploadFile> UploadFile { get; set; }

        public DbSet<Models.Maintain.MaintainLog> MaintainLog { get; set; }
        public DbSet<Models.Background.BackgroundLoginSession> BackgroundLoginSession { get; set; }
        public DbSet<Mi7OrderDetail> mi7OrderDetail { get; set; }
        
        public DbSet<Models.SkiPassProduct> skiPassProduct { get; set; }
        public DbSet<OAReceive> oAReceive { get; set; }
        public DbSet<TicketLog> ticketLog { get; set; }
        public DbSet<ServiceMessage> ServiceMessage { get; set; }
        public DbSet<TemplateMessage> templateMessage { get; set; }
        public DbSet<Models.Rent.RentOrder> RentOrder { get; set; }
        public DbSet<Models.Rent.RentItem> RentItem { get; set; }
        public DbSet<Models.Rent.RentOrderDetail> RentOrderDetail { get; set; }
        public DbSet<Models.DD.SysObject> sysObject { get; set; }
        public DbSet<Models.DD.SysType> sysType { get; set; }
        public DbSet<Models.DD.ExtendedProperties> extendedProperties { get; set; }
        public DbSet<Models.DD.SysColumn> sysColumn { get; set; }
        public DbSet<UTVTrip> utvTrip { get; set; }
        public DbSet<UTVUsers> utvUser { get; set; }
        public DbSet<UTVVehicleSchedule> utvVehicleSchedule { get; set; }
        public DbSet<Vehicle> vehicle { get; set; }
        public DbSet<UTVReserve> utvReserve { get; set; }
        public DbSet<UTVRentItem> utvrentItem { get; set; }
        public DbSet<UTVUserGroup> uTVUserGroups { get; set; }
        public DbSet<BusinessReport> businessReport { get; set; }
        public DbSet<Models.OldWeixinPaymentOrder> oldWeixinPaymentOrder { get; set; }
        public DbSet<Models.OldWeixinReceive> oldWxReceive { get; set; }
        public DbSet<Models.WepayBalance> wepayBalance { get; set; }
        public DbSet<Models.WepaySummary> wepaySummary { get; set; }
        public DbSet<Models.Users.Vip> vip { get; set; }
        public DbSet<Models.Maintain.MaintainReport> maintainReport { get; set; }

        public DbSet<Models.SaleReport> saleReport { get; set; }
        public DbSet<Models.IdList> idList { get; set; }
        public DbSet<Models.Rent.RentOrderDetailLog> rentOrderDetailLog { get; set; }
        public DbSet<Models.WepayFlowBill> wepayFlowBill { get; set; }
        public DbSet<Models.EPaymentDailyReport> ePaymentDailyReport { get; set; }
        public DbSet<Models.AlipayMchId> alipayMchId { get; set; }
        public DbSet<Models.Kol> kol { get; set; }
        public DbSet<Models.AliDownloadFlowBill> aliDownloadFlowBill { get; set; }
        public DbSet<FinancialStatement> financialStatement { get; set; }
        public DbSet<RentProductImage> rentProductImage { get; set; }
        public DbSet<Models.School.Staff> schoolStaff { get; set; }
        public DbSet<Models.School.Course> schoolCourse { get; set; }
        public DbSet<Models.School.CourseStudent> courseStudent { get; set; }
        public DbSet<Models.SkiPass> skiPass { get; set; }
        public DbSet<Models.SkipassDailyPrice> skipassDailyPrice { get; set; }
        public DbSet<Models.Users.Referee> referee { get; set; }
        public DbSet<Models.ZiwoyouListOrder> ziwoyouOrder { get; set; }
        public DbSet<Models.Deposit.DepositTemplate> depositTemplate { get; set; }
        public DbSet<Models.Rent.RentAdditionalPayment> rentAdditionalPayment { get; set; }
        public DbSet<Models.Rent.RentOrderLog> rentOrderLog { get; set; }
        public DbSet<Models.Rent.RentReward> rentReward { get; set; }
        public DbSet<Models.Rent.RentRewardRefund> rentRewardRefund { get; set; }
        public DbSet<Models.Users.CellWhiteList> cellWhiteList { get; set; }
        public DbSet<Models.WebApiLog> webApiLog { get; set; }
        //public DbSet<Models.StaffModLog> staffModLog {get; set;}
        public DbSet<Models.Mi7ExportedSaleList> mi7ExportedSaleList { get; set; }
        public DbSet<Models.Mi7ExportedSaleDetail> mi7ExportedSaleDetail { get; set; }















        //New Season
        public DbSet<Member> member { get; set; }
        public DbSet<MemberSocialAccount> memberSocialAccount { get; set; }
        public DbSet<Staff> staff { get; set; }
        public DbSet<StaffSocialAccount> staffSocialAccount { get; set; }
        public DbSet<SocialAccountForJob> socialAccountForJob { get; set; }
        public DbSet<Shop> shop { get; set; }
        public DbSet<Order> order { get; set; }
        public DbSet<MiniSession> miniSession { get; set; }
        public DbSet<OrderPayment> orderPayment { get; set; }
        public DbSet<OrderPaymentRefund> paymentRefund { get; set; }
        //public DbSet<Models.PaymentShare> paymentShare { get; set; }
        public DbSet<Models.CoreDataModLog> coreDataModLog { get; set; }
        public DbSet<Models.Retail> retail { get; set; }
        public DbSet<Models.Care> care { get; set; }
        public DbSet<Models.CareTask> careTask { get; set; }
        public DbSet<Brand> brand { get; set; }
        public DbSet<Series> series { get; set; }
        public DbSet<RentCategory> rentCategory { get; set; }
        public DbSet<RentPrice> rentPrice { get; set; }
        public DbSet<RentCategoryInfoField> rentCategoryInfoField { get; set; }
        public DbSet<RentProduct> rentProduct { get; set; }
        public DbSet<RentProductDetailInfo> rentProductDetailInfo { get; set; }
        public DbSet<Rental> rental { get; set; }
        public DbSet<Models.RentalDetail> rentalDetail { get; set; }
        public DbSet<Models.RentItem> rentItem { get; set; }
        public DbSet<Guaranty> guaranty { get; set; }
        public DbSet<GuarantyPayment> guarantyPayment { get; set; }
        public DbSet<Models.DepositAccount> depositAccount { get; set; }
        public DbSet<Models.DepositBalance> depositBalance { get; set; }
        public DbSet<Models.ScanQrCode> scanQrCode { get; set; }
        public DbSet<Point> point { get; set; }
        public DbSet<RentPackage> rentPackage { get; set; }
        public DbSet<RentPackageCategory> rentPackageCategory { get; set; }
        public DbSet<Recept> recept { get; set; }
        public DbSet<Ticket> ticket { get; set; }
        public DbSet<TicketTemplate> ticketTemplate { get; set; }
        public DbSet<Card> card { get; set; }
        public DbSet<Printer> printer { get; set; }
        public DbSet<PrintTask> printTask { get; set; }
        public DbSet<Category> category { get; set; }
        public DbSet<Product> product { get; set; }
        public DbSet<ProductImage> productImage { get; set; }
        public DbSet<CategoryProperty> categoryProperty { get; set; }
        public DbSet<CategoryPropertyOption> categoryPropertyOption { get; set; }
        public DbSet<ProductProperty> productProperty { get; set; }
        public DbSet<ProductStock> productStock { get; set; }
        public DbSet<Discount> discount { get; set; }
        public DbSet<FdOrder> fdOrder { get; set; }
        public DbSet<RentalPricePreset> rentalPricePreset { get; set; }
        public DbSet<CareImage> careImage { get; set; }
        public DbSet<OrderPaymentRefund> orderPaymentRefund { get; set; }
        public DbSet<RentItemLog> rentItemLog { get; set; }
        public DbSet<OrderShareRelation> orderShareRelation {get; set;}
        public DbSet<ShareRelationBind> shareRelationBind {get; set;}
        public DbSet<OrderShare> orderShare {get; set;}
        public DbSet<PaymentShare> paymentShare {get; set;}

    }
}
