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
                orders = orders.Where(o => o.paymentStatus.Trim().Equals(status)).ToList();
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
    }
}