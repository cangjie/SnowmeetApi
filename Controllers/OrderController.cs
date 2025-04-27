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
                    //&& (status == null || o.paymentStatus.Equals(status.Trim()))
                    && (mi7Num == null || (mi7Num.Trim().Equals("已填") && !o.retails.Any(r => r.mi7_code == null ) ) || (mi7Num.Trim().Equals("未填") && o.retails.Any(r => r.mi7_code == null ))  )
                    && (cell == null ||(cell.Length >=4 && o.cell.EndsWith(cell.Trim()) ) || o.member.memberSocialAccounts.Any(m => cell.Length >= 4 && m.type.Trim().Equals("cell") && m.num.EndsWith(cell) ) )
                    && (orderId == null || o.id == orderId)
                    && (mi7OrderId == null || o.retails.Any(r => r.mi7_code.IndexOf(mi7OrderId.Trim()) >= 0) )
                ).OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
            return orderList;
        }
        [NonAction]
        public static void RendOrderList(List<SnowmeetApi.Models.Order> orderList)
        { 
            for(int i = 0; i < orderList.Count; i++)
            {
                string txtColor = "";
                string backColor = "";
                SnowmeetApi.Models.Order order = orderList[i];
                if (order.paidAmount < order.totalCharge && order.closed == 0)
                {
                    txtColor = "red";
                }
                else if (order.retails != null 
                    && order.retails.Any(r => (r.mi7_code == null || r.mi7_code.StartsWith("XSD") || r.mi7_code.Length != 15 || (r.mi7_code.ToUpper().EndsWith("A") && r.mi7_code.ToUpper().EndsWith("I") ) )))
                {
                    txtColor = "orange";
                }
                if (order.paidAmount == 0 && order.pay_option.Trim().Equals("招待"))
                {
                    backColor = "yellow";
                }
                order.textColor = txtColor;
                order.backgroundColor = backColor;
            }
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
            string? cell = null, string? mi7OrderId = null, bool onlyMine = false)
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(staffSessionKey);
            if (staff == null)
            {
                return new ApiResult<List<SnowmeetApi.Models.Order>>()
                {
                    code = 1,
                    message = "不能确定员工身份。",
                    data = null
                };
            }
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
            RendOrderList(orders);
            return new ApiResult<List<SnowmeetApi.Models.Order>>()
            {
                code = 0,
                message = "",
                data = orders
            };
        }
    }
}