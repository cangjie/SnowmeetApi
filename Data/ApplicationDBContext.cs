﻿using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;
using SnowmeetApi.Models.UTV;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.School;
using System;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.Maintain;
using SnowmeetApi.Models.Order;
using Aop.Api.Domain;
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
            modelBuilder.Entity<SnowmeetApi.Models.Maintain.Brand>().HasNoKey();
            modelBuilder.Entity<SnowmeetApi.Models.Users.UnionId>().HasKey(u => new { u.union_id, u.open_id });
            modelBuilder.Entity<SnowmeetApi.Models.DD.ExtendedProperties>().HasNoKey();
            modelBuilder.Entity<SnowmeetApi.Models.DD.SysColumn>().HasNoKey();
            modelBuilder.Entity<SnowmeetApi.Models.OldWeixinReceive>().HasNoKey();
            modelBuilder.Entity<SnowmeetApi.Models.Maintain.MaintainReport>().HasNoKey();
            modelBuilder.Entity<Models.Order.SaleReport>().HasNoKey();
            modelBuilder.Entity<Models.Order.EPaymentDailyReport>().HasKey(e => new { e.biz_date, e.mch_id, e.pay_method });
            modelBuilder.Entity<RentPrice>().HasOne<RentCategory>().WithMany(r => r.priceList).HasForeignKey(r => r.category_id);
            modelBuilder.Entity<RentPackageCategory>().HasKey(e => new {e.package_id, e.category_id});
            modelBuilder.Entity<RentPackageCategory>().HasOne<RentPackage>().WithMany(r => r.rentPackageCategoryList).HasForeignKey(r => r.package_id);
            modelBuilder.Entity<RentPrice>().HasOne<RentPackage>().WithMany( r => r.rentPackagePriceList).HasForeignKey(r => r.package_id);
            modelBuilder.Entity<RentCategory>().HasMany<RentPackageCategory>().WithOne(r => r.rentCategory).HasForeignKey(r => r.category_id);
            modelBuilder.Entity<RentCategoryInfoField>().HasOne<RentCategory>().WithMany(r => r.infoFields).HasForeignKey(r => r.category_id);
            modelBuilder.Entity<RentProductDetailInfo>().HasKey(i => new {i.field_id, i.product_id});
            modelBuilder.Entity<RentProductDetailInfo>().HasOne<RentProduct>().WithMany(r => r.detailInfo).HasForeignKey(r => r.product_id);
            modelBuilder.Entity<RentProductImage>().HasOne<RentProduct>().WithMany(r => r.images).HasForeignKey(r => r.product_id);
            modelBuilder.Entity<RentCategoryInfoField>().HasMany<RentProductDetailInfo>().WithOne(r => r.field).HasForeignKey(r => r.field_id);
            modelBuilder.Entity<RentProduct>().HasOne<RentCategory>().WithMany(r => r.productList).HasForeignKey(r => r.category_id);
            modelBuilder.Entity<RentProductDetailInfo>().HasKey(i => new {i.product_id, i.field_id});
            modelBuilder.Entity<SkipassDailyPrice>().HasOne<Models.Product.SkiPass>().WithMany(s => s.dailyPrice).HasForeignKey(s => s.product_id);
            //modelBuilder.Entity<MaintainLog>().HasOne<Models.MaintainLive>().WithMany(m => m.taskLog).HasForeignKey(m => m.task_id);
            //modelBuilder.Entity<OrderOnline>().HasMany<MaintainLive>().WithOne(m => m.order).HasForeignKey(m => m.order_id);
            modelBuilder.Entity<Brand>().HasKey(b => new {b.brand_name, b.brand_type});
            modelBuilder.Entity<Models.Users.Member>().HasMany<RentOrderLog>().WithOne(m => m.member).HasForeignKey(r => r.oper_member_id);
            //modelBuilder.Entity<RentOrder>().HasOne<Recept>().WithOne().HasForeignKey<Recept>(r => r.submit_return_id);


            modelBuilder.Entity<MemberSocialAccount>().HasAlternateKey(m => m.num);
            modelBuilder.Entity<OrderOnline>().HasOne(o => o.msa).WithMany(m => m.orders).HasForeignKey(o => o.open_id).HasPrincipalKey(m => m.num);
            modelBuilder.Entity<Mi7ExportedSaleDetail>().HasNoKey();
            modelBuilder.Entity<MemberSocialAccount>().HasMany<MaintainLog>().WithOne(l => l.msa).HasForeignKey(l => l.staff_open_id).HasPrincipalKey(m => m.num);
            modelBuilder.Entity<MemberSocialAccount>().HasMany<Models.MaintainLive>().WithOne(m => m.staffMsa).HasForeignKey(m => m.service_open_id).HasPrincipalKey(m => m.num);
            modelBuilder.Entity<MemberSocialAccount>().HasMany<Models.Rent.RentOrder>().WithOne(r => r.receptMsa).HasForeignKey(m => m.staff_open_id).HasPrincipalKey(m => m.num);
            modelBuilder.Entity<MemberSocialAccount>().HasMany<Models.Rent.RentOrderDetailLog>().WithOne(r => r.msa).HasForeignKey(m => m.staff_open_id).HasPrincipalKey(m => m.num);
            modelBuilder.Entity<MemberSocialAccount>().HasMany<Models.Rent.RentOrderDetail>().WithOne(r => r.returnMsa).HasForeignKey(m => m.return_staff).HasPrincipalKey(m => m.num);
            modelBuilder.Entity<MemberSocialAccount>().HasMany<Models.Order.OrderPaymentRefund>().WithOne(r => r.msa).HasForeignKey(m => m.oper).HasPrincipalKey(m => m.num);
            modelBuilder.Entity<MemberSocialAccount>().HasMany<Models.Order.OrderPayment>().WithOne(r => r.msa).HasForeignKey(m => m.staff_open_id).HasPrincipalKey(m => m.num);
        }

        public DbSet<MaintainLive> MaintainLives {get; set;}
       
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
        public DbSet<SnowmeetApi.Models.Users.Member> member { get; set; }
        public DbSet<SnowmeetApi.Models.Users.MemberSocialAccount> memberSocialAccount { get; set; }
        public DbSet<SnowmeetApi.Models.Rent.RentCategory> rentCategory { get; set; }
        public DbSet<SnowmeetApi.Models.Rent.RentPrice> rentPrice {get; set;}
        public DbSet<SnowmeetApi.Models.Rent.RentPackage> rentPackage {get; set;}
        public DbSet<SnowmeetApi.Models.Rent.RentPackageCategory> rentPackageCategory {get; set; }
        public DbSet<SnowmeetApi.Models.Rent.RentCategoryInfoField> rentCategoryInfoField {get; set; }
        public DbSet<SnowmeetApi.Models.Order.FinancialStatement> financialStatement {get; set;}
        public DbSet<SnowmeetApi.Models.Rent.RentProduct> rentProduct {get; set;}
        public DbSet<SnowmeetApi.Models.Rent.RentProductDetailInfo> rentProductDetailInfo {get;set;}
        public DbSet<RentProductImage> rentProductImage {get; set;}
        public DbSet<Staff> schoolStaff {get; set;}
        public DbSet<Course> schoolCourse {get; set;}
        public DbSet<CourseStudent> courseStudent {get; set;}
        public DbSet<Models.SkiPass.SkiPass> skiPass {get; set;}
        public DbSet<Models.Product.SkipassDailyPrice> skipassDailyPrice {get; set;}
        public DbSet<Models.Users.Referee> referee {get; set;}
        public DbSet<Models.SkiPass.ZiwoyouListOrder> ziwoyouOrder {get; set;}
        public DbSet<Models.Deposit.DepositAccount> depositAccount {get; set;}
        public DbSet<Models.Deposit.DepositBalance> depositBalance {get; set;}
        public DbSet<Models.Deposit.DepositTemplate> depositTemplate {get; set;}
        public DbSet<Models.Rent.RentAdditionalPayment> rentAdditionalPayment {get; set;}
        public DbSet<Models.Rent.RentOrderLog> rentOrderLog { get; set; }
        public DbSet<Models.Rent.RentReward> rentReward { get; set; }
        public DbSet<Models.Rent.RentRewardRefund> rentRewardRefund { get; set; }
        public DbSet<Models.Users.CellWhiteList> cellWhiteList {get; set;}
        public DbSet<Models.WebApiLog> webApiLog{get; set;}
        public DbSet<Models.StaffModLog> staffModLog {get; set;}
        public DbSet<Models.Mi7ExportedSaleList> mi7ExportedSaleList {get; set;}
        public DbSet<Models.Mi7ExportedSaleDetail> mi7ExportedSaleDetail {get; set;}
    }
}
