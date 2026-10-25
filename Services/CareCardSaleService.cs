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

public class CareCardProject
{
    public int careId { get; set; }
    public int need_edge { get; set; }
    public int need_wax { get; set; }
    public int need_unwax { get; set; }
    public int free_wax { get; set; }
    public string? edge_degree { get; set; }
    public double common_charge { get; set; }
    public double repair_charge { get; set; }
    public double discount { get; set; }
    public decimal amount { get; set; }
}

public class CareCardPreview
{
    public int careOrderId { get; set; }
    public decimal amount { get; set; }
    public decimal cardAmount => items.Sum(i => i.price);
    public decimal careAmount { get; set; }
    public List<CareCardPreviewItem> items { get; set; } = new();
    public List<CareCardProject> projects { get; set; } = new();
    public List<string> errors { get; set; } = new();
    public bool canCheckout => items.Count > 0 && errors.Count == 0;
    public string serviceMode => items.Count == 0 ? "" : CareCardRules.ServiceMode(items[0]);
}

// 与原支付流水保持单订单关系，仅处理从养护开单发起的售卡订单。
public class CareCardSaleService
{
    private readonly ApplicationDBContext db;
    private readonly IConfiguration config;
    private readonly IHttpContextAccessor http;
    public CareCardSaleService(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http)
    { this.db = db; this.config = config; this.http = http; }

    // 原两单结构继续兼容；新单直接在养护主订单下保存零售明细。
    public const string OrderSource = "care_card_reception";
    public const string SingleOrderSource = "care_card_single";
    public const string CareChargeRetailType = "养护附加费用";
    public static bool IsSingleOrder(Models.Order order) => order.type == "养护" && order.order_source == SingleOrderSource;

    public static Task<Models.Order?> GetLinkedRetailAsync(ApplicationDBContext db, int careOrderId, bool includeDraft = false) =>
        db.order.AsNoTracking().Where(o => ((o.type == "零售" && o.order_source == OrderSource
            && o.source_order_no == careOrderId.ToString())
            || (o.id == careOrderId && o.type == "养护" && o.order_source == SingleOrderSource)) && o.closed == 0
            && (includeDraft || (o.valid == 1 && o.recepting == 0))).OrderByDescending(o => o.id).FirstOrDefaultAsync();

