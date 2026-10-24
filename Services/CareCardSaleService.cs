using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services;

public class CareCardSelection
{
    public int productId { get; set; }
    public int quantity { get; set; } = 1;
}

public class CareCardSaleRequest
{
    public int orderId { get; set; }
    public List<CareCardSelection> selections { get; set; } = new();
}

public class CareCardPreviewItem
{
    public int lineNo { get; set; }
    public int productId { get; set; }
    public string name { get; set; } = "";
    public decimal price { get; set; }
    public int? total { get; set; }
    public int? projectCount { get; set; }
    public bool requiresBinding { get; set; }
    public string serviceLabel { get; set; } = "";
    public List<int> careIds { get; set; } = new();
    public int punches => careIds.Count;
    public int? remaining => total == null ? null : total - punches;
}

public class CareCardPreview
{
    public int careOrderId { get; set; }
    public decimal amount { get; set; }
    public List<CareCardPreviewItem> items { get; set; } = new();
}

// 与原支付流水保持单订单关系，仅处理从养护开单发起的售卡订单。
public class CareCardSaleService
{
    private readonly ApplicationDBContext db;
    private readonly IConfiguration config;
    private readonly IHttpContextAccessor http;
    public CareCardSaleService(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http)
    { this.db = db; this.config = config; this.http = http; }

    // 复用订单来源字段定位由养护草稿创建的零售单，不增加表或列。
    public const string OrderSource = "care_card_reception";

    public static Task<Models.Order?> GetLinkedRetailAsync(ApplicationDBContext db, int careOrderId, bool includeDraft = false) =>
        db.order.AsNoTracking().Where(o => o.type == "零售" && o.order_source == OrderSource
            && o.source_order_no == careOrderId.ToString() && o.closed == 0
            && (includeDraft || (o.valid == 1 && o.recepting == 0))).OrderByDescending(o => o.id).FirstOrDefaultAsync();

    public static Task<bool> IsLinkedRetailAsync(ApplicationDBContext db, int orderId) =>
        db.order.AnyAsync(o => o.id == orderId && o.type == "零售" && o.order_source == OrderSource
            && o.valid == 1 && o.recepting == 0);

    public static async Task<bool> IsLinkedCareAsync(ApplicationDBContext db, int orderId) =>
        await GetLinkedRetailAsync(db, orderId) != null;

    public static async Task<bool> HasDraftAsync(ApplicationDBContext db, int orderId)
    {
        var retail = await GetLinkedRetailAsync(db, orderId, true);
        return retail != null && await db.retail.AnyAsync(r => r.order_id == retail.id && r.valid == 1);
    }

