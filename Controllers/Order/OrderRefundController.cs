using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Users;
using wechat_miniapp_base.Models;

namespace SnowmeetApi.Controllers.Order
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class OrderRefundController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        private IConfiguration _originConfig;
        public string _appId = "";

        public OrderRefundController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
        _originConfig = config;
            _config = config.GetSection("Settings");
        _appId = _config.GetSection("AppId").Value.Trim();

    }

    [HttpGet("{id}")]
        public async Task<OrderPaymentRefund> TenpayRefund(int paymentId, double amount, string sessionKey)
        {
            
            OrderPayment payment = await _db.OrderPayment.FindAsync(paymentId); 
            if (!payment.pay_method.Trim().Equals("微信支付"))
            {
                
                return null; 
            }
            

            string notify = payment.notify.Trim().Replace("OrderPayment/TenpayPaymentCallBack", "OrderRefund/TenpayRefundCallback"); 

            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value; 
            double refundedAmount = await _db.OrderPaymentRefund.Where(r => (r.payment_id == paymentId && r.state == 1)).SumAsync(s => s.amount); 
            
            
            if (refundedAmount >= payment.amount || refundedAmount + amount > payment.amount)
            {
                
                return null; 
            }
            

            OrderPaymentRefund refund = new OrderPaymentRefund()
            {
                order_id = payment.order_id,
                payment_id = payment.id,
                amount = amount,
                oper = "",
                state = 0,
                memo = "",
                notify_url = notify.Trim()

            }; 

            await _db.OrderPaymentRefund.AddAsync(refund); 
            await _db.SaveChangesAsync();
            //var client = new WechatTenpayClient(options);
            OrderOnlinesController orderHelper = new OrderOnlinesController(_db, _originConfig); 
            OrderOnline order = (await orderHelper.GetOrderOnline(payment.order_id, sessionKey)).Value; 

            WepayKey key = await _db.WepayKeys.FindAsync(payment.mch_id);

            var certManager = new InMemoryCertificateManager();
            var options = new WechatTenpayClientOptions()
            {
                MerchantId = key.mch_id.Trim(),
                MerchantV3Secret = "",
                MerchantCertificateSerialNumber = key.key_serial.Trim(),
                MerchantCertificatePrivateKey = key.private_key.Trim(),
                PlatformCertificateManager = certManager
            };

            var client = new WechatTenpayClient(options);
            var request = new CreateRefundDomesticRefundRequest()
            {
                OutTradeNumber = payment.out_trade_no.Trim(),
                OutRefundNumber = refund.id.ToString(),
                Amount = new CreateRefundDomesticRefundRequest.Types.Amount()
                {
                    Total = (int)Math.Round(payment.amount * 100, 0),
                    Refund = (int)(Math.Round(amount * 100))
                },
                Reason = user.miniAppOpenId,
                NotifyUrl = refund.notify_url.Trim()
            };
            var response = await client.ExecuteCreateRefundDomesticRefundAsync(request);
            try
            {
                string refundId = response.RefundId.Trim();
                if (refundId == null || refundId.Trim().Equals(""))
                {
                    return refund;
                }
                //refund.status = refundId;
                refund.refund_id = refundId;
                _db.Entry<OrderPaymentRefund>(refund).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                //await Response.WriteAsync("SUCCESS");
                return refund;
            }
            catch
            {
                refund.memo = response.ErrorMessage.Trim();
                _db.Entry<OrderPaymentRefund>(refund).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                return refund;
            }
            
        }
        /*

        // GET: api/OrderRefund
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderPaymentRefund>>> GetOrderPaymentRefund()
        {
            return await _context.OrderPaymentRefund.ToListAsync();
        }

        // GET: api/OrderRefund/5
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderPaymentRefund>> GetOrderPaymentRefund(int id)
        {
            var orderPaymentRefund = await _context.OrderPaymentRefund.FindAsync(id);

            if (orderPaymentRefund == null)
            {
                return NotFound();
            }

            return orderPaymentRefund;
        }

        // PUT: api/OrderRefund/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutOrderPaymentRefund(int id, OrderPaymentRefund orderPaymentRefund)
        {
            if (id != orderPaymentRefund.id)
            {
                return BadRequest();
            }

            _context.Entry(orderPaymentRefund).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!OrderPaymentRefundExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/OrderRefund
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<OrderPaymentRefund>> PostOrderPaymentRefund(OrderPaymentRefund orderPaymentRefund)
        {
            _context.OrderPaymentRefund.Add(orderPaymentRefund);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetOrderPaymentRefund", new { id = orderPaymentRefund.id }, orderPaymentRefund);
        }

        // DELETE: api/OrderRefund/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOrderPaymentRefund(int id)
        {
            var orderPaymentRefund = await _context.OrderPaymentRefund.FindAsync(id);
            if (orderPaymentRefund == null)
            {
                return NotFound();
            }

            _context.OrderPaymentRefund.Remove(orderPaymentRefund);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool OrderPaymentRefundExists(int id)
        {
            return _db.OrderPaymentRefund.Any(e => e.id == id);
        }
    }
}