    public static Task<bool> IsLinkedRetailAsync(ApplicationDBContext db, int orderId) =>
        db.order.AnyAsync(o => o.id == orderId && ((o.type == "零售" && o.order_source == OrderSource)
            || (o.type == "养护" && o.order_source == SingleOrderSource))
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
        if (IsSingleOrder(retail) && retail.valid == 1 && retail.recepting == 0) return retail;
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
        if (retail == null) return null;
        int careId;
        if (IsSingleOrder(retail)) careId = retail.id;
        else if (!int.TryParse(retail.source_order_no, out careId)) return null;
        var charges = await db.retail.AsNoTracking().Where(r => r.order_id == retail.id && r.valid == 1).ToListAsync();
        var cardAmount = charges.Where(r => r.retail_type != CareChargeRetailType).Sum(r => Money(r.deal_price));
        return new CareCardSaleInfo { careOrderId = careId, retailOrderId = retail.id, completed = retail.dealed == 1,
            cardAmount = cardAmount, careAmount = IsSingleOrder(retail) ? Money(retail.total_amount) - cardAmount
                : charges.Where(r => r.retail_type == CareChargeRetailType).Sum(r => Money(r.deal_price)) };
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
        if (request.selections.Select(s => s.productId).Distinct().Count() != 1)
            throw new InvalidOperationException("每单只能选择一种卡");
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
        var first = result.items[0];
        if (CareCardRules.ServiceMode(first) == "machine" && cares.Count != 1)
            throw new InvalidOperationException("机打蜡季卡仅限一套装备的订单选择");
        var priorRetail = await GetLinkedRetailAsync(db, order.id, true);
        var previous = priorRetail == null ? new CareCardPreview() : await ReadPreviewAsync(priorRetail, order.id);
        var helper = new CareController(db, config, http);
        foreach (var care in cares)
        {
            bool reset = !previous.items.Any(i => i.productId == first.productId)
                || !previous.projects.Any(p => p.careId == care.id) && !previous.items.Any(i => i.careIds.Contains(care.id));
            var plan = CareCardRules.PreviewCare(first, care, reset);
            if (care.is_cancel) throw new InvalidOperationException("已取消的装备不能重新下单");
            if (!double.IsFinite(care.repair_charge) || !double.IsFinite(care.discount) || care.repair_charge < 0 || care.discount < 0)
                throw new InvalidOperationException("附加费和减免金额必须为非负数");
            bool usesCard = CareCardRules.HasCoveredService(first, plan);
            if (usesCard)
            {
                var item = result.items.FirstOrDefault(i => i.total == null ? i.punches < 1 : i.punches < i.total);
                if (item == null) throw new InvalidOperationException("所选卡次数不足，请调整购卡数量");
                if (item.requiresBinding && !CareController.HasFullEquipInfo(care))
                    throw new InvalidOperationException("打蜡季卡需要完整的装备类型、品牌和长度");
                item.careIds.Add(care.id);
            }
            if (plan.need_edge != 1 && plan.need_wax != 1 && plan.free_wax != 1 && plan.need_unwax != 1
                && plan.need_repair != 1 && string.IsNullOrWhiteSpace(plan.repair_memo) && plan.repair_charge == 0 && plan.summer == null)
                result.errors.Add("第" + (cares.IndexOf(care) + 1) + "件装备请选择养护项目");
            double commonCharge = await ExtraServiceChargeAsync(helper, order.shop, first, plan, usesCard);
            double discount = care.discount;
            if (!string.IsNullOrWhiteSpace(care.ticket_code))
            {
                var oldCustomerTicket = await db.ticket.AsNoTracking()
                    .Where(t => t.code == care.ticket_code && t.template_id == CarePricingRules.LegacyDiscountTemplateId)
                    .Include(t => t.template).ThenInclude(t => t.productTicketTemplates).ThenInclude(r => r.product)
                    .FirstOrDefaultAsync();
                if (oldCustomerTicket != null)
                {
                    var (_, ticketDiscount) = await helper.CalcCharge(order.shop, care, oldCustomerTicket);
                    // 购卡与旧券互斥：清掉券自动带出的减免，较大的店员手动减免保留。
                    if (ticketDiscount > 0 && Money(discount) <= Money(ticketDiscount)) discount = 0;
                }
            }
            decimal amount = care.warranty || care.entertain ? 0
                : Math.Max(0, Money(commonCharge) + Money(care.repair_charge) - Money(discount));
            result.projects.Add(new CareCardProject { careId = care.id, need_edge = plan.need_edge,
                need_wax = plan.need_wax, need_unwax = plan.need_unwax, free_wax = plan.free_wax,
                edge_degree = plan.edge_degree, common_charge = commonCharge, repair_charge = care.repair_charge,
                discount = discount, amount = amount });
        }
        foreach (var item in result.items)
            item.serviceLabel = string.Join("；", cares.Where(c => item.careIds.Contains(c.id)).Select(c =>
            {
                var project = result.projects.Single(p => p.careId == c.id);
                var selected = new Care { need_edge = project.need_edge, need_wax = project.need_wax,
                    need_unwax = project.need_unwax, free_wax = project.free_wax, need_repair = c.need_repair,
                    repair_memo = c.repair_memo, urgent = c.urgent, summer = c.summer };
                return CoveredServiceLabel(item, selected);
            }).Distinct());
        result.careAmount = result.projects.Sum(p => p.amount);
        result.amount = result.cardAmount + result.careAmount;
        return result;
    }

