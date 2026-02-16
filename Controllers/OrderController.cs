using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Threading;
using NPOI.SS.Formula.Functions;
using Humanizer;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class OrderController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        public OrderController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
        }
        [NonAction]
        public async Task<SnowmeetApi.Models.Order> GetOrder(int orderId)
        {
            SnowmeetApi.Models.Order order = await _db.order.Where(o => o.id == orderId).AsNoTracking().FirstOrDefaultAsync();
            if (order == null)
            {
                return null;
            }
            order.retails = await _db.order.Entry(order).Collection(o => o.retails).Query().Where(r => r.valid == 1).AsNoTracking().ToListAsync();
            if (order.type == "零售")
            {
                order.retailImages = await _db.retailImage.Include(i => i.image).Where(i => i.order_id == orderId && i.valid).AsNoTracking().ToListAsync();
            }
            order.cares = await _db.order.Entry(order).Collection(o => o.cares).Query()
                .Include(c => c.tasks.OrderBy(t => t.id)).ThenInclude(t => t.staff)
                .Include(c => c.tasks.OrderBy(t => t.id)).ThenInclude(t => t.terminateStaff)
                .Include(c => c.pickImage)
                .Include(c => c.careImages).ThenInclude(i => i.image).AsNoTracking().ToListAsync();
            order.fdOrders = await _db.order.Entry(order).Collection(o => o.fdOrders).Query()
                .Where(r => r.valid == 1).AsNoTracking()
                .Include(f => f.product).ThenInclude(p => p.category)
                .Include(f => f.discounts.Where(d => d.valid == 1 && d.biz_type.Trim().Equals("餐饮"))).AsNoTracking().ToListAsync();
            order.rentals = await _db.order.Entry(order).Collection(o => o.rentals).Query().AsNoTracking()
                .Include(r => r.discounts.Where(d => d.valid == 1 && d.biz_type.Trim().Equals("租赁"))).AsNoTracking()
                .Include(r => r.details.Where(d => d.valid == 1)).AsNoTracking()
                .Include(r => r.rentItems.Where(i => i.valid == 1))
                    .ThenInclude(i => i.logs.OrderByDescending(o => o.id))
                        .ThenInclude(l => l.staff)
                .Include(r => r.guaranties.Where(g => g.valid == 1 && g.biz_type.Trim().Equals("租赁"))).ThenInclude(g => g.guarantyPayments).ThenInclude(g => g.payment)
                .Where(r => r.valid == 1 && (r.appending == null || (r.appending == false && r.append_commit_time != null))).AsNoTracking().ToListAsync();
            order.appendingRentals = await _db.order.Entry(order).Collection(o => o.rentals).Query().AsNoTracking()
                .Include(r => r.discounts.Where(d => d.valid == 1 && d.biz_type.Trim().Equals("租赁"))).AsNoTracking()
                .Include(r => r.details.Where(d => d.valid == 1)).AsNoTracking()
                .Include(r => r.rentItems.Where(i => i.valid == 1))
                    .ThenInclude(i => i.logs.OrderByDescending(o => o.id))
                        .ThenInclude(l => l.staff)
                .Include(r => r.pricePresets)
                .Include(r => r.rentItems.Where(i => i.valid == 1)).ThenInclude(r => r.category)
                .Include(r => r.guaranties.Where(g => g.valid == 1 && g.biz_type.Trim().Equals("租赁"))).ThenInclude(g => g.guarantyPayments).ThenInclude(g => g.payment)
                .Where(r => r.valid == 1 && r.appending != null && r.append_commit_time == null).AsNoTracking().ToListAsync();
            if (order.appendingRentals != null && order.appendingRentals.Count > 0)
            {
                Shop shop = await _db.shop.Where(s => s.name == order.shop).AsNoTracking().FirstOrDefaultAsync();
                for (int i = 0; order.appendingRentals != null && i < order.appendingRentals.Count; i++)
                {
                    Rental appendingRental = order.appendingRentals[i];
                    if (appendingRental.category_id == null)
                    {
                        appendingRental.priceList = await _db.rentPrice
                            .Where(p => p.shop_id == shop.id && p.valid == 1 && p.package_id == appendingRental.package_id)
                            .AsNoTracking().ToListAsync();
                    }
                    else
                    {
                        appendingRental.priceList = await _db.rentPrice
                            .Where(p => p.shop_id == shop.id && p.valid == 1 && p.category_id == appendingRental.category_id)
                            .AsNoTracking().ToListAsync();
                    }
                }
            }
            order.skipasses = await _db.order.Entry(order).Collection(o => o.skipasses).Query()
                .Where(s => s.valid == 1).Include(s => s.skiPassProduct).ThenInclude(p => p.dailyPrice).AsNoTracking().ToListAsync();
            order.discounts = await _db.order.Entry(order).Collection(o => o.discounts).Query().Where(d => d.valid == 1).ToListAsync();
            await _db.order.Entry(order).Reference(o => o.staff).LoadAsync();
            await _db.order.Entry(order).Reference(o => o.member).LoadAsync();
            if (order.member_id != null && order.member != null)
            {
                await _db.member.Entry(order.member).Collection(m => m.memberSocialAccounts).LoadAsync();
            }
            order.payments = await _db.order.Entry(order).Collection(o => o.payments).Query().Where(p => p.valid == 1)
                .Include(p => p.member).ThenInclude(m => m.memberSocialAccounts)
                .Include(p => p.staff)
                .Include(p => p.refunds).ThenInclude(r => r.member)
                .AsNoTracking().ToListAsync();
            return order;
        }
        [NonAction]
        public async Task<List<SnowmeetApi.Models.Order>> GetRetailOrders(int? orderId, DateTime? startDate = null, DateTime? endDate = null,
            string? shop = null, string? mi7Num = null, string? cell = null, string? mi7OrderId = null)
        {
            startDate = startDate == null ? DateTime.MinValue : startDate;
            endDate = endDate == null ? DateTime.MaxValue : endDate;
            List<SnowmeetApi.Models.Order> orderList = await _db.order
                .Include(o => o.retails)
                .Include(o => o.payments).ThenInclude(p => p.staff)
                .Include(o => o.payments).ThenInclude(o => o.refunds)
                .Include(o => o.discounts)
                .Include(o => o.staff)
                .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                .Where(o => o.valid == 1 && o.type == "零售"
                    && o.biz_date.Date >= ((DateTime)startDate).Date && o.biz_date.Date <= ((DateTime)endDate).Date
                    && (shop == null || o.shop.Equals(shop.Trim()))
                    //&& (status == null || o.paymentStatus.Equals(status.Trim()))
                    && (mi7Num == null || (mi7Num.Trim().Equals("已填") && !o.retails.Any(r => r.mi7_code == null)) || (mi7Num.Trim().Equals("未填") && o.retails.Any(r => r.mi7_code == null)))
                    && (cell == null || (cell.Length >= 4 && o.cell.EndsWith(cell.Trim())) || o.member.memberSocialAccounts.Any(m => cell.Length >= 4 && m.type.Trim().Equals("cell") && m.num.EndsWith(cell)))
                    && (orderId == null || o.id == orderId)
                    && (mi7OrderId == null || o.retails.Any(r => r.mi7_code.IndexOf(mi7OrderId.Trim()) >= 0))
                ).OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
            return orderList;
        }
        [NonAction]
        public async Task<List<SnowmeetApi.Models.Order>> GetCommonOrders(int? orderId, string? shop, int? memberId,
            int? staffId, string? type, DateTime? startDate, DateTime? endDate, string? payOption = null,
            bool? isTest = null, bool? isEntertain = null, bool? isPackage = null, bool? isOnCredit = null,
            bool? haveDiscount = null, string? status = null, DateTime? closeStartDate = null, DateTime? closeEndDate = null,
            bool? haveWarranty = null, string? retailType = null, string? keyword = null)
        {
            startDate = startDate == null ? DateTime.MinValue : startDate;
            endDate = endDate == null ? DateTime.MaxValue : endDate;
            List<SnowmeetApi.Models.Order> orderList = new List<Models.Order>();
            switch (type)
            {
                case "租赁":
                    orderList = await _db.order.Where(o => (o.biz_date.Date >= ((DateTime)startDate).Date && o.biz_date.Date <= ((DateTime)endDate).Date)
                        && (memberId == null || o.member_id == memberId) && (staffId == null || o.staff_id == staffId)
                        && (payOption == null || o.pay_option.Trim().Equals(payOption.Trim()))
                        && (shop == null || o.shop.Trim().Equals(shop.Trim())) && (type == null || o.type.Trim().Equals(type.Trim()))
                        && o.valid == 1 && (orderId == null || o.id == orderId)
                        && (closeStartDate == null || (o.close_date != null && ((DateTime)o.close_date).Date >= ((DateTime)closeStartDate).Date))
                        && (closeEndDate == null || (o.close_date != null && ((DateTime)o.close_date).Date <= ((DateTime)closeEndDate).Date)))
                    .Include(o => o.rentals.Where(r => r.valid == 1 && (r.appending == null || (r.appending == false && r.append_commit_time != null)))).ThenInclude(r => r.details.Where(d => d.valid == 1)).ThenInclude(d => d.discounts.Where(d => d.valid == 1 && d.sub_biz_type == "日租金"))
                    .Include(o => o.rentals.Where(r => r.valid == 1 && (r.appending == null || (r.appending == false && r.append_commit_time != null)))).ThenInclude(r => r.discounts.Where(d => d.valid == 1 && d.biz_type == "租赁"))
                    .Include(o => o.rentals.Where(r => r.valid == 1 && (r.appending == null || (r.appending == false && r.append_commit_time != null)))).ThenInclude(r => r.rentItems.Where(r => r.valid == 1))
                    .Include(o => o.payments).ThenInclude(p => p.staff)
                    .Include(o => o.payments).ThenInclude(p => p.refunds)
                    .Include(o => o.refunds)
                    .Include(o => o.discounts.Where(d => d.valid == 1))
                    .Include(o => o.guarantys.Where(g => g.valid == 1)).ThenInclude(g => g.guarantyPayments)//.ThenInclude(g => g.payment)
                    .Include(o => o.staff)
                    .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                    .Include(o => o.orderShares.Where(s => s.valid)).ThenInclude(o => o.paymentShares)
                    .OrderByDescending(o => o.id).AsSplitQuery().AsNoTracking().ToListAsync();
                    break;
                case "零售":
                    orderList = await _db.order.Where(o => (o.biz_date.Date >= ((DateTime)startDate).Date && o.biz_date.Date <= ((DateTime)endDate).Date)
                        && (memberId == null || o.member_id == memberId) && (staffId == null || o.staff_id == staffId)
                        && (payOption == null || o.pay_option.Trim().Equals(payOption.Trim()))
                        && (shop == null || o.shop.Trim().Equals(shop.Trim())) && (type == null || o.type.Trim().Equals(type.Trim()))
                        && o.valid == 1 && (orderId == null || o.id == orderId))
                    .Include(o => o.retails.Where(r => r.valid == 1))
                    .Include(o => o.payments).ThenInclude(p => p.staff)
                    .Include(o => o.payments).ThenInclude(p => p.refunds)
                    .Include(o => o.refunds)
                    .Include(o => o.discounts.Where(d => d.valid == 1))
                    .Include(o => o.guarantys.Where(g => g.valid == 1)).ThenInclude(g => g.guarantyPayments)//.ThenInclude(g => g.payment)
                    .Include(o => o.staff)
                    .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                    .Where(o => ((o.retails.Any(r => r.retail_type == retailType) || retailType == null)))
                    .OrderByDescending(o => o.id).AsSplitQuery().AsNoTracking().ToListAsync();
                    //orderList = orderList.Where(o =>  ((o.retails.Any(r => r.retail_type == retailType) || retailType == null) )).ToList();
                    break;
                case "养护":
                    orderList = await _db.order.Where(o => (o.biz_date.Date >= ((DateTime)startDate).Date && o.biz_date.Date <= ((DateTime)endDate).Date)
                            && (memberId == null || o.member_id == memberId) && (staffId == null || o.staff_id == staffId)
                            && (payOption == null || o.pay_option.Trim().Equals(payOption.Trim()))
                            && (shop == null || o.shop.Trim().Equals(shop.Trim())) && (type == null || o.type.Trim().Equals(type.Trim()))
                            && o.valid == 1 && (orderId == null || o.id == orderId))
                        .Include(o => o.cares.Where(c => c.valid == 1)).ThenInclude(c => c.tasks.Where(t => t.valid == 1).OrderBy(t => t.id))
                        .Include(o => o.cares.Where(c => c.valid == 1)).ThenInclude(c => c.careImages).ThenInclude(i => i.image)
                        .Include(o => o.payments).ThenInclude(p => p.staff)
                        .Include(o => o.payments).ThenInclude(p => p.refunds)
                        .Include(o => o.refunds)
                        .Include(o => o.discounts.Where(d => d.valid == 1))
                        .Include(o => o.staff)
                        .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                        .OrderByDescending(o => o.id).AsSplitQuery().AsNoTracking().ToListAsync();
                    break;
                default:
                    orderList = await _db.order.Where(o => (o.biz_date.Date >= ((DateTime)startDate).Date && o.biz_date.Date <= ((DateTime)endDate).Date)
                            && (memberId == null || o.member_id == memberId) && (staffId == null || o.staff_id == staffId)
                            && (payOption == null || o.pay_option.Trim().Equals(payOption.Trim()))
                            && (shop == null || o.shop.Trim().Equals(shop.Trim())) && (type == null || o.type.Trim().Equals(type.Trim()))
                            && o.valid == 1 && (orderId == null || o.id == orderId))
                        .Include(o => o.fdOrders.Where(f => f.valid == 1)).ThenInclude(f => f.product).ThenInclude(p => p.category)
                        .Include(o => o.retails.Where(r => r.valid == 1))
                        .Include(o => o.cares.Where(c => c.valid == 1)).ThenInclude(c => c.tasks.Where(t => t.valid == 1).OrderBy(t => t.id))
                        .Include(o => o.rentals.Where(r => r.valid == 1)).ThenInclude(r => r.details.Where(d => d.valid == 1))
                        .Include(o => o.rentals.Where(r => r.valid == 1)).ThenInclude(r => r.rentItems.Where(r => r.valid == 1))
                        .Include(o => o.payments).ThenInclude(p => p.staff)
                        .Include(o => o.payments).ThenInclude(p => p.refunds)
                        .Include(o => o.refunds)
                        .Include(o => o.discounts.Where(d => d.valid == 1))
                        .Include(o => o.guarantys.Where(g => g.valid == 1)).ThenInclude(g => g.guarantyPayments)//.ThenInclude(g => g.payment)
                        .Include(o => o.staff)
                        .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                        .OrderByDescending(o => o.id).AsSplitQuery().AsNoTracking().ToListAsync();

                    break;
            }

            if (isTest != null)
            {
                orderList = orderList.Where(o => o.is_test == ((bool)isTest ? 1 : 0)).ToList();
            }
            if (isEntertain != null)
            {
                orderList = orderList.Where(o => o.haveEntertain == isEntertain).ToList();
            }
            if (isPackage != null)
            {
                orderList = orderList.Where(o => o.is_package == ((bool)isPackage ? 1 : 0)).ToList();
            }
            if (isOnCredit != null)
            {
                orderList = orderList.Where(o => o.haveOnCredit == isOnCredit).ToList();
            }
            if (haveDiscount != null)
            {
                orderList = orderList.Where(o => o.haveDiscount == haveDiscount).ToList();
            }
            if (status != null)
            {
                orderList = orderList.Where(o => o.orderStatus.Trim().Equals(status)).ToList();
            }
            if (haveWarranty != null)
            {
                orderList = orderList.Where(o => o.haveWarranty == haveWarranty).ToList();
            }
            if (keyword != null)
            {
                switch (type)
                {
                    case "租赁":
                        orderList = orderList.Where(o => (o.memo != null && o.memo.IndexOf(keyword) >= 0)
                            || o.rentals.Any(r => ((r.memo != null && r.memo.IndexOf(keyword) >= 0)
                                || r.rentItems.Any(i => i.memo != null && i.memo.IndexOf(keyword) >= 0 && i.valid == 1)
                                || r.details.Any(d => d.memo != null && d.memo.IndexOf(keyword) >= 0 && d.valid == 1)
                            )
                            )).ToList();
                        break;
                    case "养护":
                        orderList = orderList.Where(o => ((o.memo != null && o.memo.IndexOf(keyword) >= 0)
                            || o.cares.Any(c => (c.valid == 1 && c.memo != null && c.memo.IndexOf(keyword) >= 0)
                            || c.tasks.Any(t => t.valid == 1 && t.memo != null && t.memo.IndexOf(keyword) >= 0))
                        )).ToList();
                        break;
                    default:
                        break;
                }
            }
            return orderList;
        }
        [NonAction]
        public async Task<SnowmeetApi.Models.Order> UpdateOrder(SnowmeetApi.Models.Order order, int? memberId, int? staffId, string scene)
        {
            SnowmeetApi.Models.Order oriOrder = await GetOrder(order.id);
            if (order.code == null && order.valid == 1)
            {
                await GenerateOrderCode(order);
            }
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Models.Order>(oriOrder, order, memberId, staffId, scene);
            foreach (CoreDataModLog log in logs)
            {
                await _db.coreDataModLog.AddAsync(log);
            }
            order.update_date = DateTime.Now;
            _db.order.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            _db.order.Entry(order).State = EntityState.Detached;
            return order;
        }
        [NonAction]
        public async Task<FdOrder> UpdateFdOrder(FdOrder fdOrder, int? memberId, int? staffId, string scene)
        {
            FdOrder oriFdOrder = await _db.fdOrder.Where(f => f.id == fdOrder.id).AsNoTracking().FirstOrDefaultAsync();
            if (oriFdOrder == null)
            {
                return null;
            }
            else
            {
                if (oriFdOrder.valid == 1 && fdOrder.valid == 0)
                {
                    List<Discount> discounts = await _db.discount.Where(d => d.biz_id == fdOrder.id && d.biz_type.Trim().Equals("餐饮")).ToListAsync();
                    for (int i = 0; i < discounts.Count; i++)
                    {
                        Discount discount = discounts[i];
                        discount.valid = 0;
                        discount.update_date = DateTime.Now;
                        _db.discount.Entry(discount).State = EntityState.Modified;
                    }
                }
            }
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<FdOrder>(oriFdOrder, fdOrder, memberId, staffId, scene);
            foreach (CoreDataModLog log in logs)
            {
                await _db.coreDataModLog.AddAsync(log);
            }
            fdOrder.update_date = DateTime.Now;
            _db.fdOrder.Entry(fdOrder).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return fdOrder;
        }
        [NonAction]
        public async Task<Retail> GetRetailDetail(int detailId)
        {
            return await _db.retail.FindAsync(detailId);
        }
        [NonAction]
        public async Task<Retail> UpdateRetail(Retail retail, int? memberId, int? staffId, string scene)
        {
            Retail oriRetail = await _db.retail.FindAsync(retail.id);
            List<CoreDataModLog> logs = Retail.GetUpdateDifferenceLog(oriRetail, retail, memberId, staffId, scene);
            foreach (CoreDataModLog log in logs)
            {
                await _db.coreDataModLog.AddAsync(log);
            }
            oriRetail.update_date = DateTime.Now;
            _db.retail.Entry(oriRetail).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return oriRetail;
        }
        [NonAction]
        public async Task GenerateOrderCode(SnowmeetApi.Models.Order order)
        {
            ApiResult<List<Models.Shop>> shopResult = (ApiResult<List<Models.Shop>>)((OkObjectResult)(await GetShops()).Result).Value;
            string shopCode = "WZ";
            for (int i = 0; i < shopResult.data.Count; i++)
            {
                if (shopResult.data[i].name.Trim().Equals(order.shop.Trim()))
                {
                    shopCode = shopResult.data[i].code.Trim();
                    break;
                }
            }
            string bizCode = "";
            switch (order.type.Trim())
            {
                case "零售":
                    bizCode = "LS";
                    break;
                case "养护":
                    bizCode = "YH";
                    break;
                case "雪票":
                    bizCode = "XP";
                    break;
                case "租赁":
                    bizCode = "ZL";
                    break;
                case "餐饮":
                    bizCode = "CY";
                    break;
                default:
                    bizCode = "WZ";
                    break;
            }
            string dateStr = order.create_date.ToString("yyMMdd");
            string orderCode = shopCode + "_" + bizCode + "_" + dateStr + "_";
            List<SnowmeetApi.Models.Order> orders = await _db.order.Where(o => o.code.StartsWith(orderCode))
                .AsNoTracking().ToListAsync();
            orderCode += (orders.Count + 1).ToString().PadLeft(5, '0');
            order.code = orderCode;
        }
        [NonAction]
        public async Task<bool> CheckRetailMi7CodeUnique(SnowmeetApi.Models.Order order)
        {
            bool valid = true;
            for (int i = 0; order.retails != null && i < order.retails.Count; i++)
            {
                Retail retail = order.retails[i];
                if (retail.mi7_code != null)
                {
                    List<Retail> retails = await _db.retail.Include(r => r.order)
                        .Where(r => r.mi7_code.Equals(retail.mi7_code.Trim()) && r.valid == 1)
                        .AsNoTracking().ToListAsync();
                    for (int j = 0; j < retails.Count; j++)
                    {

                        if ((retails[j].order != null && retails[j].order.valid == 1) || retails[j].order == null)
                        {
                            valid = false;
                            break;
                        }
                    }
                }
            }
            return valid;
        }
        [NonAction]
        public async Task CancelOrder(SnowmeetApi.Models.Order order, int? staffId, int? memberId, string scene)
        {
            order.valid = 0;
            order.update_date = DateTime.Now;
            _db.order.Entry(order).State = EntityState.Modified;
            for (int i = 0; order.retails != null && i < order.retails.Count; i++)
            {
                Retail retail = order.retails[i];
                retail.valid = 0;
                retail.update_date = DateTime.Now;
                _db.retail.Entry(retail).State = EntityState.Modified;
            }
            for (int i = 0; order.payments != null && i < order.payments.Count; i++)
            {
                OrderPayment payment = order.payments[i];
                payment.valid = 0;
                payment.update_date = DateTime.Now;
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
            }
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                table_name = "order",
                field_name = null,
                key_value = order.id,
                scene = scene,
                member_id = memberId,
                staff_id = staffId,
                trace_id = (DateTime.Now - DateTime.Parse("1970-1-1")).Ticks,
                is_manual = 1,
                manual_memo = "删除订单",
                create_date = DateTime.Now
            };
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Models.Shop>>>> GetShops()
        {
            List<Models.Shop> shopList = await _db.shop.OrderBy(s => s.sort).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<Models.Shop>>()
            {
                data = shopList,
                code = 0,
                message = ""
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<SnowmeetApi.Models.Order>>>> GetRetailOrders(string staffSessionKey, int? orderId,
            DateTime? startDate = null, DateTime? endDate = null, string? shop = null, string? status = null, string? mi7Num = null,
            string? cell = null, string? mi7OrderId = null, bool onlyMine = false, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(100, staffSessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
            }
            Staff staff = await _staffHelper.GetStaffBySessionKey(staffSessionKey, sessionType);
            if (mi7Num != null && mi7Num.Trim().Equals("紧急开单"))
            {
                mi7Num = "未填";
            }
            List<SnowmeetApi.Models.Order> orders = await GetRetailOrders(orderId, startDate, endDate, shop, mi7Num, cell, mi7OrderId);
            if (onlyMine)
            {
                orders = orders.Where(o => o.staff_id == staff.id).ToList();
            }
            if (status != null)
            {
                if (status.Trim().Equals("支付完成"))
                {
                    orders = orders.Where(o => o.paymentStatus.Trim().Equals(status)
                        || o.paymentStatus.Trim().Equals("无需支付")).ToList();
                }
                else
                {
                    orders = orders.Where(o => o.paymentStatus.Trim().Equals(status)).ToList();
                }
            }
            SnowmeetApi.Models.Order.RendOrderList(orders);
            return new ApiResult<List<SnowmeetApi.Models.Order>>()
            {
                code = 0,
                message = "",
                data = orders
            };
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<SnowmeetApi.Models.Order>>> GetRetailOrder(int orderId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(0, sessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
            }
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            MemberController _memberHelper = new MemberController(_db, _config);
            Models.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            List<SnowmeetApi.Models.Order> orderList = await GetRetailOrders(orderId, null, null, null, null, null, null);
            SnowmeetApi.Models.Order? order = (orderList != null && orderList.Count > 0) ? orderList[0] : null;
            if (order == null)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "订单不存在。",
                    data = null
                });
            }
            else
            {
                if (staff != null || order.member_id == member.id)
                {
                    SnowmeetApi.Models.Order.RendOrder(order);
                    return Ok(new ApiResult<object?>()
                    {
                        code = 0,
                        message = "",
                        data = order
                    });
                }
                else
                {
                    return Ok(new ApiResult<object?>()
                    {
                        code = 1,
                        message = "没有权限",
                        data = null
                    });
                }
            }
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<SnowmeetApi.Models.Order>>> UpdateOrderByStaff([FromBody] SnowmeetApi.Models.Order order,
            [FromQuery] string scene, [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(100, sessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
            }
            scene = Util.UrlDecode(scene);
            switch (scene)
            {
                case "餐厅下单":
                    if (order.dealed == 1 && order.valid == 1 && order.pay_flow_status == null)
                    {
                        CoreDataModLog log = new CoreDataModLog()
                        {
                            table_name = "Order",
                            field_name = "OrderState",
                            key_value = order.id,
                            prev_value = null,
                            current_value = Models.Order.OrderStatus.已下单.ToString(),
                            staff_id = null,
                            is_manual = 1,
                            scene = scene,
                            create_date = DateTime.Now
                        };
                        await _db.coreDataModLog.AddAsync(log);
                        await _db.SaveChangesAsync();
                    }
                    break;
                default:
                    break;
            }
            order = await UpdateOrder(order, null, staff.id, scene);
            return Ok(new ApiResult<SnowmeetApi.Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<FdOrder>>> UpdateFdOrderByStaff([FromBody] FdOrder fdOrder,
            [FromQuery] string scene, [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(100, sessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
            }
            scene = Util.UrlDecode(scene);
            fdOrder = await UpdateFdOrder(fdOrder, null, staff.id, scene);
            return Ok(new ApiResult<SnowmeetApi.Models.FdOrder>()
            {
                code = 0,
                message = "",
                data = fdOrder
            });
        }
        [HttpGet("{detailId}")]
        public async Task<ActionResult<ApiResult<Retail?>>> GetRetailDetail(int detailId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(0, sessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
            }
            Retail retail = await GetRetailDetail(detailId);
            LogController _logHelper = new LogController(_db);

            if (retail == null)
            {
                return Ok(new ApiResult<Retail?>()
                {
                    code = 1,
                    message = "记录不存在",
                    data = null
                });
            }
            else
            {
                retail.logs = await _logHelper.GetSimpleLogs("retail", retail.id);
                return Ok(new ApiResult<Retail?>()
                {
                    code = 0,
                    message = "",
                    data = retail
                });
            }
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Retail>>> UpdateRetail([FromBody] Retail retail, string scene,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(100, sessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
            }
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            scene = Util.UrlDecode(scene);
            retail = await UpdateRetail(retail, null, staff.id, scene);
            return Ok(new ApiResult<SnowmeetApi.Models.Retail>()
            {
                code = 0,
                message = "",
                data = retail
            });
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<SnowmeetApi.Models.Order?>>> PlaceOrder([FromBody] SnowmeetApi.Models.Order order,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            MemberController _memberHelper = new MemberController(_db, _config);
            Models.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (staff != null && staff.title_level >= 100)
            {
                order.staff_id = staff.id;
            }
            else if (member != null && order.member_id == null)
            {
                order.member_id = member.id;
            }
            order.recepting = 0;
            order.valid = 1;
            order.biz_date = DateTime.Now;
            order.create_date = DateTime.Now;
            if (order.shop == null || order.shop.Trim().Equals(""))
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "店铺不能为空",
                    data = null
                });
            }

            if (order.sub_type != "雪季初临时订单")
            {
                switch (order.type)
                {
                    case "零售":
                        if (!await CheckRetailMi7CodeUnique(order))
                        {
                            return new ApiResult<SnowmeetApi.Models.Order?>()
                            {
                                code = 1,
                                message = "七色米订单号重复",
                                data = null
                            };
                        }
                        for (int i = 0; i < order.retails.Count; i++)
                        {
                            Retail retail = order.retails[i];
                            retail.valid = 1;
                            if (retail.retail_type == null)
                            {
                                retail.retail_type = "零售";
                            }
                        }
                        break;
                    case "养护":
                        CareController _careHelper = new CareController(_db, _config, _http);
                        double total = 0;
                        for (int i = 0; i < order.cares.Count; i++)
                        {
                            order.biz_date = DateTime.Now;
                            Care care = order.cares[i];
                            Product product = await _careHelper.GetProduct(order.shop, care);

                            if (product == null || care.warranty || care.entertain)
                            {
                                care.common_charge = 0;
                            }
                            else
                            {
                                care.common_charge = product.sale_price;
                                if (care.ticket_code != null && care.ticket_code.Trim() != "")
                                {
                                    Ticket ticket = await _db.ticket.Where(t => t.code == care.ticket_code && t.valid == 1 && t.used == 0)
                                        .Include(t => t.template).ThenInclude(p => p.productTicketTemplates).ThenInclude(p => p.product)
                                        .AsNoTracking().FirstOrDefaultAsync();
                                    if (ticket != null)
                                    {
                                        ProductTicketTemplate productTicketTemplate = ticket.template.productTicketTemplates
                                            .Where(p => p.product_id == product.id).FirstOrDefault();
                                        if (productTicketTemplate != null)
                                        {
                                            if (productTicketTemplate.fixed_price != null)
                                            {
                                                care.common_charge = (double)productTicketTemplate.fixed_price;
                                            }
                                            //需要减免直接else if
                                        }
                                    }

                                }
                            }
                            total += (care.common_charge + care.repair_charge - care.discount - care.ticket_discount);
                        }

                        order.total_amount = total;
                        order.paying_amount = total;
                        if (total == 0)
                        {
                            order.dealed = 1;
                        }
                        for (int i = 0; order.cares != null && i < order.cares.Count; i++)
                        {
                            Care care = order.cares[i];
                            if (care.urgent == 1)
                            {
                                care.member_pick_date = DateTime.Now.Date;
                            }
                            else
                            {
                                care.member_pick_date = DateTime.Now.Date.AddDays(1);
                            }
                        }
                        break;

                    default:
                        break;
                }
            }

            /*
            if (_http.HttpContext.Request.Host.Value != null
                && _http.HttpContext.Request.Host.Value.Equals("mini.snowmeet.top"))
            {
                order.is_test = 1;
            }
            else
            {
                order.is_test = 0;
            }
            */

            if (order.staff_id == 28 || order.staff_id == 31 || order.staff_id == 34)
            {
                order.is_test = 1;
            }
            else
            {
                order.is_test = 0;
            }

            await GenerateOrderCode(order);
            await _db.order.AddAsync(order);


            CoreDataModLog log = new CoreDataModLog()
            {
                table_name = "Order",
                field_name = "OrderState",
                key_value = order.id,
                prev_value = null,
                current_value = Models.Order.OrderStatus.待生成.ToString(),
                staff_id = staff.id,
                is_manual = 1,
                scene = "开单",
                create_date = DateTime.Now
            };
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            for (int i = 0; order.cares != null && i < order.cares.Count; i++)
            {
                if (order.cares[i].discount > 0)
                {
                    Discount discount = new Discount()
                    {
                        id = 0,
                        order_id = order.id,
                        biz_type = "养护",
                        biz_id = order.cares[i].id,
                        amount = order.cares[i].discount,
                        valid = 1,
                        staff_id = order.staff_id,
                        create_date = DateTime.Now
                    };
                    await _db.discount.AddAsync(discount);
                }
                if (order.cares[i].ticket_discount > 0)
                {
                    Discount discount = new Discount()
                    {
                        id = 0,
                        order_id = order.id,
                        biz_type = "养护",
                        biz_id = order.cares[i].id,
                        amount = order.cares[i].discount,
                        valid = 1,
                        staff_id = order.staff_id,
                        ticket_code = order.cares[i].ticket_code,
                        create_date = DateTime.Now
                    };
                    await _db.discount.AddAsync(discount);
                }

            }
            await _db.SaveChangesAsync();
            if (order.paying_amount == 0 && order.type == "养护")
            {
                _db.order.Entry(order).State = EntityState.Detached;
                for (int i = 0; i < order.cares.Count; i++)
                {
                    _db.care.Entry(order.cares[i]).State = EntityState.Detached;
                }
                CareController _careHelper = new CareController(_db, _config, _http);
                await _careHelper.EffectCareOrder(order.id);
                order = await GetOrder(order.id);
            }

            return Ok(new ApiResult<SnowmeetApi.Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<SnowmeetApi.Models.Order?>>> CancelOrder(int orderId,
            string scene, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            scene = Util.UrlDecode(scene);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff.title_level < 100)
            {
                return Ok(new ApiResult<SnowmeetApi.Models.Order?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            SnowmeetApi.Models.Order order = await _db.order.FindAsync(orderId);
            order.retails = await _db.order.Entry(order).Collection(o => o.retails).Query().ToListAsync();
            order.payments = await _db.order.Entry(order).Collection(o => o.payments).Query().ToListAsync();
            if (!order.canDelete)
            {
                return Ok(new ApiResult<SnowmeetApi.Models.Order?>()
                {
                    code = 1,
                    message = "订单不支持取消",
                    data = null
                });
            }
            await CancelOrder(order, staff.id, null, scene);
            SnowmeetApi.Models.Order.RendOrder(order);
            return Ok(new ApiResult<SnowmeetApi.Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<SnowmeetApi.Models.Order>>>> GetOrdersByStaff(int? orderId,
            string? shop, string? type, string? subType, DateTime? startDate, DateTime? endDate, string sessionKey,
            string? payOption, string sessionType = "wechat_mini_openid", bool? isTest = null, bool? isEntertain = null,
            bool? isPackage = null, bool? isOnCredit = null, bool? haveDiscount = null, string? status = null,
            string? cell = null, bool? haveWarranty = null, string? retailType = null, string? keyword = null)
        {
            shop = shop == null ? null : Util.UrlDecode(shop);
            type = type == null ? null : Util.UrlDecode(type);
            subType = subType == null ? null : Util.UrlDecode(subType);
            sessionKey = Util.UrlDecode(sessionKey);
            payOption = payOption == null ? null : Util.UrlDecode(payOption);
            status = status == null ? null : Util.UrlDecode(status);
            cell = cell == null ? null : Util.UrlDecode(cell);
            retailType = retailType == null ? null : Util.UrlDecode(retailType);
            keyword = keyword == null ? null : Util.UrlDecode(keyword);
            if (keyword != null && keyword.Trim() == "")
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "关键词不能为空",
                    data = null
                });
            }
            //startDate = DateTime.Parse("2025-10-27");
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "不是管理员",
                    data = null
                });
            }
            if (type == "养护" && ((DateTime)startDate).Date < DateTime.Parse("2025-11-01"))
            {
                //startDate = DateTime.Parse("2025-11-01");
            }
            List<SnowmeetApi.Models.Order> orders = await GetCommonOrders(orderId, shop, null, null, type, startDate, endDate, payOption,
            isTest, isEntertain, isPackage, isOnCredit, haveDiscount, status, null, null, haveWarranty, retailType, keyword);
            List<SnowmeetApi.Models.Order> newOrders = new List<Models.Order>();
            if (cell != null)
            {
                newOrders = orders.Where(o => o.customerCell.EndsWith(cell)).ToList();
            }
            else
            {
                newOrders = orders;
            }
            SnowmeetApi.Models.Order.RendOrderList(newOrders);
            return Ok(new ApiResult<List<SnowmeetApi.Models.Order>>()
            {
                code = 0,
                message = "",
                data = newOrders
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order>>> GetOrderByCustomer(int orderId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            SnowmeetApi.Models.Order order = await GetOrder(orderId);
            if (order.member_id != null && order.member_id != member.id)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "该订单不属于当前顾客。",
                    data = null
                });
            }
            if (order.pay_flow_status != null && order.pay_flow_status.Trim().Equals("已生成"))
            {
                order.pay_flow_status = Models.Order.PayFlowStatus.待支付.ToString();
                await UpdateOrder(order, member.id, null, "顾客微信小程序打开待支付订单");
            }
            return Ok(new ApiResult<Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<SnowmeetApi.Models.Order?>>> GetOrderByStaff(int orderId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<SnowmeetApi.Models.Order>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            SnowmeetApi.Models.Order order = await GetOrder(orderId);
            if (order == null)
            {
                return Ok(new ApiResult<SnowmeetApi.Models.Order>()
                {
                    code = 1,
                    message = "没有找到",
                    data = null
                });
            }
            else
            {
                return Ok(new ApiResult<SnowmeetApi.Models.Order>()
                {
                    code = 0,
                    message = "",
                    data = order
                });
            }
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<List<Discount>>>> SetDiscount(int orderId, int? bizId, string? bizType,
            double discountAmount, string? ticketCode, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<SnowmeetApi.Models.Order>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            List<Discount>? discounts = await SetDiscount(orderId, bizId, bizType, discountAmount, ticketCode, staff == null ? null : staff.id, null);
            if (discounts == null)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "减免设置失败",
                    data = null
                });
            }
            else
            {
                return Ok(new ApiResult<List<Discount>>()
                {
                    code = 0,
                    message = "",
                    data = discounts
                });
            }
        }
        [NonAction]
        public async Task<List<Discount>> SetDiscount(int? orderId, int? bizId, string? bizType, double discountAmount,
            string? ticketCode, int? staffId, int? memberId)
        {
            List<Discount> discounts = await _db.discount
                .Where(d => d.order_id == orderId && d.biz_id == bizId && d.biz_type == bizType && d.valid == 1).ToListAsync();
            if (discounts.Count == 0)
            {
                if (discountAmount > 0)
                {
                    Discount discount = new Discount()
                    {
                        id = 0,
                        order_id = orderId,
                        biz_id = bizId,
                        biz_type = bizType,
                        amount = discountAmount,
                        ticket_code = ticketCode,
                        staff_id = staffId,
                        member_id = memberId,
                        valid = 1,
                        create_date = DateTime.Now
                    };
                    await _db.discount.AddAsync(discount);
                }
            }
            else
            {
                if (ticketCode != null)
                {
                    List<Discount> ticketDiscounts = discounts.Where(d => d.ticket_code != null).ToList();
                    if (ticketDiscounts.Count == 0)
                    {
                        Discount discount = new Discount()
                        {
                            id = 0,
                            order_id = orderId,
                            biz_id = bizId,
                            biz_type = bizType,
                            amount = discountAmount,
                            ticket_code = ticketCode,
                            staff_id = staffId,
                            member_id = memberId,
                            valid = 1,
                            create_date = DateTime.Now
                        };
                        await _db.discount.AddAsync(discount);
                    }
                    else
                    {
                        List<Discount> subList = ticketDiscounts.Where(d => d.ticket_code.Trim().Equals(ticketCode.Trim())).ToList();
                        if (subList.Count > 0)
                        {
                            Discount discount = subList[0];
                            string json = JsonConvert.SerializeObject(discount);
                            Discount oriDiscount = JsonConvert.DeserializeObject<Discount>(json);
                            discount.amount = discountAmount;
                            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Discount>(oriDiscount, discount, memberId, staffId, "修改优惠券减免金额");
                            discount.update_date = DateTime.Now;
                            for (int i = 0; i < logs.Count; i++)
                            {
                                await _db.coreDataModLog.AddAsync(logs[i]);
                            }
                            _db.discount.Entry(discount).State = EntityState.Modified;
                        }
                        else
                        {
                            return null;
                        }
                    }
                }
                else
                {
                    List<Discount> subList = discounts.Where(d => d.ticket_code == null).ToList();
                    if (subList.Count > 0)
                    {
                        Discount discount = subList[0];
                        string json = JsonConvert.SerializeObject(discount);
                        Discount oriDiscount = JsonConvert.DeserializeObject<Discount>(json);
                        discount.amount = discountAmount;
                        List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Discount>(oriDiscount, discount, memberId, staffId, "修改普通减免金额");
                        discount.update_date = DateTime.Now;
                        for (int i = 0; i < logs.Count; i++)
                        {
                            await _db.coreDataModLog.AddAsync(logs[i]);
                        }
                        _db.discount.Entry(discount).State = EntityState.Modified;
                    }
                    else
                    {
                        Discount discount = new Discount()
                        {
                            id = 0,
                            order_id = orderId,
                            biz_id = bizId,
                            biz_type = bizType,
                            amount = discountAmount,
                            ticket_code = ticketCode,
                            staff_id = staffId,
                            member_id = memberId,
                            valid = 1,
                            create_date = DateTime.Now
                        };
                        await _db.discount.AddAsync(discount);
                    }
                }
            }
            await _db.SaveChangesAsync();
            return discounts;
        }
        [NonAction]
        public async Task<OrderPayment> GetReadyOrderPayment(Models.Order order, double? amount, string payMethod, int? memberId, string? openId, bool needShare = false)
        {
            if (order == null && order.closed == 1)
            {
                return null;
            }
            double payAmount = 0;
            if (amount == null)
            {
                payAmount = (double)order.paying_amount;
            }
            else
            {
                payAmount = (double)amount;
            }
            List<OrderPayment> allPayments = await _db.orderPayment.Where(o => o.order_id == order.id).ToListAsync();
            OrderPayment lastPayment = allPayments.Where(o => o.valid == 1 && o.pay_method.Trim().Equals(payMethod.Trim())
                && o.status.Equals(OrderPayment.PaymentStatus.待支付.ToString()) && o.open_id == openId && o.member_id == memberId)
                .OrderByDescending(o => o.id).FirstOrDefault();
            OrderPayment payment;
            bool needCreateNew = false;
            if (lastPayment == null)
            {
                needCreateNew = true;
            }
            else if (lastPayment.submit_time == null)
            {
                needCreateNew = true;
            }
            else if ((DateTime.Now - (DateTime)lastPayment.submit_time).Seconds >= 3600)
            {
                needCreateNew = true;
            }
            else if (order.totalCharge != lastPayment.amount)
            {
                needCreateNew = true;
            }
            else
            {
                needCreateNew = false;
            }
            //needCreateNew = true;
            if (needCreateNew)
            {
                if (lastPayment != null)
                {
                    lastPayment.valid = 0;
                    lastPayment.update_date = DateTime.Now;
                    _db.orderPayment.Entry(lastPayment).State = EntityState.Modified;
                }
                payment = new OrderPayment()
                {
                    id = 0,
                    order_id = order.id,
                    pay_method = payMethod.Trim(),
                    amount = payAmount,
                    out_trade_no = order.code + "_ZF_" + (allPayments.Count + 1).ToString().PadLeft(2, '0'),
                    status = OrderPayment.PaymentStatus.待支付.ToString(),
                    member_id = memberId,
                    open_id = openId

                };
                await _db.orderPayment.AddAsync(payment);
                await _db.SaveChangesAsync();
                switch (payment.pay_method)
                {
                    case "支付宝":
                        AliController _aliHelper = new AliController(_db, _config, _http);
                        return await _aliHelper.GetPaymentQrCodeUrl(payment, order);
                    case "微信支付":
                        TenpayController _tenHelper = new TenpayController(_db, _config, _http);
                        return await _tenHelper.TenpayRequest(payment, order, needShare);
                    default:
                        break;
                }
                return null;
            }
            else
            {
                lastPayment.member_id = memberId;
                lastPayment.open_id = openId;
                lastPayment.update_date = DateTime.Now;
                _db.orderPayment.Entry(lastPayment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                return lastPayment;
            }
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<ActionResult<OrderPayment?>>> GetWepayPayment(int orderId, double? amount,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Models.Order order = await GetOrder(orderId);
            string message = "";
            if (order == null)
            {
                message = "无此订单";
            }
            else if (order.closed == 1)
            {
                message = "订单关闭";
            }
            else
            {
                StaffController _staffHelper = new StaffController(_db);
                Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);

                List<OrderPayment> prevPayments = await _db.orderPayment
                    .Where(p => p.valid == 1 && p.status.Trim().Equals(OrderPayment.PaymentStatus.待支付.ToString())
                    && p.order_id == orderId && p.pay_method.Trim().Equals("微信支付")).AsNoTracking().ToListAsync();
                for (int i = 0; i < prevPayments.Count; i++)
                {
                    prevPayments[i].valid = 0;
                    _db.orderPayment.Entry(prevPayments[i]).State = EntityState.Modified;
                }
                TenpayController _tenHelper = new TenpayController(_db, _config, _http);
                int mchId = _tenHelper.GetMchId(order);
                OrderPayment newPayment = new OrderPayment()
                {
                    id = 0,
                    order_id = order.id,
                    amount = amount == null ? (double)order.paying_amount : (double)amount,
                    staff_id = staff.id,
                    pay_method = "微信支付",
                    mch_id = mchId,
                    create_date = DateTime.Now
                };
                await _db.orderPayment.AddAsync(newPayment);
                CoreDataModLog log = new CoreDataModLog()
                {
                    table_name = "Order",
                    field_name = "OrderState",
                    key_value = orderId,
                    prev_value = null,
                    current_value = Models.Order.OrderStatus.待支付.ToString(),
                    staff_id = staff.id,
                    is_manual = 1,
                    scene = "准备微信支付",
                    create_date = DateTime.Now
                };

                //order.current_pay_method = null;
                _db.order.Entry(order).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                return Ok(new ApiResult<OrderPayment?>()
                {
                    code = 0,
                    message = "",
                    data = newPayment
                });
            }
            return Ok(new ApiResult<OrderPayment?>()
            {
                code = 1,
                message = message,
                data = null
            });
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<OrderPayment>>> GetAlipayPaymentQrCode(int orderId, double? amount,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Models.Order order = await GetOrder(orderId);
            string message = "";
            if (order == null)
            {
                message = "无此订单";
            }
            else if (order.closed == 1)
            {
                message = "订单关闭";
            }
            else
            {
                OrderPayment payment = await GetReadyOrderPayment(order, amount, "支付宝", null, null);
                StaffController _staffHelper = new StaffController(_db);
                Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
                payment.staff_id = staff == null ? null : staff.id;
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                if (payment == null)
                {
                    message = "获取二维码失败";
                }
                else if (payment.ali_qr_code == null || payment.ali_qr_code.Trim().Equals(""))
                {
                    message = "支付宝系统故障";
                }
                if (message.Trim().Equals(""))
                {
                    CoreDataModLog log = new CoreDataModLog()
                    {
                        table_name = "Order",
                        field_name = "OrderState",
                        key_value = orderId,
                        prev_value = null,
                        current_value = Models.Order.OrderStatus.待支付.ToString(),
                        staff_id = staff.id,
                        is_manual = 1,
                        scene = "显示支付宝二维码",
                        create_date = DateTime.Now
                    };
                    await _db.coreDataModLog.AddAsync(log);
                    await _db.SaveChangesAsync();
                    return Ok(new ApiResult<OrderPayment>()
                    {
                        code = 0,
                        message = "",
                        data = payment
                    });
                }
            }
            return Ok(new ApiResult<OrderPayment>()
            {
                code = 1,
                message = message,
                data = null
            });
        }
        [HttpGet("{paymentId}")]
        public async Task<ActionResult<ApiResult<OrderPayment?>>> WechatPayByOrderPayment(int paymentId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            string message = "";
            if (member == null || member.wechatMiniOpenId == null)
            {
                message = "未找到用户";
            }
            if (!message.Trim().Equals(""))
            {
                return Ok(new ApiResult<OrderPayment?>()
                {
                    code = 1,
                    message = message,
                    data = null
                });
            }
            OrderPayment payment = await _db.orderPayment.Where(p => p.id == paymentId).AsNoTracking().FirstOrDefaultAsync();
            Models.Order order = await GetOrder(payment.order_id);
            ///////正式上线时去掉注释

            if (order.shop == "万龙体验中心" && order.type == "租赁")
            {
                payment.need_share = 1;
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            else if (order.type != "雪票")
            {
                payment.need_share = 0;
            }

            List<OrderPayment> allPayments = await _db.orderPayment.Where(p => p.order_id == order.id)
                .OrderByDescending(p => p.out_trade_no).AsNoTracking().ToListAsync();
            string? outTradeNo = allPayments[0].out_trade_no;
            if (outTradeNo == null)
            {
                outTradeNo = order.code + "_ZF_" + allPayments.Count.ToString().PadLeft(2, '0');
            }
            else
            {
                string[] outTradeNoArr = outTradeNo.Split('_');
                int outNum = int.Parse(outTradeNoArr[outTradeNoArr.Length - 1].Trim()) + 1;
                outTradeNo = order.code + "_ZF_" + outNum.ToString().PadLeft(2, '0');
            }
            if (payment.member_id == null)
            {
                payment.member_id = member.id;
                payment.open_id = member.wechatMiniOpenId.Trim();
                payment.update_date = DateTime.Now;
                payment.out_trade_no = outTradeNo.Trim();
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            if (payment.member_id != member.id && payment.member_id != null)
            {
                //outTradeNo = payment.out_trade_no;

                CoreDataModLog log = new CoreDataModLog()
                {
                    id = 0,
                    table_name = "order_payment",
                    field_name = "member_id",
                    key_value = payment.id,
                    scene = "支付顾客换人",
                    member_id = member.id,
                    staff_id = null,
                    prev_value = payment.member_id.ToString(),
                    current_value = member.id.ToString(),
                    trace_id = 0,
                    is_manual = 1,
                    manual_memo = ""
                };
                await _db.coreDataModLog.AddAsync(log);
                payment.member_id = member.id;
                payment.open_id = member.wechatMiniOpenId.Trim();
                payment.out_trade_no = outTradeNo.Trim();
                payment.prepay_id = null;
                payment.timestamp = null;
                payment.nonce = null;
                payment.sign = null;
                payment.update_date = DateTime.Now;
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }

            if (payment.prepay_id == null)
            {
                TenpayController _tenHelper = new TenpayController(_db, _config, _http);
                payment = await _tenHelper.TenpayRequest(payment, order, payment.need_share == 1 ? true : false);
            }
            return Ok(new ApiResult<OrderPayment?>()
            {
                code = 0,
                message = "",
                data = payment
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<OrderPayment>>> WechatPay(int orderId, double? amount,
            string sessionKey, string sessionType = "wechat_mini_openid", bool needShare = false)
        {
            string payMethod = "微信支付";
            Models.Order? order = await GetOrder(orderId);
            string message = "";
            if (order.closed == 1)
            {
                message = "订单已关闭";
            }
            double realPayAmount = (amount == null) ? 0 : (double)amount;
            realPayAmount = order.totalCharge;
            if (order.paidAmount > 0)
            {
                message = "订单已经支付过";
            }
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null || member.wechatMiniOpenId == null)
            {
                message = "未找到用户";
            }
            if (!message.Trim().Equals(""))
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = message,
                    data = null
                });
            }
            OrderPayment payment = await GetReadyOrderPayment(order, amount, payMethod, member.id, member.wechatMiniOpenId, needShare);
            order.pay_flow_status = Models.Order.PayFlowStatus.支付中.ToString();
            //order.update_date = DateTime.Now;
            await UpdateOrder(order, member.id, null, "微信支付点击支付按钮");
            return Ok(new ApiResult<OrderPayment>()
            {
                code = 0,
                message = "",
                data = payment
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order?>>> EffectUnpaidOrder(int orderId,
            string? payMethod, bool payLater, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null && staff.title_level < 100)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.Order order = await GetOrder(orderId);
            /*
            if (order.paidAmount >= order.totalCharge)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "订单已支付",
                    data = null
                });
            }
            */
            if (order.dealed != 0)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "无效订单",
                    data = null
                });
            }
            OrderPayment payment;
            if (payLater)
            {
                payment = new OrderPayment()
                {
                    id = 0,
                    order_id = orderId,
                    amount = (double)order.paying_amount,
                    pay_method = null,
                    is_debt = 1,
                    staff_id = staff.id,
                    create_date = DateTime.Now
                };

            }
            else if (payMethod != null && !payMethod.Trim().Equals("微信支付") && !payMethod.Trim().Equals("支付宝"))
            {
                payment = new OrderPayment()
                {
                    id = 0,
                    order_id = orderId,
                    amount = (double)order.paying_amount,
                    pay_method = payMethod,
                    is_debt = 0,
                    status = OrderPayment.PaymentStatus.支付成功.ToString(),
                    staff_id = staff.id,
                    paid_date = DateTime.Now,
                    create_date = DateTime.Now
                };
            }
            else
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "支付方式不支持",
                    data = null
                });
            }
            if (payment.amount > 0)
            {
                await _db.orderPayment.AddAsync(payment);
                await _db.SaveChangesAsync();
                await _db.order.Entry(order).Collection(o => o.payments).LoadAsync();
                CoreDataModLog log = new CoreDataModLog()
                {
                    table_name = "Order",
                    field_name = "OrderState",
                    key_value = orderId,
                    prev_value = null,
                    current_value = Models.Order.OrderStatus.支付成功.ToString(),
                    staff_id = staff.id,
                    is_manual = 1,
                    scene = "手工收款",
                    create_date = DateTime.Now
                };
                await _db.coreDataModLog.AddAsync(log);
                await _db.SaveChangesAsync();
                await UpdateOrder(order, null, staff.id, "手动确认支付");
            }

            await DealSuccessPaidOrder(order.id, payment.id);
            return Ok(new ApiResult<Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpGet]
        public async Task DealSuccessPaidOrder(int orderId, int? paymentId = null)
        {
            Models.Order order = await _db.order.Where(o => o.id == orderId)
                .AsNoTracking().FirstOrDefaultAsync();
            order.dealed = 1;
            order.pay_flow_status = Models.Order.PayFlowStatus.已支付.ToString();
            order.paying_amount = null;
            await UpdateOrder(order, null, null, "支付成功");
            CoreDataModLog orderSucLog = CoreDataModLog.CreateManualLog("Order", "", order.id, "租赁支付回调",
                null, null, null, order.type, "支付成功，检查订单类型");
            await _db.coreDataModLog.AddAsync(orderSucLog);
            await _db.SaveChangesAsync();
            OrderShareController _shareHelper = new OrderShareController(_db, _config, _http);
            switch (order.type)
            {
                case "租赁":
                    CoreDataModLog orderLog = CoreDataModLog.CreateManualLog("Order", "", order.id, "租赁支付回调", null, null, null,
                        paymentId.ToString(), "支付成功，开始生效租赁订单");
                    await _db.coreDataModLog.AddAsync(orderLog);
                    await _db.SaveChangesAsync();
                    RentController _rentHelper = new RentController(_db, _config, _http);
                    await _rentHelper.EffectRentOrder(order.id, (int)paymentId);
                    break;
                case "养护":
                    CoreDataModLog orderCareLog = CoreDataModLog.CreateManualLog("Order", "", order.id, "租赁支付回调", null, null, null,
                        paymentId.ToString(), "支付成功，开始生效养护订单");
                    await _db.coreDataModLog.AddAsync(orderCareLog);
                    await _db.SaveChangesAsync();
                    CareController _careHelper = new CareController(_db, _config, _http);
                    await _careHelper.EffectCareOrder(order.id);
                    break;
                case "雪票":
                    CoreDataModLog orderSkiPassLog = CoreDataModLog.CreateManualLog("Order", "", order.id, "雪票支付回调", null, null, null,
                        paymentId.ToString(), "支付成功，开始生成雪票");
                    await _db.coreDataModLog.AddAsync(orderSkiPassLog);
                    await _db.SaveChangesAsync();
                    SkiPassController _skiPassHelper = new SkiPassController(_db, _config, _http);
                    await _skiPassHelper.CreateSkiPass(order.id);
                    List<OrderShare> shares = await _db.orderShare.Where(s => s.valid && s.order_id == orderId).AsNoTracking().ToListAsync();
                    for (int i = 0; i < shares.Count; i++)
                    {
                        await _shareHelper.CreatePaymentShare(shares[i]);
                    }

                    break;
                default:
                    break;
            }
        }
        [NonAction]
        public async Task<Models.OrderPayment?> QueryPaymentPaid(int paymentId)
        {
            DateTime startTime = DateTime.Now;
            OrderPayment payment = await _db.orderPayment.Where(p => p.id == paymentId).AsNoTracking().FirstOrDefaultAsync();
            for (; payment.status.Trim() == OrderPayment.PaymentStatus.待支付.ToString()
                && payment.valid == 1 && (DateTime.Now - startTime).Seconds <= 7200;)
            {
                Thread.Sleep(1000);
                payment = await _db.orderPayment.Where(p => p.id == paymentId).AsNoTracking().FirstOrDefaultAsync();
            }
            return payment;
        }
        [NonAction]
        public async Task<Models.Order?> QueryOrderPaid(int orderId)
        {
            DateTime startTime = DateTime.Now;
            Models.Order order = await _db.order.Where(o => o.id == orderId).AsNoTracking().FirstOrDefaultAsync();
            OrderPayment payment = await _db.orderPayment.Where(p => p.order_id == orderId && p.valid == 1 && p.queryed == 0
                && p.status.Trim().Equals(OrderPayment.PaymentStatus.待支付.ToString())
                && p.create_date > DateTime.Now.AddHours(-2)).AsNoTracking()
                .OrderByDescending(p => p.id).FirstOrDefaultAsync();
            int? paymentId = null;
            for (; (payment != null && order.dealed == 0 && (DateTime.Now - startTime).Seconds <= 3600);)
            {
                Thread.Sleep(1000);
                if (paymentId == null)
                {
                    paymentId = payment.id;
                }
                payment = await _db.orderPayment.Where(p => p.order_id == orderId && p.valid == 1 && p.queryed == 0
                && p.status.Trim().Equals(OrderPayment.PaymentStatus.待支付.ToString())
                && p.create_date > DateTime.Now.AddHours(-2)).AsNoTracking()
                .OrderByDescending(p => p.id).FirstOrDefaultAsync();
                order = await _db.order.Where(o => o.id == orderId).AsNoTracking().FirstOrDefaultAsync();
            }


            if (paymentId != null)
            {
                payment = await _db.orderPayment.Where(p => p.id == paymentId).AsNoTracking().FirstOrDefaultAsync();
                payment.queryed = 1;
                payment.order = null;
                try
                {
                    _db.orderPayment.Entry(payment).State = EntityState.Modified;
                }
                catch
                {

                }
            }
            order.queryed = 1;
            order.update_date = DateTime.Now;
            try
            {
                _db.order.Entry(order).State = EntityState.Modified;
            }
            catch
            {

            }
            await _db.SaveChangesAsync();
            for (int i = 0; order.payments != null && i < order.payments.Count; i++)
            {
                order.payments[i].order = null;
            }
            //order.payments = null;
            return order;
        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order>>> CancelPaying(int orderId, string sessionKey,
        string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null && staff.title_level < 100)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.Order order = await GetOrder(orderId);
            bool needOrderUpdate = false;
            bool isFirstSetPayMethod = true;
            if (order.current_pay_method != null)
            {
                isFirstSetPayMethod = false;
                order.current_pay_method = null;
                needOrderUpdate = true;
            }
            if (order.pay_flow_status != null)
            {
                order.pay_flow_status = null;
                needOrderUpdate = true;
            }
            bool canceled = true;
            AliController _aliHelper = new AliController(_db, _config, _http);
            TenpayController _weHelper = new TenpayController(_db, _config, _http);
            List<OrderPayment> payments = order.payments
                .Where(p => p.valid == 1 && (p.pay_method.Trim().Equals("微信支付") || p.pay_method.Trim().Equals("支付宝"))
                && !p.status.Trim().Equals(OrderPayment.PaymentStatus.支付成功.ToString())).ToList();
            for (int i = 0; i < payments.Count; i++)
            {
                OrderPayment payment = payments[i];
                OrderPayment oriPayment = await _db.orderPayment.Where(p => p.id == payment.id).AsNoTracking().FirstOrDefaultAsync();
                switch (payment.pay_method.Trim())
                {
                    case "支付宝":
                        if (payment.ali_qr_code != null)
                        {
                            canceled = await _aliHelper.ClosePayment(payment);
                        }
                        break;
                    case "微信支付":
                        if (payment.prepay_id != null)
                        {
                            canceled = await _weHelper.ClosePayment(payment);
                        }
                        break;
                    default:
                        break;
                }
                if (canceled)
                {
                    payment.valid = 0;
                    payment.update_date = DateTime.Now;
                    List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<OrderPayment>(oriPayment, payment, null, staff.id, "修改支付方式");
                    for (int j = 0; j < logs.Count; j++)
                    {
                        await _db.coreDataModLog.AddAsync(logs[i]);
                    }
                    _db.orderPayment.Entry(payment).State = EntityState.Modified;
                }
                else
                {
                    break;
                }
                //await _db.SaveChangesAsync();
            }
            await _db.SaveChangesAsync();
            if (canceled)
            {
                if (needOrderUpdate)
                {
                    await UpdateOrder(order, null, staff.id, "修改支付方式");
                }
                if (!isFirstSetPayMethod)
                {
                    CoreDataModLog log = new CoreDataModLog()
                    {
                        table_name = "Order",
                        field_name = "OrderState",
                        key_value = orderId,
                        prev_value = null,
                        current_value = Models.Order.OrderStatus.待生成.ToString(),
                        staff_id = staff.id,
                        is_manual = 1,
                        scene = "重新选择支付方式",
                        create_date = DateTime.Now
                    };
                    await _db.coreDataModLog.AddAsync(log);
                    await _db.SaveChangesAsync();
                }
                return Ok(new ApiResult<Models.Order>()
                {
                    code = 0,
                    message = "",
                    data = order
                });
            }
            else
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "订单无法修改",
                    data = null
                });
            }
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetUnCommonPayMethod()
        {
            var list = await _db.orderPayment.FromSqlRaw(" select distinct pay_method from order_payment where valid = 1 and status = '"
                + OrderPayment.PaymentStatus.支付成功.ToString() + "' and pay_method not in ('微信支付','支付宝','京东收银','POS机刷卡', '现金') ")
                .OrderBy(p => p.pay_method).AsNoTracking().Select(p => p.pay_method).ToListAsync();
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = list
            });
        }

        [HttpGet("{key}")]
        public async Task<ActionResult<ApiResult<List<CoreDataModLog>>>> LoadLogs(string tableName, string fieldName, int key)
        {
            List<CoreDataModLog> logs = await _db.coreDataModLog.Where(l => l.table_name.ToLower().Trim().Equals(tableName)
                && l.field_name.ToLower().Trim().Equals(fieldName) && l.key_value == key).OrderByDescending(l => l.id).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<CoreDataModLog>>()
            {
                code = 0,
                message = "",
                data = logs
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<CoreDataModLog?>>> LogShowWechatQrCode(int orderId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null && staff.title_level < 100)
            {
                return Ok(new ApiResult<CoreDataModLog?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            CoreDataModLog log = new CoreDataModLog()
            {
                table_name = "Order",
                field_name = "OrderState",
                key_value = orderId,
                prev_value = null,
                current_value = Models.Order.OrderStatus.待支付.ToString(),
                staff_id = staff.id,
                is_manual = 1,
                scene = "显示微信支付二维码",
                create_date = DateTime.Now
            };
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<CoreDataModLog?>()
            {
                code = 0,
                message = "",
                data = log
            });
        }
        [HttpGet("{paymentId}")]
        public async Task<ActionResult<ApiResult<Models.Order?>>> GetOrderFromPaymentByCustomer(int paymentId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);

            OrderPayment payment = await _db.orderPayment.Where(p => p.id == paymentId).AsNoTracking().FirstOrDefaultAsync();
            if (payment == null || payment.valid == 0)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "没有找到",
                    data = null
                });
            }
            Models.Order order = await GetOrder(payment.order_id);
            if (payment.status.Trim() == OrderPayment.PaymentStatus.支付成功.ToString()
             && order.member_id != member.id && payment.member_id != member.id)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "订单和当前用户无关",
                    data = null
                });
            }
            return Ok(new ApiResult<Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [NonAction]
        public async Task<Models.Order> UpdateOrderWithDetail(Models.Order order, int? staffId, int? memberId, string scene)
        {
            Models.Order oriOrder = await GetOrder(order.id);
            for (int i = 0; i < order.retails.Count; i++)
            {
                Retail retail = order.retails[i];
                Retail oriRetail = oriOrder.retails.Where(r => r.id == retail.id).FirstOrDefault();
                if (oriRetail != null)
                {
                    List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Retail>(oriRetail, retail, memberId, staffId, scene);
                    for (int j = 0; j < logs.Count; j++)
                    {
                        await _db.coreDataModLog.AddAsync(logs[j]);
                    }
                }
            }
            order.update_date = DateTime.Now;
            List<CoreDataModLog> orderLogs = Util.GetUpdateDifferenceLog<Models.Order>(oriOrder, order, memberId, staffId, scene);
            for (int j = 0; j < orderLogs.Count; j++)
            {
                await _db.coreDataModLog.AddAsync(orderLogs[j]);
            }
            _db.Update(order);
            await _db.SaveChangesAsync();
            return order;
        }
        [NonAction]
        public async Task<bool> CheckMi7Code(string mi7Code, int retailId)
        {
            if (!Util.IsValidMi7Code(mi7Code))
            {
                return false;
            }
            List<Retail> rList = await _db.retail.Include(r => r.order)
                .Where(r => r.valid == 1 && r.mi7_code.Trim() == mi7Code && r.id != retailId && r.order.valid == 1)
                .AsNoTracking().ToListAsync();
            if (rList == null || rList.Count == 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Models.Order?>>> UpdateOrderWithDetailByStaff([FromBody] Models.Order order,
            [FromQuery] string scene, [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            bool mi7CodeValid = true;
            for (int i = 0; order.retails != null && i < order.retails.Count; i++)
            {
                Retail retail = order.retails[i];
                if (retail.mi7_code != null)
                {
                    if (!(await CheckMi7Code(retail.mi7_code, retail.id)))
                    {
                        mi7CodeValid = false;
                        break;
                    }
                }
            }
            if (!mi7CodeValid)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "七色米重复",
                    data = null
                });
            }
            order.member = null;
            order = await UpdateOrderWithDetail(order, staff.id, null, scene);
            return Ok(new ApiResult<Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpPost("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order?>>> Refund([FromRoute] int orderId,
        [FromBody] List<OrderPaymentRefund> refunds, [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.Order order = await GetOrder(orderId);
            string message = "";
            for (int i = 0; i < refunds.Count; i++)
            {
                OrderPaymentRefund refund = refunds[i];
                OrderPayment payment = order.availablePayments.Where(p => p.id == refund.payment_id).FirstOrDefault();
                if (payment == null)
                {
                    message = "该笔支付记录不存在";
                    break;
                }
                else if (Math.Round(payment.refundedAmount + refund.amount, 2) > Math.Round(payment.amount, 2))
                {
                    message = "退款金额超过可退款金额";
                    break;
                }
                else
                {
                    refund.out_refund_no = payment.out_trade_no + "_TK_" + (payment.refunds.Count + 1).ToString().PadLeft(2, '0');
                    refund.order_id = orderId;
                    refund.staff_id = staff.id;
                    refund.create_date = DateTime.Now;
                    if (payment.pay_method != "微信支付" && payment.pay_method != "支付宝")
                    {
                        refund.state = 1;
                    }
                    else
                    {
                        refund.state = 0;
                    }
                    refund.refund_id = "";
                    await _db.orderPaymentRefund.AddAsync(refund);
                }
            }
            if (!message.Trim().Equals(""))
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = message,
                    data = null
                });
            }
            await _db.SaveChangesAsync();
            AliController _aliHelper = new AliController(_db, _config, _http);
            TenpayController _weHelper = new TenpayController(_db, _config, _http);
            for (int i = 0; i < refunds.Count; i++)
            {
                OrderPaymentRefund refund = refunds[i];
                OrderPayment payment = order.availablePayments.Where(p => p.id == refund.payment_id).FirstOrDefault();
                switch (payment.pay_method.Trim())
                {
                    case "支付宝":
                        refund = await _aliHelper.Refund(refund.id);
                        break;
                    case "微信支付":
                        refund = await _weHelper.Refund(refund.id);
                        break;
                    default:
                        break;
                }
            }
            order = await GetOrder(orderId);

            try
            {
                if (order.type == "租赁" && order.shop == "万龙体验中心" && order.hide == false && order.valid == 1 && order.paidAmount > 0
                && order.refundAmount > 0 && Math.Round((double)order.totalRentUnRefund, 2) == 0)
                {
                    OrderShareController _shareHelper = new OrderShareController(_db, _config, _http);
                    List<OrderShare> orderShares = await _shareHelper.CreateOrderShares(order);
                }
            }
            catch
            {

            }

            return Ok(new ApiResult<Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpGet("{tempOrderId}")]
        public async Task<ActionResult<ApiResult<Models.Order>>> PlaceRentOrder(int tempOrderId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.Order order = await _db.order.Include(o => o.rentals).ThenInclude(r => r.rentItems)
                .Where(o => o.id == tempOrderId && o.valid == 0).AsNoTracking().FirstOrDefaultAsync();
            if (order == null)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "无订单数据",
                    data = null
                });
            }
            double paidAmount = 0;
            RentController _rentHelper = new RentController(_db, _config, _http);
            for (int i = 0; i < order.rentals.Count; i++)
            {
                Rental rental = order.rentals[i];
                double guarantyAmount = 0;
                if (!rental.noGuaranty)
                {
                    guarantyAmount = (double)rental.guaranty - (double)rental.guaranty_discount;
                }
                if (guarantyAmount > 0)
                {
                    Guaranty guaranty = new Guaranty()
                    {
                        id = 0,
                        order_id = order.id,
                        guaranty_type = "在线支付",
                        biz_type = "租赁",
                        biz_id = rental.id,
                        sub_biz_type = null,
                        sub_biz_id = null,
                        amount = guarantyAmount,
                        memo = "",
                        valid = 1,
                        relieve = 0,
                        staff_id = staff.id,
                        create_date = DateTime.Now
                    };
                    await _db.guaranty.AddAsync(guaranty);
                }
                rental.valid = 1;
                rental.staff_id = staff.id;
                rental.update_date = DateTime.Now;

                _db.rental.Entry(rental).State = EntityState.Modified;
                paidAmount += guarantyAmount;
            }
            order.valid = 1;
            order.update_date = DateTime.Now;
            order.staff_id = staff.id;
            order.paying_amount = paidAmount;
            await GenerateOrderCode(order);
            _db.order.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            _db.order.Entry(order).State = EntityState.Detached;
            for (int i = 0; i < order.rentals.Count; i++)
            {
                Rental rental = order.rentals[i];
                _db.rental.Entry(rental).State = EntityState.Detached;
                //await _db.SaveChangesAsync();
                if (rental.entertain)
                {
                    await _rentHelper.EffectRental(rental.id, rental.staff_id);
                }
            }
            return Ok(new ApiResult<Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [NonAction]
        public async Task<Discount> UpdateSingleDiscount(int orderId, string bizType, int bizId, string? subBizType,
            int? subBizId, double amount, int? staffId, string scene, string memo = "", string? ticketCode = null)
        {
            Discount discount = await _db.discount.Where(d => d.order_id == orderId && d.biz_type == bizType && d.biz_id == bizId
                && d.sub_biz_type == subBizType && d.sub_biz_id == subBizId && d.ticket_code == ticketCode)
                .AsNoTracking().FirstOrDefaultAsync();
            if (amount == 0 && discount != null)
            {
                CoreDataModLog log = CoreDataModLog.CreateManualLog("discount", "valid", discount.id, scene, null, staffId,
                    discount.valid.ToString(), "0", "取消减免");
                discount.valid = 0;
                discount.staff_id = staffId;
                discount.update_date = DateTime.Now;
                await _db.coreDataModLog.AddAsync(log);
                _db.discount.Entry(discount).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            if (discount == null && amount != 0)
            {
                discount = new Discount()
                {
                    id = 0,
                    order_id = orderId,
                    biz_type = bizType,
                    biz_id = bizId,
                    sub_biz_type = subBizType,
                    sub_biz_id = subBizId,
                    amount = amount,
                    member_id = null,
                    staff_id = staffId,
                    valid = 1,
                    create_date = DateTime.Now
                };
                await _db.discount.AddAsync(discount);
                await _db.SaveChangesAsync();
            }
            else
            {
                Discount oriDiscount = await _db.discount.Where(d => d.id == discount.id).AsNoTracking().FirstOrDefaultAsync();
                discount.valid = 1;
                discount.amount = amount;
                discount.staff_id = staffId;
                discount.update_date = DateTime.Now;
                List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Discount>(oriDiscount, discount, null, staffId, scene);
                for (int i = 0; i < logs.Count; i++)
                {
                    await _db.coreDataModLog.AddAsync(logs[i]);
                }
                _db.discount.Entry(discount).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            return discount;
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order?>>> PayWithDeposit(int orderId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            bool canNotFindMember = false;
            bool canNotFindOrder = false;
            OrderController _orderHelper = new OrderController(_db, _config, _http);
            Models.Order order = await _orderHelper.GetOrder(orderId);

            if ((order == null || order.paying_amount == null) && order.type != "租赁")
            {
                canNotFindOrder = true;
            }
            if (order.member_id == null)
            {
                canNotFindMember = true;
            }
            MemberController _memberHelper = new MemberController(_db, _config);
            Member member = await _memberHelper.GetWholeMemberById((int)order.member_id);
            if (member == null)
            {
                canNotFindMember = true;
            }
            if (canNotFindMember || canNotFindOrder)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = canNotFindMember ? "未找到会员信息" : (canNotFindOrder ? "未找到订单信息" : ""),
                    data = null
                });
            }
            double payingAmount = 0;
            if (order.type == "租赁")
            {
                payingAmount = (double)order.totalRentSummaryAmount;
            }
            else
            {
                payingAmount = Math.Round((double)order.paying_amount, 2);
            }
            double availableAmount = Math.Round(member.availableDeposit, 2);
            if (payingAmount > availableAmount)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "储值余额不足",
                    data = null
                });
            }
            if (order.paidAmount > 0 && order.type != "租赁")
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "订单已支付",
                    data = null
                });
            }
            if (order.depositPaidAmount == 0)
            {
                DepositController _depositHelper = new DepositController(_db, _config);
                OrderPayment payment = new OrderPayment()
                {
                    id = 0,
                    order_id = order.id,
                    pay_method = "储值支付",
                    staff_id = staff.id,
                    member_id = order.member_id,
                    amount = payingAmount,
                    status = OrderPayment.PaymentStatus.待支付.ToString(),
                    deposit_type = "服务储值",
                    create_date = DateTime.Now
                };
                await _db.orderPayment.AddAsync(payment);
                await _db.SaveChangesAsync();
                List<DepositBalance> balances = await _depositHelper.ConsumeDeposit(payment);
                if (balances == null)
                {
                    return Ok(new ApiResult<Models.Order?>()
                    {
                        code = 1,
                        message = "消费失败",
                        data = null
                    });
                }
            }
            member = await _memberHelper.GetWholeMemberById(member.id);
            order.member = member;
            order.paying_amount = null;
            _db.order.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            _db.order.Entry(order).State = EntityState.Detached;
            await _db.SaveChangesAsync();
            CareController _careHelper = new CareController(_db, _config, _http);

            if (order.type == "养护")
            {
                await _careHelper.EffectCareOrder(order.id);
            }
            order = await _orderHelper.GetOrder(order.id);
            return Ok(new ApiResult<Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [NonAction]
        public async Task SetReferee(int orderId, int? bizId)
        {
            Models.Order order = await _db.order.Where(o => o.id == orderId).AsNoTracking().FirstOrDefaultAsync();
            if (order == null || order.member_id == null)
            {
                return;
            }
            BizReferee referee = await _db.bizReferee
                .Where(b => b.biz_type == "雪票" && b.member_id == order.member_id && b.valid)
                .AsNoTracking().FirstOrDefaultAsync();
            if (referee == null && order.staff_id != null)
            {
                BizReferee bf = new BizReferee()
                {
                    id = 0,
                    biz_type = order.type,
                    order_id = orderId,
                    member_id = (int)order.member_id,
                    biz_id = bizId,
                    staff_id = order.staff_id,
                    valid = true,
                    create_date = DateTime.Now
                };
                await _db.bizReferee.AddAsync(bf);
                await _db.SaveChangesAsync();
            }
        }
        public class OrderBalance
        {
            public DateTime? transDate { get; set; }
            public string transType { get; set; }
            public string payMethod { get; set; }
            public double amount { get; set; }
            public Staff staff { get; set; }

        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ActionResult<List<OrderBalance>?>>> GetOrderBalance(int orderId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<OrderBalance>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.Order order = await GetOrder(orderId);
            List<OrderBalance> balances = new List<OrderBalance>();
            for (int i = 0; order.availablePayments != null && i < order.availablePayments.Count; i++)
            {
                OrderPayment payment = order.availablePayments[i];
                OrderBalance b = new OrderBalance()
                {
                    transDate = payment.paid_date,
                    transType = "支付",
                    payMethod = payment.pay_method,
                    amount = payment.amount,
                    staff = payment.staff

                };
                balances.Add(b);
                for (int j = 0; payment.refunds != null && j < payment.refunds.Count; j++)
                {
                    OrderPaymentRefund refund = payment.refunds[j];
                    if (refund.state == 1 || refund.refund_id != "")
                    {
                        OrderBalance br = new OrderBalance()
                        {
                            transDate = refund.create_date,
                            transType = "退款",
                            payMethod = payment.pay_method,
                            amount = refund.amount,
                            staff = refund.staff

                        };
                        balances.Add(br);
                    }
                }
            }
            balances = balances.OrderBy(b => b.transDate).ToList();
            return Ok(new ApiResult<List<OrderBalance>?>()
            {
                code = 0,
                message = "",
                data = balances
            });
        }
        [NonAction]
        public async Task<Models.Order?> SetOrderClosedState(Models.Order order, bool closed, int staffId)
        {
            if (order.close_date == null)
            {
                return null;
            }
            bool isOpen = order.closed == 1? false: true;
            if (isOpen != closed)
            {
                return null;
            }
            order.closed = closed? 1: 0;
            order.close_date = closed ? DateTime.Now : order.close_date;
            CoreDataModLog log = new CoreDataModLog()
            {
                table_name = "Order",
                field_name = "closed",
                key_value = order.id,
                prev_value = isOpen? "0": "1",
                current_value = closed? "1": "0",
                staff_id = staffId,
                is_manual = 1,
                scene = closed? "订单关闭": "订单重开",
                create_date = DateTime.Now
            };
            await _db.coreDataModLog.AddAsync(log);
            _db.order.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return order;

        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order?>>> SetOrderCloseStatus(int orderId, bool closed,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<OrderBalance>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.Order order = await _db.order.FindAsync(orderId);
            if (order == null)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "没有找到订单",
                    data = null
                });
            }
            order = await SetOrderClosedState(order, closed, staff.id);
            if (order == null)
            {
                return Ok(new ApiResult<List<OrderBalance>?>()
                {
                    code = 1,
                    message = "无需重复操作",
                    data = null
                });
            }
            else
            {
                return  Ok(new ApiResult<Models.Order>()
                {
                    code = 0,
                    message = "",
                    data = order
                });
            }
        }
    }

}