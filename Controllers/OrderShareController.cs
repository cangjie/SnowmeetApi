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
    public class OrderShareController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        public OrderShareController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
        }
        [NonAction]
        public async Task<List<OrderShare>> CreateShares(Models.Order order)
        {
            if (order.valid == 0 || order.hide)
            {
                return null;
            }
            List<OrderPayment> paymentsWithShare = await _db.orderPayment
                .Where(p => p.order_id == order.id && p.valid == 1 && p.status == "支付成功")
                .Include(p => p.paymentShares.Where(s => s.valid)).AsNoTracking().ToListAsync();
            bool havePaymentShares = false;
            for(int i = 0; i < paymentsWithShare.Count; i++)
            {
                OrderPayment payment = paymentsWithShare[i];
                if (payment.paymentShares.Count > 0)
                {
                    havePaymentShares = true;
                    break;
                }
            }
            if (havePaymentShares)
            {
                return null;
            }
            List<OrderShare> oriShares = await _db.orderShare.Where(s => s.valid && s.order_id == order.id)
                .AsNoTracking().ToListAsync();
            for(int i = 0; i < oriShares.Count; i++)
            {
                oriShares[i].valid = false;
                _db.orderShare.Entry(oriShares[i]).State = EntityState.Modified;
            }
            List<OrderShare> shares = new List<OrderShare>();
            if (order.shop == "万龙体验中心" && order.type == "租赁")
            {
                double rentalAmount = 0;
                for(int i = 0; i < order.rentals.Count; i++)
                {
                    Rental rental  = order.rentals[i];
                    rentalAmount += (rental.totalRentalAmount - rental.totalDiscountAmount);
                }
                OrderShare share = new OrderShare()
                {
                    id = 0,
                    order_id = order.id,
                    relation_id = 4,
                    amount = rentalAmount/2,
                    valid = true,
                    create_date = DateTime.Now
                };
                await _db.orderShare.AddAsync(share);
                shares.Add(share);
            }
            await _db.SaveChangesAsync();
            return shares;
        }
    }
}