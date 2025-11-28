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
        public async Task<List<OrderShare>> CreateOrderShares(Models.Order order)
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
        [NonAction]
        public async Task<List<PaymentShare>> CreatePaymentShare(OrderShare orderShare)
        {
            List<OrderPayment> payments = await _db.orderPayment
                .Where(p => p.order_id == orderShare.order_id && p.valid == 1 && p.status == "支付成功"
                && (p.pay_method == "微信支付" || p.pay_method == "支付宝")).OrderBy(p => p.pay_method)
                .AsNoTracking().ToListAsync();
            List<PaymentShare> shares = new List<PaymentShare>();
            double sharedAmount = 0;
            orderShare.amount = Math.Round(orderShare.amount, 2);
            for(int i = 0; i < payments.Count && sharedAmount < orderShare.amount; i++)
            {
                //sharedAmount += Math.Min(orderShare.amount, payments)
                double currentAmount = Math.Min(orderShare.amount-sharedAmount, payments[i].amount * 0.29);
                currentAmount = Math.Round(currentAmount, 2);
                sharedAmount += currentAmount;
                sharedAmount = Math.Round(sharedAmount, 2);
                PaymentShare pShare = new PaymentShare()
                {
                    id = 0,
                    payment_id = payments[i].id,
                    share_id = orderShare.id,
                    amount = currentAmount,
                    valid = true,
                    out_trade_no = payments[i].out_trade_no.Trim() + "_FZ_" + (i+1).ToString().PadLeft(2, '0'),
                    create_date = DateTime.Now
                };
                await _db.paymentShare.AddAsync(pShare);
                shares.Add(pShare);
            }
            if (Math.Round(sharedAmount, 2) == Math.Round(orderShare.amount, 2))
            {
                orderShare.dealed = true;
                _db.orderShare.Entry(orderShare).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            return shares;
        }

        [HttpGet]
        public async Task<ActionResult<List<PaymentShare>>> GetPaymentShares(int shareId)
        {
            OrderShare share = await _db.orderShare.Where(s => s.id == shareId)
                .AsNoTracking().FirstOrDefaultAsync();
            return Ok(await CreatePaymentShare(share));
        }




        [HttpGet("{orderId}")]
        public async Task<ActionResult<List<OrderShare>>> CreateShare(int orderId)
        {
            OrderController _orderHelper = new OrderController(_db, _config, _http);
            Models.Order order = await _orderHelper.GetOrder(orderId);
            return Ok(await CreateOrderShares(order));
        }
    }
}