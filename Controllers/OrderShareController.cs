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
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class OrderShareController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        private readonly string _appId = "";
        public OrderShareController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _appId = _config.GetSection("Settings").GetSection("AppId").Value.Trim();
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
            for (int i = 0; i < paymentsWithShare.Count; i++)
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
            List<OrderShare> oriShares = await _db.orderShare.Where(s => s.valid && s.order_id == order.id
                && s.relation_id == WanlongShareRules.WanlongRelationId)
                .AsNoTracking().ToListAsync();
            for (int i = 0; i < oriShares.Count; i++)
            {
                oriShares[i].valid = false;
                _db.orderShare.Entry(oriShares[i]).State = EntityState.Modified;
            }
            List<OrderShare> shares = new List<OrderShare>();
            if (order.shop == "万龙体验中心" && order.type == "租赁")
            {
                double rentalAmount = 0;
                for (int i = 0; i < order.rentals.Count; i++)
                {
                    rentalAmount += WanlongShareRules.RentAfterDiscounts(order.rentals[i]);
                }
                OrderShare share = new OrderShare()
                {
                    id = 0,
                    order_id = order.id,
                    relation_id = WanlongShareRules.WanlongRelationId,
                    amount = rentalAmount / 2,
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
            // 2026-11-02 用户定：微信、支付宝的分账上限都是 30%，超过时照常生成分账记录，但标成 can_not_share，
            // 夜间不自动分，需要时手动分。判断按这个订单所有有效应分的合计（万龙 + 介绍人），不是单独一条；
            // 30% 的基数两个平台不同（微信按扣退款后的净额，支付宝按付款总额），见 WanlongShareRules.ShareBase。
            List<OrderPayment> payments = (await _db.orderPayment
                .Where(p => p.order_id == orderShare.order_id && p.valid == 1 && p.status == "支付成功"
                && (p.pay_method == "微信支付" || p.pay_method == "支付宝")).Include(p => p.refunds)
                .AsNoTracking().ToListAsync()).OrderByDescending(p => WanlongShareRules.ShareBase(p)).ToList();

            List<PaymentShare> shares = new List<PaymentShare>();
            double sharedAmount = 0;
            orderShare.amount = Math.Round(orderShare.amount, 2);
            double totalShare = await _db.orderShare.Where(s => s.valid && s.order_id == orderShare.order_id).SumAsync(s => s.amount);
            bool overFlow = ShareCapRules.IsOverCap(Math.Max(totalShare, orderShare.amount), payments);
            bool usedTicket = await ShareQueries.UsedTicketAsync(_db, orderShare.order_id);
            // 介绍人的收款方要看在这个渠道上有没有账号、绑没绑定；万龙那条不检查（它在支付宝的绑定不记在 share_relation_bind 里）
            OrderShareRelation? introducerRelation = null;
            if (await _db.introducer.AnyAsync(i => i.relation_id == orderShare.relation_id))
            {
                introducerRelation = await _db.orderShareRelation.Include(r => r.binds)
                    .AsNoTracking().FirstOrDefaultAsync(r => r.id == orderShare.relation_id);
            }
            List<int> paymentIds = payments.Select(p => p.id).ToList();
            // 同一笔付款上别的应分已经占用的额度；请求号序号 = 这笔付款上已有记录总行数（含作废的）+ 1，不回收
            List<PaymentShare> others = await _db.paymentShare
                .Where(s => paymentIds.Contains(s.payment_id) && s.valid && s.share_id != orderShare.id).AsNoTracking().ToListAsync();
            Dictionary<int, int> rowCounts = (await _db.paymentShare.Where(s => paymentIds.Contains(s.payment_id))
                .GroupBy(s => s.payment_id).Select(g => new { g.Key, n = g.Count() }).ToListAsync())
                .ToDictionary(x => x.Key, x => x.n);

            for (int i = 0; i < payments.Count && sharedAmount < orderShare.amount; i++)
            {
                OrderPayment payment = payments[i];
                double currentAmount;
                if (overFlow)
                {
                    // 超限：一条完整金额的记录，挂在基数最大的那笔付款上
                    currentAmount = orderShare.amount;
                }
                else
                {
                    // 没超限：按这笔付款剩余的容量拆分；退光了的、容量用完的付款不生成 0 元记录
                    double capacity = ShareCapRules.Capacity(payment, others.Where(o => o.payment_id == payment.id).Sum(o => o.amount));
                    if (capacity <= 0)
                    {
                        continue;
                    }
                    currentAmount = Math.Min(orderShare.amount - sharedAmount, capacity);
                }
                currentAmount = Math.Round(currentAmount, 2);
                sharedAmount += currentAmount;
                sharedAmount = Math.Round(sharedAmount, 2);
                int seq = rowCounts.GetValueOrDefault(payment.id) + 1;
                rowCounts[payment.id] = seq;
                string? problem = introducerRelation == null ? null
                    : ShareReceiverRules.Problem(introducerRelation, introducerRelation.binds ?? new List<ShareRelationBind>(), payment.pay_method);
                string? autoMemo = usedTicket ? ShareMemoRules.TicketMemo
                    : (problem ?? (overFlow ? ShareCapRules.OverCapMemo(payment) : null));
                PaymentShare pShare = new PaymentShare()
                {
                    id = 0,
                    payment_id = payment.id,
                    share_id = orderShare.id,
                    amount = currentAmount,
                    valid = true,
                    out_trade_no = payment.out_trade_no.Trim() + "_FZ_" + seq.ToString().PadLeft(2, '0'),
                    can_not_share = overFlow || usedTicket || problem != null,
                    memo = autoMemo,
                    create_date = DateTime.Now
                };
                await _db.paymentShare.AddAsync(pShare);
                shares.Add(pShare);
                if (overFlow)
                {
                    break;
                }
            }
            if (Math.Round(sharedAmount, 2) == Math.Round(orderShare.amount, 2))
            {
                orderShare.dealed = true;
                _db.orderShare.Entry(orderShare).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
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
        [NonAction]
        public async Task<PaymentShare> SharePayment(PaymentShare share)
        {
            if (share.submit_time != null)
            {
                return share;
            }
            if (share.payment == null)
            {
                share.payment = await _db.orderPayment.Where(p => p.id == share.payment_id)
                    .AsNoTracking().FirstOrDefaultAsync();
            }
            switch (share.payment.pay_method.Trim())
            {
                case "支付宝":
                    AliController _aliHelper = new AliController(_db, _config, _http);
                    share = await _aliHelper.Settle(share);
                    break;
                case "微信支付":
                    TenpayController _tenHelper = new TenpayController(_db, _config, _http);
                    if (await BindWepayShareRelation(share, _tenHelper))
                    {
                        share = await _tenHelper.Settle(share);    
                    }
                    break;
                default:
                    break;
            }
            return share;
        }
        [NonAction]
        public async Task<bool> BindWepayShareRelation(PaymentShare share, TenpayController _tHelper)
        {
            OrderShare orderShare = await _db.orderShare.Where(s => s.id == share.share_id)
                .Include(s => s.relation).ThenInclude(r => r.binds).AsNoTracking().FirstOrDefaultAsync();
            if (share.payment == null)
            {
                share.payment = await _db.orderPayment.Where(p => p.id == share.payment_id).AsNoTracking().FirstOrDefaultAsync();
            }
            if (share.payment == null || share.payment.pay_method != "微信支付")
            {
                return false;
            }
            ShareRelationBind bind = orderShare.relation.binds.
                Where(b => b.pay_method == "微信支付" && b.wepay_key_id == share.payment.mch_id)
                .FirstOrDefault();
            if (bind == null)
            {
                var client = await _tHelper.GetClient((int)share.payment.mch_id);
                var req = new AddProfitSharingReceiverRequest()
                {
                    AppId = _appId,
                    Type = orderShare.relation.wepay_account_type,
                    Account = orderShare.relation.wepay_account_num,
                    RelationType = "USER"
                };
                if (orderShare.relation.wepay_account_type == "MERCHANT_ID")
                {
                    req.Name = orderShare.relation.wepay_account_name;
                    req = client.EncryptRequestSensitiveProperty(req);
                }
                var res = await client.ExecuteAddProfitSharingReceiverAsync(req);
                string ret = res.IsSuccessful().ToString().ToLower();
                if (ret.Equals("true"))
                {
                   ShareRelationBind newBind = new ShareRelationBind()
                   {
                        id = 0,
                        share_relation_id = orderShare.relation.id,
                        pay_method = "微信支付",
                        wepay_key_id = share.payment.mch_id,
                        ali_account_id = null,
                        valid = true,
                        create_date = DateTime.Now
                   };
                   await _db.shareRelationBind.AddAsync(newBind);
                   await _db.SaveChangesAsync();
                   return true;
                }
                else
                {
                    return false;
                }
            }
            else 
            {
                return bind.valid;
            }
        }
        [NonAction]
        public async Task<ActionResult<PaymentShare>> SharePayment(int paymentShareId)
        {
            PaymentShare share = await _db.paymentShare.Where(p => p.id == paymentShareId)
                .Include(p => p.payment)
                .Include(s => s.orderShare).ThenInclude(o => o.order).ThenInclude(o => o.payments)
                .ThenInclude(p => p.refunds)
                .Include(s => s.orderShare).ThenInclude(s => s.relation)
                .AsNoTracking().FirstOrDefaultAsync();
            return Ok(await SharePayment(share));
        }
        /// <summary>
        /// 夜间要提交的分账记录：有效、未提交、没标手动、关单日 <= shareDate 的万龙体验中心租赁单。
        /// 先万龙（relation 4）、后介绍人，同一次执行里按这个顺序提交。
        /// </summary>
        [NonAction]
        public async Task<List<PaymentShare>> GetDueWanlongShares(DateTime shareDate)
        {
            List<PaymentShare> shares = await _db.paymentShare.Include(p => p.payment)
                .Include(s => s.orderShare).ThenInclude(o => o.order).ThenInclude(o => o.payments)
                .ThenInclude(p => p.refunds).Include(s => s.orderShare).ThenInclude(s => s.relation)
                .Where(s => s.valid && s.orderShare.valid && s.submit_time == null
                && ((DateTime)s.orderShare.order.close_date).Date <= shareDate.Date //&& s.orderShare.order.hide == false
                && s.orderShare.order.type == "租赁" && s.orderShare.order.shop == "万龙体验中心" && s.can_not_share == false)
                .AsNoTracking().ToListAsync();
            return shares.OrderBy(s => s.orderShare.relation_id == WanlongShareRules.WanlongRelationId ? 0 : 1).ThenBy(s => s.id).ToList();
        }
        [HttpGet]
        public async Task<ActionResult<List<List<PaymentShare>>>> ExecuteWLShare(DateTime? date = null)
        {
            DateTime shareDate = date == null ? DateTime.Now.AddDays(-1).Date : (((DateTime)date).Date);
            List<PaymentShare> shares = await GetDueWanlongShares(shareDate);
            for (int i = 0; i < shares.Count; i++)
            {
                //Console.WriteLine("Sharing PaymentShare ID: " + shares[i].id);
                if (shares[i].orderShare.order.hide)
                {
                    shares[i].can_not_share = true;
                    shares[i].memo = "订单已隐藏，无法分账";
                    shares[i].update_date = DateTime.Now;
                    _db.paymentShare.Entry(shares[i]).State = EntityState.Modified;
                }
                else
                {
                    shares[i] = await SharePayment(shares[i]);
                }
            }
            await _db.SaveChangesAsync();
            return Ok(shares);
        }
        [HttpGet("{orderId}")]
        public async Task ExecuteShare(int orderId)
        {
            List<OrderShare> shares = await _db.orderShare.Where(s => s.order_id == orderId)
                .Include(s => s.paymentShares).AsNoTracking().ToListAsync();
            for (int i = 0; i < shares.Count; i++)
            {
                for (int j = 0; shares[i].paymentShares != null && j < shares[i].paymentShares.Count; j++)
                {
                    PaymentShare sp = shares[i].paymentShares[j];
                    if (sp.submit_time == null)
                    {
                        await SharePayment(sp);
                    }
                }
            }
        }
        [HttpGet]
        public async Task CloseWepayShare()
        {
            TenpayController _tHelper = new TenpayController(_db, _config, _http);
            List<OrderPayment> payments = await _db.orderPayment
                .Include(p => p.paymentShares.Where(s => s.valid && s.success != null && (bool)s.success).OrderByDescending(s => s.submit_time))
                .Where(p => p.pay_method == "微信支付" && p.status == "支付成功" && p.need_share == 1 && p.share_close_date == null)
                .AsSplitQuery().AsNoTracking().ToListAsync();
            for (int i = 0; i < payments.Count; i++)
            {
                OrderPayment payment = payments[i];
                if (payment.paymentShares != null && payment.paymentShares.Count > 0 && payment.paymentShares[0].response_time != null
                    && ((DateTime)payment.paymentShares[0].response_time).Date <= DateTime.Now.Date.AddDays(-4))
                {
                    await UnFreezeWepaySharePayment(payment, _tHelper);
                }
            }
        }
        [NonAction]
        public async Task UnFreezeWepaySharePayment(OrderPayment payment, TenpayController _tHelper, string description = "完成关闭")
        {
            WechatTenpayClient client = await _tHelper.GetClient((int)payment.mch_id);
            var req = new SetProfitSharingOrderUnfrozenRequest()
            {
                TransactionId = payment.wepay_trans_id,
                OutOrderNumber = payment.out_trade_no.Trim(),
                Description = description
            };
            var res = await client.ExecuteSetProfitSharingOrderUnfrozenAsync(req);
            if (res.ErrorCode == null)
            {
                payment.share_close_date = DateTime.Now;
                payment.update_date = DateTime.Now;
                _db.orderPayment.Entry(payment).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
        }
    }
}