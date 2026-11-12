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
            if (orderShare.share_method == ShareWorkflowRules.Bank) return new List<PaymentShare>();
            if (orderShare.share_method != null)
            {
                var existing = await _db.paymentShare.AsNoTracking().Where(p => p.valid && p.share_id == orderShare.id).ToListAsync();
                if (existing.Count > 0) return existing;
            }
            // 2026-11-02 用户定：微信、支付宝的分账上限都是 30%，超过时照常生成分账记录，但标成 can_not_share，
            // 夜间不自动分，需要时手动分。判断按这个订单所有有效应分的合计（万龙 + 介绍人），不是单独一条；
            // 30% 的基数两个平台不同（微信按扣退款后的净额，支付宝按付款总额），见 WanlongShareRules.ShareBase。
            List<OrderPayment> payments = (await _db.orderPayment
                .Where(p => p.order_id == orderShare.order_id && p.valid == 1 && p.status == "支付成功"
                && p.pay_method != null && (p.pay_method.Trim() == "微信支付" || p.pay_method.Trim() == "支付宝")).Include(p => p.refunds)
                .AsNoTracking().ToListAsync()).OrderByDescending(p => WanlongShareRules.ShareBase(p)).ToList();
            if (orderShare.share_method == ShareWorkflowRules.Auto || orderShare.share_method == ShareWorkflowRules.Manual)
            {
                Models.Order sourceOrder = await _db.order.AsNoTracking().FirstAsync(o => o.id == orderShare.order_id);
                payments = payments.Where(p => ShareWorkflowRules.CanUsePayment(sourceOrder, p)).ToList();
            }

            List<PaymentShare> shares = new List<PaymentShare>();
            double sharedAmount = 0;
            orderShare.amount = Math.Round(orderShare.amount, 2);
            double totalShare = await _db.orderShare.Where(s => s.valid && s.order_id == orderShare.order_id && s.share_method != ShareWorkflowRules.Bank).SumAsync(s => s.amount);
            bool overFlow = ShareCapRules.IsOverCap(Math.Max(totalShare, orderShare.amount), payments);
            bool usedTicket = await ShareQueries.UsedTicketAsync(_db, orderShare.order_id);
            // 介绍人的收款方要看在这个渠道上有没有账号、绑没绑定；万龙那条不检查（它在支付宝的绑定不记在 share_relation_bind 里）
            OrderShareRelation? introducerRelation = null;
            if (await ShareQueries.IsIntroducerRelationAsync(_db, orderShare.relation_id))
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
            PaymentShare current;
            await using (var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
            {
                current = await _db.paymentShare.AsNoTracking().Include(p => p.payment).ThenInclude(p => p.refunds)
                    .Include(p => p.orderShare).ThenInclude(s => s.order)
                    .Include(p => p.orderShare).ThenInclude(s => s.relation).ThenInclude(r => r.binds)
                    .FirstOrDefaultAsync(p => p.id == share.id);
                if (current == null || !current.valid || current.submit_time != null || current.orderShare == null
                    || !current.orderShare.valid || current.orderShare.batch_id != null || current.orderShare.order == null)
                    return current ?? share;
                OrderShare orderShare = current.orderShare;
                bool systemTicket = orderShare.order.type == "雪票" && orderShare.amount == 1 && orderShare.share_method == null;
                // 已生成但尚未提交的员工雪票分账，也必须在提交时重新核对是否在职。
                if (systemTicket && orderShare.relation?.staff_id is int staffId
                    && !await _db.staff.AsNoTracking().AnyAsync(s => s.id == staffId && s.valid == 1))
                    return current;
                bool distributor = !systemTicket && (orderShare.share_method == ShareWorkflowRules.Auto
                    || await ShareQueries.IsIntroducerRelationAsync(_db, orderShare.relation_id));
                if (orderShare.share_method == ShareWorkflowRules.Manual || orderShare.share_method == ShareWorkflowRules.Bank)
                    return current;
                if (distributor && (orderShare.approved_at == null || orderShare.approved_by_staff_id == null
                    || (orderShare.plan_date != null && orderShare.plan_date.Value.Date > DateTime.Today)
                    || current.can_not_share || current.payment == null || !ShareWorkflowRules.CanUsePayment(orderShare.order, current.payment)
                    || current.amount > WanlongShareRules.ShareBase(current.payment) + 0.001
                    || orderShare.relation == null || ShareReceiverRules.Problem(orderShare.relation,
                        orderShare.relation.binds ?? new List<ShareRelationBind>(), current.payment.pay_method) != null))
                    return current;
                DateTime now = DateTime.Now;
                int claimed = await _db.paymentShare.Where(p => p.id == current.id && p.valid && p.submit_time == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(p => p.submit_time, now).SetProperty(p => p.update_date, now));
                if (claimed != 1) return current;
                current.submit_time = now;
                current.update_date = now;
                await tx.CommitAsync();
            }
            return await SubmitClaimedPayment(current);
        }

        [NonAction]
        public async Task<PaymentShare> SubmitClaimedPayment(PaymentShare share)
        {
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
                    else
                    {
                        share.success = false;
                        share.response_time = DateTime.Now;
                        share.response_content = "微信分账收款方绑定失败，尚未提交分账";
                        share.update_date = DateTime.Now;
                        _db.paymentShare.Update(share);
                        await _db.SaveChangesAsync();
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
        /// 夜间待提交记录：原万龙租赁系统分账，以及到期且经管理员审核的分销商自动分账。
        /// 先万龙（relation 4）、后分销商，同一次执行里按这个顺序提交。
        /// </summary>
        [NonAction]
        public async Task<List<PaymentShare>> GetDueWanlongShares(DateTime shareDate)
        {
            DateTime planLimit = ShareDateRules.DueLimit(shareDate);   // 介绍人分账的预计分账日期没到的，这次不提交（ShareDateRules.IsDue）
            List<int> distributorRelations = await ShareQueries.AllIntroducerRelationsAsync(_db);
            List<PaymentShare> shares = await _db.paymentShare.Include(p => p.payment).ThenInclude(p => p.refunds)
                .Include(s => s.orderShare).ThenInclude(o => o.order).ThenInclude(o => o.payments)
                .ThenInclude(p => p.refunds).Include(s => s.orderShare).ThenInclude(s => s.relation)
                .Where(s => s.valid && s.orderShare.valid && s.orderShare.batch_id == null && s.submit_time == null
                && (s.orderShare.plan_date == null || s.orderShare.plan_date <= planLimit)
                && s.can_not_share == false
                && ((!distributorRelations.Contains(s.orderShare.relation_id) && s.orderShare.share_method == null
                     && s.orderShare.order.type == "租赁" && s.orderShare.order.shop == "万龙体验中心"
                     && s.orderShare.order.close_date != null && s.orderShare.order.close_date.Value.Date <= shareDate.Date)
                    || (distributorRelations.Contains(s.orderShare.relation_id) && s.orderShare.approved_at != null
                        && s.orderShare.approved_by_staff_id != null
                        && (s.orderShare.share_method == ShareWorkflowRules.Auto || (s.orderShare.share_method == null && s.orderShare.auto_share)))))
                .AsNoTracking().ToListAsync();
            var distributorShares = shares.Where(s => distributorRelations.Contains(s.orderShare.relation_id)).ToList();
            var shareIds = distributorShares.Select(s => s.share_id).Distinct().ToList();
            if (shareIds.Count > 0)
            {
                var allRows = await _db.paymentShare.AsNoTracking().Include(p => p.payment).ThenInclude(p => p.refunds)
                    .Where(p => p.valid && shareIds.Contains(p.share_id)).ToListAsync();
                var relationIds = distributorShares.Select(s => s.orderShare.relation_id).Distinct().ToList();
                var relations = (await _db.orderShareRelation.AsNoTracking().Include(r => r.binds)
                    .Where(r => relationIds.Contains(r.id)).ToListAsync()).ToDictionary(r => r.id);
                var blocked = new HashSet<int>();
                foreach (var group in distributorShares.GroupBy(s => s.share_id))
                {
                    var sample = group.First();
                    var rows = allRows.Where(p => p.share_id == group.Key).ToList();
                    if (!relations.TryGetValue(sample.orderShare.relation_id, out var relation)
                        || Math.Abs(Math.Round(rows.Sum(p => p.amount), 2) - Math.Round(sample.orderShare.amount, 2)) > 0.001
                        || rows.Any(p => p.submit_time != null && p.success != true)
                        || rows.Where(p => p.submit_time == null).Any(p => p.can_not_share || p.payment == null
                            || !ShareWorkflowRules.CanUsePayment(sample.orderShare.order, p.payment)
                            || ShareReceiverRules.Problem(relation, relation.binds ?? new List<ShareRelationBind>(), p.payment.pay_method) != null)
                        || rows.Where(p => p.submit_time == null).GroupBy(p => p.payment_id)
                            .Any(g => g.Sum(p => p.amount) > WanlongShareRules.ShareBase(g.First().payment) + 0.001))
                        blocked.Add(group.Key);
                }
                shares = shares.Where(s => !blocked.Contains(s.share_id)).ToList();
            }
            return shares.OrderBy(s => s.orderShare.relation_id == WanlongShareRules.WanlongRelationId ? 0 : 1).ThenBy(s => s.id).ToList();
        }
        [HttpGet]
        public async Task<ActionResult<List<List<PaymentShare>>>> ExecuteWLShare(DateTime? date = null)
        {
            DateTime shareDate = date == null ? DateTime.Now.AddDays(-1).Date : (((DateTime)date).Date);
            if (shareDate > DateTime.Today.AddDays(-1)) shareDate = DateTime.Today.AddDays(-1);
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
                    try { shares[i] = await SharePayment(shares[i]); }
                    catch (Exception e) { Console.WriteLine("分账提交异常，待核对：" + shares[i].id + " " + e.GetType().Name); }
                }
            }
            await _db.SaveChangesAsync();
            return Ok(shares);
        }
        [NonAction]
        public Task<List<PaymentShare>> GetSystemTicketSharesAsync(int orderId)
        {
            return SystemTicketShareQuery(orderId).ToListAsync();
        }

        // 取了某一张票：只选这张票自己的那 1 元，再加上旧订单里没挂票（skipass_id 为空）的分账。
        [NonAction]
        public Task<List<PaymentShare>> GetSystemTicketSharesOfTicketAsync(int orderId, int skipassId)
        {
            return SystemTicketShareQuery(orderId)
                .Where(p => p.orderShare.skipass_id == skipassId || p.orderShare.skipass_id == null).ToListAsync();
        }

        private IQueryable<PaymentShare> SystemTicketShareQuery(int orderId)
        {
            return _db.paymentShare.AsNoTracking().Where(p => p.valid && p.submit_time == null
                && p.orderShare.valid && p.orderShare.batch_id == null && p.orderShare.order_id == orderId
                && p.orderShare.order.type == "雪票" && p.orderShare.amount == 1 && p.orderShare.share_method == null);
        }

        [NonAction]
        public async Task ExecuteShare(int orderId)
        {
            foreach (PaymentShare share in await GetSystemTicketSharesAsync(orderId)) await SharePayment(share);
        }

        // 取票按票分账：只分这张票的那 1 元（和旧订单没挂票的分账）。
        [NonAction]
        public async Task ExecuteShareOfTicket(int orderId, int skipassId)
        {
            foreach (PaymentShare share in await GetSystemTicketSharesOfTicketAsync(orderId, skipassId)) await SharePayment(share);
        }
        // 有待执行、待审核或结果不明的渠道分账时，必须保留微信冻结资金。
        private async Task<HashSet<int>> PendingDistributorPaymentIdsAsync(IReadOnlyCollection<int> paymentIds)
        {
            if (paymentIds.Count == 0) return new HashSet<int>();
            var rows = await _db.paymentShare.AsNoTracking()
                .Include(p => p.orderShare).ThenInclude(s => s.order)
                .Where(p => paymentIds.Contains(p.payment_id) && p.valid && p.success != true && p.response_time == null
                    && p.orderShare.valid && p.orderShare.batch_id == null)
                .ToListAsync();
            var legacyRelationIds = rows.Where(p => p.orderShare.share_method == null && p.orderShare.auto_share && !p.can_not_share)
                .Select(p => p.orderShare.relation_id).Distinct().ToList();
            var legacyIntroducers = legacyRelationIds.Count == 0
                ? new Dictionary<int, Introducer>()
                : await ShareQueries.IntroducersByRelationAsync(_db, legacyRelationIds);
            return rows.Where(p => p.orderShare.share_method == ShareWorkflowRules.Auto
                    || p.orderShare.share_method == ShareWorkflowRules.Manual
                    || (p.orderShare.share_method == null && p.orderShare.auto_share && !p.can_not_share
                        && !(p.orderShare.order.type == "雪票" && p.orderShare.amount == 1)
                        && legacyIntroducers.ContainsKey(p.orderShare.relation_id)))
                .Select(p => p.payment_id).ToHashSet();
        }

        [NonAction]
        public async Task<bool> HasPendingDistributorChannelSharesAsync(int paymentId)
            => (await PendingDistributorPaymentIdsAsync(new[] { paymentId })).Contains(paymentId);

        private async Task<HashSet<int>> ClosableWechatPaymentIdsAsync(List<OrderPayment> payments, DateTime now)
        {
            if (payments.Count == 0) return new HashSet<int>();
            var orderIds = payments.Select(p => p.order_id).Distinct().ToList();
            // 只以新流程的银行转账作为完成事件，历史清账批次不触发解冻。
            var bankEvents = await (from share in _db.orderShare.AsNoTracking()
                join batch in _db.shareBatch.AsNoTracking() on share.batch_id equals (int?)batch.id
                where share.valid && share.share_method == ShareWorkflowRules.Bank && orderIds.Contains(share.order_id)
                select new { share.order_id, batch.create_date }).ToListAsync();
            var latestBank = bankEvents.GroupBy(e => e.order_id).ToDictionary(g => g.Key, g => g.Max(e => e.create_date));
            var pending = await PendingDistributorPaymentIdsAsync(payments.Select(p => p.id).ToArray());
            return payments.Where(p => !pending.Contains(p.id) &&
                p.paymentShares.Select(s => s.response_time).Where(t => t != null)
                    .Concat(latestBank.TryGetValue(p.order_id, out var bankDate) ? new DateTime?[] { bankDate } : Array.Empty<DateTime?>())
                    .Any(t => t != null) &&
                p.paymentShares.Select(s => s.response_time).Where(t => t != null)
                    .Concat(latestBank.TryGetValue(p.order_id, out var date) ? new DateTime?[] { date } : Array.Empty<DateTime?>())
                    .Max()!.Value.Date <= now.Date.AddDays(-4))
                .Select(p => p.id).ToHashSet();
        }

        private Task<List<OrderPayment>> OpenWechatPaymentsAsync(int? paymentId = null)
            => _db.orderPayment
                .Include(p => p.paymentShares.Where(s => s.valid && s.success == true))
                .Where(p => (paymentId == null || p.id == paymentId) && p.valid == 1
                    && p.pay_method == "微信支付" && p.status == "支付成功" && p.need_share == 1 && p.share_close_date == null)
                .AsSplitQuery().AsNoTracking().ToListAsync();

        [NonAction]
        public async Task<bool> CanCloseWechatShareAsync(int paymentId, DateTime now)
            => (await ClosableWechatPaymentIdsAsync(await OpenWechatPaymentsAsync(paymentId), now)).Contains(paymentId);

        [HttpGet]
        public async Task CloseWepayShare()
        {
            List<OrderPayment> payments = await OpenWechatPaymentsAsync();
            var closable = await ClosableWechatPaymentIdsAsync(payments, DateTime.Now);
            TenpayController helper = new TenpayController(_db, _config, _http);
            foreach (var payment in payments.Where(p => closable.Contains(p.id)))
                await UnFreezeWepaySharePayment(payment, helper);
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