    private static decimal Money(double value) => Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);

    private static async Task<double> ExtraServiceChargeAsync(CareController helper, string shop,
        CareCardPreviewItem card, Care selected, bool usesCard)
    {
        if (selected.warranty || selected.entertain) return 0;
        if (usesCard && CareCardRules.ServiceMode(card) == "machine")
        {
            selected.use_card = true;
            var (charge, _) = await helper.CalcCharge(shop, selected, null,
                new PunchCard { card_name = card.name, total = null });
            selected.use_card = false;
            if (CarePricingRules.CountChargeableItems(selected) > 0 && await helper.GetProduct(shop, selected) == null)
                throw new InvalidOperationException("当前门店未配置养护价格，请核对门店");
            return (double)Money(charge);
        }
        var covered = CareCardRules.CoveredServices(card, selected);
        int selectedCount = CarePricingRules.CountChargeableItems(selected);
        int coveredCount = usesCard ? CarePricingRules.CountChargeableItems(covered) : 0;
        if (selected.summer == null && selectedCount == coveredCount && (selected.urgent == 0 || selectedCount == 0)) return 0;
        var fullPrice = await helper.GetProduct(shop, selected);
        if (fullPrice == null && CarePricingRules.ResolveProductName(selected) != "")
            throw new InvalidOperationException("当前门店未配置养护价格，请核对门店");
        var coveredPrice = coveredCount == 0 ? null : await helper.GetProduct(shop, covered);
        if (coveredCount > 0 && coveredPrice == null)
            throw new InvalidOperationException("当前门店未配置卡抵扣项目的价格，请核对门店");
        return (double)Math.Max(0, Money(fullPrice?.sale_price ?? 0) - Money(coveredPrice?.sale_price ?? 0));
    }

    private static void ApplyProjects(CareCardPreview preview, IEnumerable<Care> cares)
    {
        foreach (var care in cares)
        {
            var project = preview.projects.Single(p => p.careId == care.id);
            care.need_edge = project.need_edge; care.need_wax = project.need_wax;
            care.need_unwax = project.need_unwax; care.free_wax = project.free_wax; care.edge_degree = project.edge_degree;
            care.use_card = false; care.card_id = null; care.card_name = null; care.ticket_code = null;
            care.common_charge = project.common_charge; care.discount = project.discount; care.ticket_discount = 0;
            if (care.summer != null) care.biz_type = "非雪季养护";
            else if (care.biz_type == "非雪季养护") care.biz_type = null;
        }
    }

    // 每张拟购卡的临时使用安排放入现有 retail.memo，支付业务完成后清空。
    // 售价、商品、发卡来源和实际用量始终由原业务表记录。
    private sealed class PendingCard
    {
        public int version { get; set; } = 1;
        public CareCardPreviewItem item { get; set; } = new();
        public List<CareCardProject>? projects { get; set; }
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
        result.careAmount = rows.Where(r => r.retail_type == CareChargeRetailType).Sum(r => Money(r.deal_price));
        foreach (var row in rows.Where(r => r.retail_type != CareChargeRetailType))
        {
            if (retailOrder.dealed != 1)
            {
                result.items.Add(ReadPlan(row));
                var savedProjects = JsonConvert.DeserializeObject<PendingCard>(row.memo)?.projects;
                if (savedProjects != null) result.projects = savedProjects;
                continue;
            }
            var card = await db.punchCard.AsNoTracking().FirstOrDefaultAsync(c => c.id == row.punch_card_id && c.source_retail_id == row.id);
            if (card == null) throw new InvalidOperationException("未找到已发出的会员卡，请联系管理员核对");
            var uses = await db.punchCardUsed.AsNoTracking().Where(u => u.card_id == card.id && u.order_id == careOrderId
                && u.biz_type == "养护" && u.valid).ToListAsync();
            var careIds = uses.Select(u => u.biz_id).ToList();
            var cares = await db.care.AsNoTracking().Where(c => careIds.Contains(c.id)).ToListAsync();
            var item = new CareCardPreviewItem { lineNo = result.items.Count + 1, productId = row.product_id.Value,
                name = card.card_name, price = Math.Round((decimal)row.deal_price, 2), total = card.total,
                projectCount = card.care_project_count, requiresBinding = CareCardRules.RequiresEquipmentBinding(card.total, card.card_name),
                careIds = careIds };
            item.serviceLabel = string.Join("；", cares.Select(c => CoveredServiceLabel(item, c)).Distinct());
            result.items.Add(item);
        }
        if (IsSingleOrder(retailOrder))
        {
            result.careAmount = retailOrder.dealed == 1 ? Money(retailOrder.total_amount) - result.cardAmount
                : result.projects.Sum(p => p.amount);
            result.amount = result.cardAmount + result.careAmount;
        }
        return result;
    }

    private static string CoveredServiceLabel(CareCardPreviewItem item, Care selected) =>
        CareCardRules.ServiceMode(item) == "machine" ? "机打蜡" : ServiceLabel(CareCardRules.CoveredServices(item, selected));

    private static string ServiceLabel(Care care)
    {
        var labels = new List<string>();
        if (care.need_edge == 1) labels.Add("修刃");
        if (care.need_wax == 1) labels.Add("热蜡");
        if (care.free_wax == 1) labels.Add("机打蜡");
        if (care.need_unwax == 1 && care.need_wax != 1) labels.Add("刮蜡");
        if (care.need_repair == 1 || !string.IsNullOrWhiteSpace(care.repair_memo)) labels.Add("维修");
        if (care.summer != null) labels.Add("寄存");
        if (care.urgent == 1) labels.Add("立等");
        return string.Join("＋", labels);
    }

    private static bool SamePlan(CareCardPreviewItem a, CareCardPreviewItem b) =>
        a.productId == b.productId && a.price == b.price && a.name == b.name && a.total == b.total
        && a.projectCount == b.projectCount && a.requiresBinding == b.requiresBinding && a.serviceLabel == b.serviceLabel
        && a.careIds.OrderBy(x => x).SequenceEqual(b.careIds.OrderBy(x => x));

    private static bool SameProjects(List<CareCardProject> a, List<CareCardProject> b) =>
        a.Count == b.Count && a.OrderBy(p => p.careId).Zip(b.OrderBy(p => p.careId)).All(pair =>
            pair.First.careId == pair.Second.careId && pair.First.need_edge == pair.Second.need_edge
            && pair.First.need_wax == pair.Second.need_wax && pair.First.need_unwax == pair.Second.need_unwax
            && pair.First.free_wax == pair.Second.free_wax && pair.First.edge_degree == pair.Second.edge_degree
            && Money(pair.First.common_charge) == Money(pair.Second.common_charge)
            && Money(pair.First.repair_charge) == Money(pair.Second.repair_charge)
            && Money(pair.First.discount) == Money(pair.Second.discount) && pair.First.amount == pair.Second.amount);

    public async Task<CareCardPreview> SaveDraftAsync(CareCardSaleRequest request)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockAsync(db, "care-card-order:" + request.orderId);
        var preview = await PreviewAsync(request);
        var existing = await GetLinkedRetailAsync(db, request.orderId, true);
        if (existing != null && existing.recepting != 1) throw new InvalidOperationException("订单已提交，请到原结算页继续支付");
        if (existing == null && preview.items.Count == 0) { await tx.CommitAsync(); return preview; }
        var careOrder = await db.order.AsTracking().FirstAsync(o => o.id == request.orderId);
        var retailOrder = existing == null ? careOrder : await db.order.AsTracking().FirstAsync(o => o.id == existing.id);
        if (existing == null && preview.items.Count > 0)
        {
            retailOrder.order_source = SingleOrderSource;
            retailOrder.sub_type = "养护购卡";
        }
        retailOrder.shop = careOrder.shop; retailOrder.member_id = careOrder.member_id;
        retailOrder.staff_id = careOrder.staff_id; retailOrder.is_test = careOrder.is_test;
        retailOrder.name = careOrder.name; retailOrder.gender = careOrder.gender; retailOrder.cell = careOrder.cell;
        retailOrder.contact_name = careOrder.contact_name; retailOrder.contact_num = careOrder.contact_num;
        retailOrder.contact_gender = careOrder.contact_gender; retailOrder.wechat_unverified = careOrder.wechat_unverified;
        retailOrder.total_amount = (double)preview.amount; retailOrder.paying_amount = (double)preview.amount;
        retailOrder.update_date = DateTime.Now;
        var old = await db.retail.AsTracking().Where(r => r.order_id == retailOrder.id).ToListAsync();
        if (old.Any(r => r.punch_card_id != null)) throw new InvalidOperationException("本单已发卡，不能覆盖购卡安排");
        db.retail.RemoveRange(old); await db.SaveChangesAsync();
        foreach (var item in preview.items)
            db.retail.Add(new Retail { order_id = retailOrder.id, product_id = item.productId, sale_price = (double)item.price,
                deal_price = (double)item.price, order_type = "卡类销售", retail_type = "养护卡",
                memo = JsonConvert.SerializeObject(new PendingCard { item = item, projects = item.lineNo == 1 ? preview.projects : null }), valid = 1 });
        if (!IsSingleOrder(retailOrder) && preview.careAmount > 0)
            db.retail.Add(new Retail { order_id = retailOrder.id, sale_price = (double)preview.careAmount,
                deal_price = (double)preview.careAmount, order_type = "养护补费", retail_type = CareChargeRetailType,
                memo = "关联养护订单 " + request.orderId + " 的卡外项目及附加费用，与卡款合并收取", valid = 1 });
        if (preview.items.Count > 0)
            ApplyProjects(preview, await db.care.AsTracking().Where(c => c.order_id == request.orderId).ToListAsync());
        else if (IsSingleOrder(retailOrder))
        {
            retailOrder.order_source = null;
            retailOrder.sub_type = "";
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return preview;
    }

    public async Task<object> GetDraftAsync(int orderId)
    {
        var retail = await GetLinkedRetailAsync(db, orderId, true);
        var preview = retail == null ? new CareCardPreview { careOrderId = orderId } : await ReadPreviewAsync(retail, orderId);
        if (retail?.recepting == 1 && preview.items.Count > 0)
            preview = await PreviewAsync(new CareCardSaleRequest { orderId = orderId,
                selections = preview.items.GroupBy(i => i.productId).Select(g => new CareCardSelection { productId = g.Key, quantity = g.Count() }).ToList() });
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
        if (!preview.canCheckout) throw new InvalidOperationException(string.Join("；", preview.errors));
        if (preview.amount != saved.amount || preview.careAmount != saved.careAmount
            || saved.projects.Count > 0 && !SameProjects(preview.projects, saved.projects)
            || preview.items.Count != saved.items.Count || preview.items.Where((p, n) => !SamePlan(p, saved.items[n])).Any())
            throw new InvalidOperationException("商品价格、权益或装备项目已变化，请重新试算并确认");
        var careOrder = await db.order.AsTracking().FirstAsync(o => o.id == orderId);
        var retailOrder = await db.order.AsTracking().FirstAsync(o => o.id == existing.id);
        if (careOrder.member_id != retailOrder.member_id) throw new InvalidOperationException("顾客已改变，请重新选择卡类");
        retailOrder.valid = 1; retailOrder.recepting = 0; retailOrder.staff_id = staffId;
        retailOrder.wechat_unverified = careOrder.wechat_unverified;
        careOrder.valid = 1; careOrder.recepting = 0;
        careOrder.total_amount = IsSingleOrder(retailOrder) ? (double)preview.amount : 0;
        careOrder.paying_amount = careOrder.total_amount;
        careOrder.dealed = 0; careOrder.pay_with_deposit = false; careOrder.staff_id = staffId;
        careOrder.biz_date = retailOrder.biz_date = DateTime.Now;
        var helper = new OrderController(db, config, http);
        if (retailOrder.id != careOrder.id) await helper.GenerateOrderCode(retailOrder);
        await helper.GenerateOrderCode(careOrder);
        var cares = await db.care.AsTracking().Where(c => c.order_id == orderId).ToListAsync();
        ApplyProjects(preview, cares);
        foreach (var care in cares)
        {
            care.valid = 1;
            care.member_pick_date = care.urgent == 1 ? careOrder.biz_date.Date : careOrder.biz_date.Date.AddDays(1);
            var plan = preview.items.SingleOrDefault(i => i.careIds.Contains(care.id));
            if (plan == null) continue;
            care.use_card = true; care.card_id = null;
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
                throw new InvalidOperationException("购卡及养护费用尚未收齐");
            var careOrder = await db.order.AsTracking().FirstAsync(o => o.id == linkedCare.id);
            var memberId = order.member_id ?? throw new InvalidOperationException("购卡订单未匹配会员");
            careOrder.member_id = memberId;
            var payment = paid.FirstOrDefault(p => p.id == paymentId) ?? paid.First();
            bool verified = OrderVerifiedRules.ResolveOnPaid(careOrder.wechat_unverified || order.wechat_unverified,
                memberId, memberId, payment.pay_method, payment.is_proxy_pay, payment.member_id);
            order.wechat_unverified = careOrder.wechat_unverified = verified;
            var allRows = await db.retail.AsTracking().Where(r => r.order_id == order.id && r.valid == 1).ToListAsync();
            var rows = allRows.Where(r => r.retail_type != CareChargeRetailType).ToList();
            var plans = rows.ToDictionary(r => r.id, ReadPlan);
            var expectedTotal = allRows.Sum(r => Money(r.deal_price));
            if (IsSingleOrder(order))
            {
                var snapshot = await ReadPreviewAsync(order, careOrder.id);
                var currentCares = await db.care.AsNoTracking().Where(c => c.order_id == careOrder.id).ToListAsync();
                if (snapshot.projects.Count != currentCares.Count || snapshot.projects.Any(p => !currentCares.Any(c =>
                    c.id == p.careId && Money(c.common_charge) == Money(p.common_charge)
                    && Money(c.repair_charge) == Money(p.repair_charge) && Money(c.discount) == Money(p.discount))))
                    throw new InvalidOperationException("待付养护费用已变化，请联系管理员核对");
                expectedTotal += snapshot.careAmount;
            }
            if (expectedTotal != Money(order.total_amount))
                throw new InvalidOperationException("购卡及养护收费明细与订单合计不一致，请联系管理员核对");
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
            // 兼容本次修复前已提交、尚未付款的购卡单，沿用原下单日计算取板日期。
            var careRows = await db.care.AsTracking().Where(c => c.order_id == careOrder.id).ToListAsync();
            foreach (var care in careRows.Where(c => c.member_pick_date == null))
                care.member_pick_date = care.urgent == 1 ? careOrder.biz_date.Date : careOrder.biz_date.Date.AddDays(1);
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
        if (IsSingleOrder(order) && (order.valid != 1 || order.recepting != 0)) return "请先提交养护购卡订单再付款";
        if (order.type == "养护" && !IsSingleOrder(order) && await IsLinkedCareAsync(db, order.id))
            return "该养护单由关联购卡订单支付后自动核销，请勿重复收款";
        var care = await LinkedCareAsync(db, order);
        if (care == null) return null;
        if (order.closed != 0 || order.dealed != 0) return "购卡订单已支付或已关闭";
        double alreadyPaid = await db.orderPayment.Where(p => p.order_id == order.id && p.valid == 1 && p.status == "支付成功" && p.is_debt == 0).SumAsync(p => p.amount);
        if (Math.Round(alreadyPaid, 2) >= Math.Round(order.total_amount, 2)) return "购卡款已收到，正在处理发卡核销，请勿重复收款";
        if (amount != null && Math.Round(amount.Value, 2) != Math.Round(order.total_amount, 2)) return "购卡及养护费用须一次付清";
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
