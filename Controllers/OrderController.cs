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

        [HttpGet]
        public async Task<List<SnowmeetApi.Models.Order>> GetRetailOrders(int? orderId, DateTime? startDate = null, DateTime? endDate = null,
            string? shop = null, string? status = null, string? mi7Num = null, string? cell = null, string? mi7OrderId = null)
        {
            startDate = startDate == null? DateTime.MinValue: startDate;
            endDate = endDate == null? DateTime.MaxValue: endDate;
            List<SnowmeetApi.Models.Order> orderList = await _db.order
                .Include(o => o.retails)
                .Include(o => o.payments).ThenInclude(o => o.refunds)
                .Include(o => o.staff)
                .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                .Where(o => o.valid == 1 && o.type == "零售" 
                    && o.biz_date.Date >= ((DateTime)startDate).Date && o.biz_date.Date <= ((DateTime)endDate).Date  
                    && (shop == null || o.shop.Equals(shop.Trim())) 
                    && (status == null || o.paymentStatus.Equals(status.Trim()))
                    && (mi7Num == null || (mi7Num.Trim().Equals("已填") && !o.retails.Any(r => r.mi7_code == null ) ) || (mi7Num.Trim().Equals("未填") && o.retails.Any(r => r.mi7_code == null ))  )
                    && (cell == null || o.cell.EndsWith(cell.Trim()) || o.member.cell.EndsWith(cell.Trim()) )
                    && (orderId == null || o.id == orderId)
                    && (mi7OrderId == null || o.retails.Any(r => r.mi7_code.IndexOf(mi7OrderId.Trim()) >= 0) )
                ).AsNoTracking().ToListAsync();
            return orderList;
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
        [NonAction]
        public async Task<ActionResult<ApiResult<List<SnowmeetApi.Models.Order>>>> GetRetailOrders(string staffSessionKey, DateTime startDate, DateTime endDate,
            string shop = "", string status = "", string mi7Num = "", bool onlyMine = false, string cell = "", string orderId = "0", string mi7OrderId = "")
        {
            return BadRequest();
        }
    }
}