    public static async Task<Models.Order?> LinkedCareAsync(ApplicationDBContext db, Models.Order retail)
    {
        if (retail.type != "零售" || retail.order_source != OrderSource || retail.valid != 1 || retail.recepting != 0) return null;
        if (!int.TryParse(retail.source_order_no, out int careId)) throw new InvalidOperationException("购卡订单来源不完整");
        var care = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == careId && o.type == "养护");
        if (care == null) throw new InvalidOperationException("关联养护订单不存在");
        care.wechat_unverified |= retail.wechat_unverified;
        return care;
    }

    public static async Task<CareCardSaleInfo?> GetInfoAsync(ApplicationDBContext db, Models.Order order)
    {
        var retail = order.type == "养护" ? await GetLinkedRetailAsync(db, order.id)
            : order.type == "零售" && order.order_source == OrderSource && order.valid == 1 && order.recepting == 0 ? order : null;
        if (retail == null || !int.TryParse(retail.source_order_no, out int careId)) return null;
        return new CareCardSaleInfo { careOrderId = careId, retailOrderId = retail.id, completed = retail.dealed == 1 };
    }

    // SQL Server跨进程锁；SQLite隔离测试由写事务串行化。不能只用进程内锁保护支付回调。
    public static async Task LockAsync(ApplicationDBContext db, string resource)
    {
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @result int;
EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
IF @result < 0 THROW 51010, N'操作繁忙，请稍后重试', 1;");
    }

    public async Task<CareCardPreview> PreviewAsync(CareCardSaleRequest request)
    {
        var order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == request.orderId && o.type == "养护");
        if (order == null || order.valid != 0 || order.recepting != 1) throw new InvalidOperationException("请先保存养护草稿");
        if (request.selections != null && request.selections.Count == 0) return new CareCardPreview { careOrderId = order.id };
        if (order.member_id == null) throw new InvalidOperationException("购卡前请先匹配会员");
        var cares = await db.care.AsNoTracking().Where(c => c.order_id == order.id).OrderBy(c => c.id).ToListAsync();
        if (cares.Count == 0) throw new InvalidOperationException("请先添加装备");
        if (request.selections == null || request.selections.Count > 20
            || request.selections.Any(s => s.quantity <= 0 || s.quantity > 20)
            || request.selections.Sum(s => s.quantity) > 20) throw new InvalidOperationException("购卡数量不合法（最多20张）");
        var result = new CareCardPreview { careOrderId = order.id };
        foreach (var selection in request.selections.OrderBy(s => s.productId))
        {
            var product = await db.product.AsNoTracking().FirstOrDefaultAsync(p => p.id == selection.productId && p.valid == 1 && p.on_shelves == 1);
            if (product == null) throw new InvalidOperationException("卡商品不存在或已下架");
            var category = await db.category.AsNoTracking().FirstOrDefaultAsync(c => c.code == product.category_code && c.valid == 1);
            if (category == null || category.biz_type != "养护" || (category.name != "次卡" && category.name != "季卡"))
                throw new InvalidOperationException("只能选择养护次卡或季卡");
            int? total = category.name == "季卡" ? null : product.punch_total;
            decimal price = Math.Round((decimal)product.sale_price, 2, MidpointRounding.AwayFromZero);
            if (price <= 0 || (category.name == "次卡" && (total == null || total <= 0)))
                throw new InvalidOperationException("卡商品价格或总次数配置不正确");
            for (int n = 0; n < selection.quantity; n++) result.items.Add(new CareCardPreviewItem
            {
                lineNo = result.items.Count + 1, productId = product.id, name = product.name,
                price = price, total = total, projectCount = product.care_project_count,
                requiresBinding = CareCardRules.RequiresEquipmentBinding(total, product.name)
            });
        }
        if (result.items.Count == 0) return result; // 清空已选卡，不改变普通养护。
        // 优先分配覆盖范围更窄的卡，结果明确回显到每张卡，提交使用同一确定性算法。
        foreach (var care in cares.Where(c => !c.use_card && string.IsNullOrWhiteSpace(c.ticket_code)))
        {
            var item = result.items.Where(i => (i.total == null ? i.punches < 1 : i.punches < i.total)
                && CareCardRules.Covers(i, CareCardRules.PreviewCare(i, care)))
                .OrderBy(i => i.projectCount ?? (i.name.Contains("双项") ? 2 : 1)).ThenBy(i => i.lineNo).FirstOrDefault();
            if (item == null) throw new InvalidOperationException("所选卡不能覆盖全部装备项目或次数不足，请调整卡类或养护项目");
            if (item.requiresBinding && !CareController.HasFullEquipInfo(care)) throw new InvalidOperationException("打蜡季卡需要完整的装备类型、品牌和长度");
            item.careIds.Add(care.id);
        }
        foreach (var item in result.items)
            item.serviceLabel = string.Join("；", cares.Where(c => item.careIds.Contains(c.id)).Select(c =>
            {
                var plan = CareCardRules.PreviewCare(item, c);
                return plan.free_wax == 1 ? "机打蜡" : (plan.need_edge == 1 && plan.need_wax == 1 ? "修刃＋热蜡" : plan.need_edge == 1 ? "修刃" : "热蜡");
            }).Distinct());
        await ValidateExistingBenefitsAsync(order, cares.Where(c => c.use_card || !string.IsNullOrWhiteSpace(c.ticket_code)).ToList());
        result.amount = result.items.Sum(i => i.price);
        return result;
    }

    private async Task ValidateExistingBenefitsAsync(Models.Order order, List<Care> cares)
    {
        var helper = new CareController(db, config, http);
        var seasonIds = new HashSet<int>();
        foreach (var care in cares)
        {
            PunchCard card = null;
            Ticket ticket = null;
            if (care.use_card)
            {
                card = await db.punchCard.AsNoTracking().FirstOrDefaultAsync(c => c.id == care.card_id && c.member_id == order.member_id && !c.is_refund);
                if (card == null || (card.total != null && card.remaining <= 0)) throw new InvalidOperationException("已有卡不可用");
                var priorCount = cares.Count(c => c.use_card && c.card_id == card.id);
                if (card.total != null && card.remaining < priorCount) throw new InvalidOperationException("已有卡剩余次数不足");
                if (card.total == null && (!seasonIds.Add(card.id) || await db.punchCardUsed.AnyAsync(u => u.card_id == card.id && u.valid && u.create_date >= DateTime.Today)))
                    throw new InvalidOperationException("该季卡今天已使用");
                if (CareCardRules.RequiresEquipmentBinding(card.total, card.card_name) && !CareController.IsSeasonCardUnbound(card)
                    && (card.equip_type != care.equipment || card.equip_brand != care.brand || card.equip_scale != care.scale))
                    throw new InvalidOperationException("打蜡季卡绑定装备不匹配");
            }
            if (!string.IsNullOrWhiteSpace(care.ticket_code))
            {
                ticket = await db.ticket.Include(t => t.template).ThenInclude(t => t.productTicketTemplates).ThenInclude(t => t.product)
                    .FirstOrDefaultAsync(t => t.code == care.ticket_code && t.valid == 1 && t.used == 0 && t.member_id == order.member_id);
                if (ticket == null || cares.Count(c => c.ticket_code == care.ticket_code) != 1) throw new InvalidOperationException("优惠券不可用或重复选择");
            }
            var (charge, discount) = await helper.CalcCharge(order.shop, care, ticket, card);
            if (Math.Round(charge + care.repair_charge - Math.Max(care.discount, discount) - care.ticket_discount, 2) != 0)
                throw new InvalidOperationException("本次购卡关联养护须由权益全额覆盖，额外收费请使用普通养护开单");
        }
    }

    // 每张拟购卡的临时使用安排放入现有 retail.memo，支付业务完成后清空。
    // 售价、商品、发卡来源和实际用量始终由原业务表记录。
    private sealed class PendingCard
    {
        public int version { get; set; } = 1;
        public CareCardPreviewItem item { get; set; } = new();
    }

    private static CareCardPreviewItem ReadPlan(Retail retail)
    {
        try
        {
            var plan = JsonConvert.DeserializeObject<PendingCard>(retail.memo);
            if (plan?.version != 1 || plan.item == null || plan.item.productId != retail.product_id
                || plan.item.price != Math.Round((decimal)retail.deal_price, 2) || string.IsNullOrWhiteSpace(plan.item.name)
                || plan.item.careIds == null || plan.item.careIds.Distinct().Count() != plan.item.careIds.Count)
                throw new InvalidOperationException("待付购卡安排不完整，请联系管理员核对");
            return plan.item;
        }
        catch (JsonException) { throw new InvalidOperationException("待付购卡安排不完整，请联系管理员核对"); }
    }

    private async Task<CareCardPreview> ReadPreviewAsync(Models.Order retailOrder, int careOrderId)
    {
        var rows = await db.retail.AsNoTracking().Where(r => r.order_id == retailOrder.id && r.valid == 1).OrderBy(r => r.id).ToListAsync();
        var result = new CareCardPreview { careOrderId = careOrderId, amount = rows.Sum(r => Math.Round((decimal)r.deal_price, 2)) };
        foreach (var row in rows)
        {
            if (retailOrder.dealed != 1) { result.items.Add(ReadPlan(row)); continue; }
            var card = await db.punchCard.AsNoTracking().FirstOrDefaultAsync(c => c.id == row.punch_card_id && c.source_retail_id == row.id);
            if (card == null) throw new InvalidOperationException("未找到已发出的会员卡，请联系管理员核对");
            var uses = await db.punchCardUsed.AsNoTracking().Where(u => u.card_id == card.id && u.order_id == careOrderId
                && u.biz_type == "养护" && u.valid).ToListAsync();
            var careIds = uses.Select(u => u.biz_id).ToList();
            var cares = await db.care.AsNoTracking().Where(c => careIds.Contains(c.id)).ToListAsync();
            result.items.Add(new CareCardPreviewItem { lineNo = result.items.Count + 1, productId = row.product_id.Value,
                name = card.card_name, price = Math.Round((decimal)row.deal_price, 2), total = card.total,
                projectCount = card.care_project_count, requiresBinding = CareCardRules.RequiresEquipmentBinding(card.total, card.card_name),
                careIds = careIds, serviceLabel = string.Join("；", cares.Select(ServiceLabel).Distinct()) });
        }
        return result;
    }

    private static string ServiceLabel(Care care) => care.free_wax == 1 ? "机打蜡"
        : care.need_edge == 1 && care.need_wax == 1 ? "修刃＋热蜡" : care.need_edge == 1 ? "修刃" : "热蜡";

    private static bool SamePlan(CareCardPreviewItem a, CareCardPreviewItem b) =>
        a.productId == b.productId && a.price == b.price && a.name == b.name && a.total == b.total
        && a.projectCount == b.projectCount && a.requiresBinding == b.requiresBinding && a.serviceLabel == b.serviceLabel
        && a.careIds.OrderBy(x => x).SequenceEqual(b.careIds.OrderBy(x => x));

    public async Task<CareCardPreview> SaveDraftAsync(CareCardSaleRequest request)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync(db, "care-card-order:" + request.orderId);
        var preview = await PreviewAsync(request);
        var existing = await GetLinkedRetailAsync(db, request.orderId, true);
        if (existing != null && existing.recepting != 1) throw new InvalidOperationException("订单已提交，请到原结算页继续支付");
        if (existing == null && preview.items.Count == 0) { await tx.CommitAsync(); return preview; }
        var careOrder = await db.order.AsNoTracking().FirstAsync(o => o.id == request.orderId);
        var retailOrder = existing == null ? new Models.Order { type = "零售", sub_type = "养护购卡", valid = 0, recepting = 1,
            order_source = OrderSource, source_order_no = request.orderId.ToString() }
            : await db.order.AsTracking().FirstAsync(o => o.id == existing.id);
        retailOrder.shop = careOrder.shop; retailOrder.member_id = careOrder.member_id;
        retailOrder.staff_id = careOrder.staff_id; retailOrder.is_test = careOrder.is_test;
        retailOrder.name = careOrder.name; retailOrder.gender = careOrder.gender; retailOrder.cell = careOrder.cell;
        retailOrder.contact_name = careOrder.contact_name; retailOrder.contact_num = careOrder.contact_num;
        retailOrder.contact_gender = careOrder.contact_gender; retailOrder.wechat_unverified = careOrder.wechat_unverified;
        retailOrder.total_amount = (double)preview.amount; retailOrder.paying_amount = (double)preview.amount;
        retailOrder.update_date = DateTime.Now;
        if (existing == null) { db.order.Add(retailOrder); await db.SaveChangesAsync(); }
        var old = await db.retail.AsTracking().Where(r => r.order_id == retailOrder.id).ToListAsync();
        if (old.Any(r => r.punch_card_id != null)) throw new InvalidOperationException("本单已发卡，不能覆盖购卡安排");
        db.retail.RemoveRange(old); await db.SaveChangesAsync();
        foreach (var item in preview.items)
            db.retail.Add(new Retail { order_id = retailOrder.id, product_id = item.productId, sale_price = (double)item.price,
                deal_price = (double)item.price, order_type = "卡类销售", retail_type = "养护卡",
                memo = JsonConvert.SerializeObject(new PendingCard { item = item }), valid = 1 });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return preview;
    }

    public async Task<object> GetDraftAsync(int orderId)
    {
        var retail = await GetLinkedRetailAsync(db, orderId, true);
        var preview = retail == null ? new CareCardPreview { careOrderId = orderId } : await ReadPreviewAsync(retail, orderId);
        return new { careOrderId = orderId, retailOrderId = retail?.recepting == 0 ? (int?)retail.id : null,
            state = retail == null || retail.recepting == 1 ? "draft" : retail.dealed == 1 ? "completed" : "pending",
            preview, selections = preview.items.GroupBy(i => i.productId).Select(g => new CareCardSelection { productId = g.Key, quantity = g.Count() }).ToList() };
    }

    public async Task<int> SubmitAsync(int orderId, int staffId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync(db, "care-card-order:" + orderId);
        var existing = await GetLinkedRetailAsync(db, orderId, true);
        if (existing == null) throw new InvalidOperationException("请先选择并保存卡类");
        if (existing.recepting == 0) return existing.id;
        var saved = await ReadPreviewAsync(existing, orderId);
        if (saved.items.Count == 0) throw new InvalidOperationException("请先添加卡类");
        var request = new CareCardSaleRequest { orderId = orderId,
            selections = saved.items.GroupBy(i => i.productId).Select(g => new CareCardSelection { productId = g.Key, quantity = g.Count() }).ToList() };
        var preview = await PreviewAsync(request);
        if (preview.items.Count != saved.items.Count || preview.items.Where((p, n) => !SamePlan(p, saved.items[n])).Any())
            throw new InvalidOperationException("商品价格、权益或装备项目已变化，请重新试算并确认");
        var careOrder = await db.order.AsTracking().FirstAsync(o => o.id == orderId);
        var retailOrder = await db.order.AsTracking().FirstAsync(o => o.id == existing.id);
        if (careOrder.member_id != retailOrder.member_id) throw new InvalidOperationException("顾客已改变，请重新选择卡类");
        retailOrder.valid = 1; retailOrder.recepting = 0; retailOrder.staff_id = staffId;
        retailOrder.wechat_unverified = careOrder.wechat_unverified;
        careOrder.valid = 1; careOrder.recepting = 0; careOrder.total_amount = 0; careOrder.paying_amount = 0;
        careOrder.dealed = 0; careOrder.pay_with_deposit = false; careOrder.staff_id = staffId;
        var helper = new OrderController(db, config, http);
        await helper.GenerateOrderCode(retailOrder); await helper.GenerateOrderCode(careOrder);
        var cares = await db.care.AsTracking().Where(c => c.order_id == orderId).ToListAsync();
        foreach (var care in cares)
        {
            care.valid = 1;
            var plan = preview.items.SingleOrDefault(i => i.careIds.Contains(care.id));
            if (plan == null) continue;
            CareCardRules.ApplyDefaultServices(plan, care);
            care.use_card = true; care.card_id = null; care.common_charge = 0; care.discount = 0; care.ticket_discount = 0;
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return retailOrder.id;
    }

    public async Task<bool> CompletePaidAsync(int retailOrderId, int? paymentId)
    {
        var retail = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == retailOrderId);
        var linkedCare = retail == null ? null : await LinkedCareAsync(db, retail);
        if (linkedCare == null) return false;
        await using var tx = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        try
        {
            await LockAsync(db, "care-card-order:" + linkedCare.id);
            var order = await db.order.AsTracking().FirstAsync(o => o.id == retailOrderId);
            await db.Entry(order).ReloadAsync();
            if (order.dealed == 1) return true;
            var paid = await db.orderPayment.AsNoTracking().Where(p => p.order_id == retailOrderId && p.valid == 1
                && p.status == "支付成功" && p.is_debt == 0).ToListAsync();
            if (paid.Count == 0 || paid.Sum(p => Math.Round((decimal)p.amount, 2)) < Math.Round((decimal)order.total_amount, 2))
                throw new InvalidOperationException("购卡款尚未收齐");
            var careOrder = await db.order.AsTracking().FirstAsync(o => o.id == linkedCare.id);
            var memberId = order.member_id ?? throw new InvalidOperationException("购卡订单未匹配会员");
            careOrder.member_id = memberId;
            var payment = paid.FirstOrDefault(p => p.id == paymentId) ?? paid.First();
            bool verified = OrderVerifiedRules.ResolveOnPaid(careOrder.wechat_unverified || order.wechat_unverified,
                memberId, memberId, payment.pay_method, payment.is_proxy_pay, payment.member_id);
            order.wechat_unverified = careOrder.wechat_unverified = verified;
            var rows = await db.retail.AsTracking().Where(r => r.order_id == order.id && r.valid == 1).ToListAsync();
            var plans = rows.ToDictionary(r => r.id, ReadPlan);
            if (rows.Count == 0) throw new InvalidOperationException("购卡销售明细为空");
            await RetailCardService.GrantAsync(db, order, plans);
            foreach (var row in rows)
            {
                foreach (int careId in plans[row.id].careIds)
                {
                    var care = await db.care.AsTracking().FirstOrDefaultAsync(c => c.id == careId && c.order_id == careOrder.id);
                    if (care == null) throw new InvalidOperationException("待付购卡安排中的装备已不存在");
                    if (care.is_cancel) continue;
                    care.card_id = row.punch_card_id; care.use_card = true; care.card_name = plans[row.id].name;
                }
            }
            await db.SaveChangesAsync();
            await new CareController(db, config, http).EffectCareOrder(careOrder.id);
            order.dealed = careOrder.dealed = 1;
            order.paying_amount = careOrder.paying_amount = null;
            order.pay_flow_status = careOrder.pay_flow_status = "已支付";
            // 后续查询、防重和取消只依赖原订单、发卡来源及核销记录，临时安排到此即清理。
            foreach (var row in rows) { row.memo = ""; row.update_date = DateTime.Now; }
            await db.SaveChangesAsync();
            await new OrderController(db, config, http).SupplementMemberProfileFromOrder(order);
            if (tx != null) await tx.CommitAsync();
            return true;
        }
        catch
        {
            if (tx != null) { await tx.RollbackAsync(); db.ChangeTracker.Clear(); }
            throw;
        }
    }

    public static async Task<bool> ContainsCardSaleAsync(ApplicationDBContext db, int orderId) =>
        await db.retail.AnyAsync(r => r.order_id == orderId && r.valid == 1 && (r.punch_card_id != null
            || db.product.Any(p => p.id == r.product_id && db.category.Any(c => c.code == p.category_code && (c.name == "次卡" || c.name == "季卡")))))
        || await IsLinkedRetailAsync(db, orderId);

    public static async Task<string?> PaymentErrorAsync(ApplicationDBContext db, Models.Order order,
        double? amount, string method, int? payerId = null, bool checkPayer = false)
    {
        if (order.type == "养护" && await IsLinkedCareAsync(db, order.id)) return "该养护单由关联购卡订单支付后自动核销，请勿重复收款";
        var care = await LinkedCareAsync(db, order);
        if (care == null) return null;
        if (order.closed != 0 || order.dealed != 0) return "购卡订单已支付或已关闭";
        double alreadyPaid = await db.orderPayment.Where(p => p.order_id == order.id && p.valid == 1 && p.status == "支付成功" && p.is_debt == 0).SumAsync(p => p.amount);
        if (Math.Round(alreadyPaid, 2) >= Math.Round(order.total_amount, 2)) return "购卡款已收到，正在处理发卡核销，请勿重复收款";
        if (amount != null && Math.Round(amount.Value, 2) != Math.Round(order.total_amount, 2)) return "购卡须一次付清全部卡款";
        if (method == "储值支付" || method == "挂账") return "购卡暂不支持储值或挂账";
        if (!care.wechat_unverified && (method != "微信支付" || (checkPayer && payerId != care.member_id)))
            return "请先核验顾客本人，或由订单会员本人微信支付";
        return null;
    }

    // 调用方须把取消标记、任务状态与本方法置于同一事务，避免“返了次数但取消失败”。
    public static async Task ReverseCareUseAsync(ApplicationDBContext db, Care care, int staffId, string reason)
    {
        await LockAsync(db, "care-card-order:" + care.order_id);
        var usages = await db.punchCardUsed.AsTracking().Where(u => u.biz_type == "养护" && u.biz_id == care.id && u.valid).ToListAsync();
        foreach (int cardId in usages.Select(u => u.card_id).Distinct().OrderBy(x => x))
        {
            await LockAsync(db, "punch-card:" + cardId);
            var card = await db.punchCard.AsTracking().FirstAsync(c => c.id == cardId);
            await db.Entry(card).ReloadAsync();
            int count = usages.Where(u => u.card_id == cardId).Sum(u => u.punch_count);
            int before = card.punches ?? 0;
            if (card.total != null) card.punches = CareCardRules.RestoredPunches(before, count);
            card.update_date = DateTime.Now;
            foreach (var usage in usages.Where(u => u.card_id == cardId)) { usage.valid = false; usage.update_date = DateTime.Now; }
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("punch_card", "punches", cardId, "取消养护返次", null, staffId,
                before.ToString(), (card.punches ?? 0).ToString(), "care=" + care.id + "；撤销" + count + "次；" + reason));
        }
    }
}
