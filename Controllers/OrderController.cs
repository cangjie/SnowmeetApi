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
            order.cares = await _db.order.Entry(order).Collection(o => o.cares).Query().Include(c => c.tasks).AsNoTracking().ToListAsync();
            order.fdOrders = await _db.order.Entry(order).Collection(o => o.fdOrders).Query()
                .Where(r => r.valid == 1).AsNoTracking()
                .Include(f => f.product).ThenInclude(p => p.category)
                .Include(f => f.discounts.Where(d => d.valid == 1 && d.biz_type.Trim().Equals("餐饮"))).AsNoTracking().ToListAsync();
            order.rentals = await _db.order.Entry(order).Collection(o => o.rentals).Query().AsNoTracking()
                .Include(r => r.discounts.Where(d => d.valid == 1 && d.biz_type.Trim().Equals("租赁"))).AsNoTracking()
                .Include(r => r.details.Where(d => d.valid == 1)).AsNoTracking()
                .Include(r => r.rentItems.Where(i => i.valid == 1))
                    .ThenInclude(r => r.repairationCharge)
                .Include(r => r.rentItems.Where(i => i.valid == 1))
                    .ThenInclude(i => i.logs.Where(l => l.table_name.Trim().Equals("rent_item")).OrderByDescending(o => o.id))
                        .ThenInclude(l => l.staff)
                .Include(r => r.guaranties.Where(g => g.valid == 1 && g.biz_type.Trim().Equals("租赁"))).ThenInclude(g => g.guarantyPayments).ThenInclude(g => g.payment)
                .AsNoTracking().ToListAsync();
            order.discounts = await _db.order.Entry(order).Collection(o => o.discounts).Query().Where(d => d.valid == 1).ToListAsync();
            await _db.order.Entry(order).Reference(o => o.staff).LoadAsync();
            await _db.order.Entry(order).Reference(o => o.member).LoadAsync();
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
            bool? isTest = null, bool? isEnterain = null, bool? isPackage = null, bool? isOnCredit = null,
            bool? haveDiscount = null, string? status = null)
        {
            startDate = startDate == null ? DateTime.MinValue : startDate;
            endDate = endDate == null ? DateTime.MaxValue : endDate;
            List<SnowmeetApi.Models.Order> orderList = await _db.order
                .Include(o => o.retails.Where(r => r.valid == 1))
                .Include(o => o.cares.Where(c => c.valid == 1)).ThenInclude(c => c.tasks.Where(t => t.valid == 1).OrderBy(t => t.id))
                .Include(o => o.rentals.Where(r => r.valid == 1)).ThenInclude(r => r.details.Where(d => d.valid == 1))
                .Include(o => o.rentals.Where(r => r.valid == 1)).ThenInclude(r => r.rentItems.Where(r => r.valid == 1))
                .Include(o => o.fdOrders.Where(f => f.valid == 1)).ThenInclude(f => f.product).ThenInclude(p => p.category)
                .Include(o => o.payments).ThenInclude(p => p.staff)
                .Include(o => o.payments).ThenInclude(o => o.refunds)
                .Include(o => o.discounts.Where(d => d.valid == 1))
                .Include(o => o.guarantys.Where(g => g.valid == 1)).ThenInclude(g => g.guarantyPayments).ThenInclude(g => g.payment)
                .Include(o => o.staff)
                .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                .Where(o => (o.biz_date.Date >= ((DateTime)startDate).Date && o.biz_date.Date <= ((DateTime)endDate).Date)
                    && (memberId == null || o.member_id == memberId) && (staffId == null || o.staff_id == staffId)
                    && (payOption == null || o.pay_option.Trim().Equals(payOption.Trim()))
                    && (shop == null || o.shop.Trim().Equals(shop.Trim())) && (type == null || o.type.Trim().Equals(type.Trim()))
                    && o.valid == 1)
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();

            if (isTest != null)
            {
                orderList = orderList.Where(o => o.is_test == ((bool)isTest ? 1 : 0)).ToList();
            }
            if (isEnterain != null)
            {
                orderList = orderList.Where(o => o.haveEntrain == isEnterain).ToList();
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
                    break;
                default:
                    break;
            }
            if (_http.HttpContext.Request.Host.Value != null
                && _http.HttpContext.Request.Host.Value.Equals("mini.snowmeet.top"))
            {
                order.is_test = 0;
            }
            else
            {
                order.is_test = 1;
            }

            await _db.order.AddAsync(order);
            await _db.SaveChangesAsync();
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
            string? payOption, string sessionType = "wechat_mini_openid", bool? isTest = null, bool? isEnterain = null,
            bool? isPackage = null, bool? isOnCredit = null, bool? haveDiscount = null, string? status = null)
        {
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
            List<SnowmeetApi.Models.Order> orders = await GetCommonOrders(orderId, shop, null, null, type, startDate, endDate, payOption,
            isTest, isEnterain, isPackage, isOnCredit, haveDiscount, status);
            SnowmeetApi.Models.Order.RendOrderList(orders);
            return Ok(new ApiResult<List<SnowmeetApi.Models.Order>>()
            {
                code = 0,
                message = "",
                data = orders
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
            /*
            if (order.single_payment == 1)
            {
                payAmount = order.totalCharge;
            }
            else if (amount == null)
            {
                payAmount = order.totalCharge;
            }
            else
            {
                payAmount = (double)amount;
            }
            */
            if (amount == null)
            {
                payAmount = order.totalCharge;
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
        public async Task<ActionResult<ApiResult<string>>> GetAlipayPaymentQrCode(int orderId, double? amount,
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
                    return Ok(new ApiResult<string>()
                    {
                        code = 0,
                        message = "",
                        data = payment.ali_qr_code.Trim()
                    });
                }
            }
            return Ok(new ApiResult<string>()
            {
                code = 1,
                message = message,
                data = null
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
                    amount = order.totalCharge,
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
                    amount = order.totalCharge,
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

            await DealSuccessPaidOrder(order.id);
            return Ok(new ApiResult<Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [NonAction]
        public async Task DealSuccessPaidOrder(int orderId)
        {
            Models.Order order = await _db.order.Where(o => o.id == orderId)
                .AsNoTracking().FirstOrDefaultAsync();
            order.dealed = 1;
            order.pay_flow_status = Models.Order.PayFlowStatus.已支付.ToString();
            
            await UpdateOrder(order, null, null, "支付成功");
        }
        [HttpGet]
        public async Task<Models.Order?> QueryOrderPaid(int orderId)
        {
            DateTime startTime = DateTime.Now;
            Models.Order order = await _db.order.Where(o => o.id == orderId).AsNoTracking().FirstOrDefaultAsync();

            OrderPayment payment = await _db.orderPayment.Where(p => p.order_id == orderId && p.valid == 1 && p.queryed == 0
                 && p.status.Trim().Equals(OrderPayment.PaymentStatus.支付成功.ToString())
                 && p.paid_date > DateTime.Now.AddHours(-4)).AsNoTracking()
                 .OrderByDescending(p => p.id).FirstOrDefaultAsync();

            for (; (payment == null && order.dealed == 0 && (DateTime.Now - startTime).Seconds <= 3600);)
            {
                Thread.Sleep(1000);
                payment = await _db.orderPayment.Where(p => p.order_id == orderId && p.valid == 1 && p.queryed == 0
                 && p.status.Trim().Equals(OrderPayment.PaymentStatus.支付成功.ToString())
                 && p.paid_date > DateTime.Now.AddHours(-4)).AsNoTracking()
                 .OrderByDescending(p => p.id).FirstOrDefaultAsync();
                order = await _db.order.Where(o => o.id == orderId).AsNoTracking().FirstOrDefaultAsync();
            }
            if (payment != null)
            {
                payment.queryed = 1;
                payment.order = null;
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            order.queryed = 1;
            order.update_date = DateTime.Now;
            _db.order.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            order.payments = null;
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
        /*
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<List<CoreDataModLog>>>> GetOrderStatusLog(int orderId)
        {
            Models.Order order = await _db.order.Where(o => o.id == orderId).Include(o => o.staff).Include(o => o.member)
                .AsNoTracking().FirstOrDefaultAsync();
            if (order == null)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "未找到订单",
                    data = null
                });
            }
            List<CoreDataModLog> logs = await _db.coreDataModLog.Where(c => (c.table_name.ToLower().Equals("order")
                && c.key_value == orderId && c.field_name.ToString().Trim().ToLower().Equals("orderstate")))
                .Include(c => c.staff).Include(c => c.member)
                .OrderByDescending(c => c.id).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<CoreDataModLog>>()
            {
                code = 0,
                message = "",
                data = logs
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<List<CoreDataModLog>>>> GetOrderMemoLog(int orderId)
        {
            List<CoreDataModLog> logs = await _db.coreDataModLog.Where(l => l.table_name.ToLower().Equals("order")
                && l.key_value == orderId && l.field_name.Trim().Equals("memo"))
                .Include(l => l.staff).Include(l => l.member).AsNoTracking()
                .OrderByDescending(l => l.id).ToListAsync();
            return Ok(new ApiResult<List<CoreDataModLog>>()
            {
                code = 0,
                message = "",
                data = logs
            });
        }
        */
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
        public async Task LogShowWechatQrCode(int orderId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null && staff.title_level < 100)
            {
                return;
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
        }
    }

}