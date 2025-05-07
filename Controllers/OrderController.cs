using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
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
        public async Task<List<SnowmeetApi.Models.Order>> GetRetailOrders(int? orderId, DateTime? startDate = null, DateTime? endDate = null,
            string? shop = null, string? mi7Num = null, string? cell = null, string? mi7OrderId = null)
        {
            startDate = startDate == null ? DateTime.MinValue : startDate;
            endDate = endDate == null ? DateTime.MaxValue : endDate;
            List<SnowmeetApi.Models.Order> orderList = await _db.order
                .Include(o => o.retails)
                .Include(o => o.payments).ThenInclude(p => p.staff)
                .Include(o => o.payments).ThenInclude(o => o.refunds)
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
            int? staffId, string? type, string? subType, DateTime? startDate, DateTime? endDate, string payOption = "普通")
        {
            startDate = startDate == null ? DateTime.MinValue : startDate;
            endDate = endDate == null ? DateTime.MaxValue : endDate;
            List<SnowmeetApi.Models.Order> orderList = await _db.order
                .Include(o => o.retails)
                .Include(o => o.cares).ThenInclude(c => c.tasks.OrderBy(t => t.id))
                .Include(o => o.payments).ThenInclude(p => p.staff)
                .Include(o => o.payments).ThenInclude(o => o.refunds)
                .Include(o => o.staff)
                .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                .Where(o => (o.biz_date.Date >= ((DateTime)startDate).Date && o.biz_date.Date <= ((DateTime)endDate).Date)
                    && (memberId == null || o.member_id == memberId) && (staffId == null || o.staff_id == staffId)
                    && o.pay_option.Trim().Equals(payOption.Trim())
                    && (shop == null || o.shop.Trim().Equals(shop.Trim())))
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
            return orderList;
        }
        [NonAction]
        public async Task<SnowmeetApi.Models.Order> UpdateOrder(SnowmeetApi.Models.Order order, int? memberId, int? staffId, string scene)
        {
            SnowmeetApi.Models.Order oriOrder = await _db.order.FindAsync(order.id);
            List<CoreDataModLog> logs = SnowmeetApi.Models.Order.GetUpdateDifferenceLog(oriOrder, order, memberId, staffId, scene);
            foreach (CoreDataModLog log in logs)
            {
                await _db.coreDataModLog.AddAsync(log);
            }
            oriOrder.update_date = DateTime.Now;
            _db.order.Entry(oriOrder).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return oriOrder;
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
            ApiResult<List<Shop>> shopResult = (ApiResult<List<Shop>>)((OkObjectResult)(await GetShops()).Result).Value;
            string shopCode = "WZ";
            for(int i = 0; i < shopResult.data.Count; i++)
            {
                if (shopResult.data[i].name.Trim().Equals(order.shop.Trim()))
                {
                    shopCode = shopResult.data[i].code.Trim();
                    break;
                }
            }
            string bizCode = "";
            switch(order.type.Trim())
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
            for(int i = 0; order.retails != null && i < order.retails.Count; i++)
            {
                Retail retail = order.retails[i];
                if (retail.mi7_code != null)
                {
                    List<Retail> retails = await _db.retail.Include(r => r.order)
                        .Where(r => r.mi7_code.Equals(retail.mi7_code.Trim()) && r.valid == 1 )
                        .AsNoTracking().ToListAsync();
                    for(int j = 0; j < retails.Count; j++)
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
            for(int i = 0; order.retails != null && i < order.retails.Count; i++)
            {
                Retail retail = order.retails[i];
                retail.valid = 0;
                retail.update_date = DateTime.Now;
                _db.retail.Entry(retail).State = EntityState.Modified;
            }
            for(int i = 0; order.payments != null && i < order.payments.Count; i++)
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
                key_value = order.id.ToString(),
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
        public async Task<ActionResult<ApiResult<List<Shop>>>> GetShops()
        {
            List<Shop> shopList = await _db.shop.OrderBy(s => s.sort).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<Shop>>()
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
                        || o.paymentStatus.Trim().Equals("无需支付") ).ToList();
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
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
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
            order = await UpdateOrder(order, null, staff.id, scene);
            return Ok(new ApiResult<SnowmeetApi.Models.Order>()
            {
                code = 0,
                message = "",
                data = order
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
                retail.logs = await _logHelper.GetSimpleLogs("retail", retail.id.ToString());
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
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (staff != null && staff.title_level >= 100)
            {
                order.staff_id = staff.id;
            }
            else if (member != null && order.member_id == null)
            {
                order.member_id = member.id;
            }
            switch(order.type)
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
            await GenerateOrderCode(order);
            await _db.order.AddAsync(order);
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
            string payOption = "普通", string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<object?>(){
                    code = 1,
                    message = "不是管理员",
                    data = null
                });
            }
            List<SnowmeetApi.Models.Order> orders = await GetCommonOrders(orderId, shop, null, null, type, subType, startDate, endDate, payOption);
            SnowmeetApi.Models.Order.RendOrderList(orders);
            return Ok(new ApiResult<List<SnowmeetApi.Models.Order>>(){
                code = 0,
                message = "",
                data = orders
            });
        }
    }
}