using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class RetailController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        public RetailController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
            _config = config;
        }
        [HttpGet]
        public async Task<ActionResult<List<Models.Order.Retail>>> ShowMi7Order(DateTime startDate)
        {
            List<Models.Order.Retail> retailList = new List<Retail>();
            var mi7List  =  await (from m in _db.mi7Order
                .Where(m => m.create_date.Date >= startDate.Date && m.mi7_order_id.Trim().StartsWith("XSD"))
                group m by m.mi7_order_id into g
                select new { mi7OrderId = g.Key, salePrie = g.Sum(g => g.sale_price), charge = g.Sum(g => g.real_charge), count = g.Count() })
                .AsNoTracking().ToListAsync();
            List<Mi7Order> mi7Orders = await _db.mi7Order.Where(m => m.create_date.Date >= startDate.Date)
                .Include(m => m.order).ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .AsNoTracking().ToListAsync();
            for(int i = 0; i < mi7List.Count; i++)
            {
                Retail r = new Retail()
                {
                    mi7OrderId = mi7List[i].mi7OrderId,
                    salePrie = mi7List[i].salePrie,
                    charge = mi7List[i].charge,
                    count = mi7List[i].count
                };
                List<Mi7Order> subMi7Orders = mi7Orders.Where(m => m.mi7_order_id.Trim().Equals(r.mi7OrderId.Trim())).ToList();
                List<OrderOnline> orderList = new List<OrderOnline>();
                for(int j = 0; j < subMi7Orders.Count; j++)
                {
                    Mi7Order mi7Order = subMi7Orders[j];
                    if (mi7Order != null && orderList.Select(o => o.id == mi7Order.order_id).ToList().Count == 0)
                    {
                        orderList.Add(mi7Order.order);
                    }
                }
                r.orders = orderList;
                retailList.Add(r);
            }
            List<Retail> newList = retailList.OrderBy(r => r.orders[0].create_date).ToList();
            return Ok(newList);
        } 
    }
}