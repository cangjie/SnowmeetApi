using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record ManualKitchenLineInput(int ProductId, decimal Quantity, string? Remark);
public sealed record ManualKitchenOrderInput(int ShopId, Guid RequestId, string? DisplayNo,
    string? TableNo, string? Remark, IReadOnlyList<ManualKitchenLineInput> Lines);
public sealed record ManualKitchenOrderResult(long OrderId, string DisplayNo, string ReviewStatus,
    int LineCount, bool Replayed);

/// <summary>Employees select local dishes and quantities; no platform identity or SKU is involved.</summary>
public sealed class FnbManualOrderService(ApplicationDBContext db)
{
    private static readonly TimeZoneInfo Shanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    public async Task<ManualKitchenOrderResult> CreateAsync(ManualKitchenOrderInput input, FnbAccess.Actor actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await CreateCoreAsync(input, actor, verified: false);
        await tx.CommitAsync();
        return result;
    }

    /// <summary>建单本体，由调用方开事务。verified=true 用于「建单即扣料」：配料已由员工在单上确认，不再依赖已发布配方。</summary>
    internal async Task<ManualKitchenOrderResult> CreateCoreAsync(ManualKitchenOrderInput input, FnbAccess.Actor actor, bool verified)
    {
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) throw new UnauthorizedAccessException("无门店权限");
        if (input.RequestId == Guid.Empty || !FnbText.FitsChineseVarchar(input.DisplayNo, 128))
            throw new ArgumentException("手动订单参数无效");
        ValidateOrder(input.Lines, input.TableNo, input.Remark);

        string dedupeKey = input.RequestId.ToString("N");
        var imported = await db.fnbOrderImport.AsNoTracking().FirstOrDefaultAsync(x =>
            x.shop_id == input.ShopId && x.source_method == "internal" && x.dedupe_key == dedupeKey);
        if (imported != null)
        {
            if (imported.order_id == null) throw new InvalidOperationException("订单请求正在处理，请稍后重试");
            var previous = await db.fnbOrder.AsNoTracking().FirstAsync(x => x.id == imported.order_id && x.shop_id == input.ShopId);
            int count = await db.fnbOrderLine.CountAsync(x => x.order_id == previous.id);
            return new ManualKitchenOrderResult(previous.id, previous.display_no, previous.review_status, count, true);
        }

        var (built, allPublished) = await BuildLinesAsync(input.ShopId, input.Lines);
        DateTime now = DateTime.UtcNow;
        string displayNo = string.IsNullOrWhiteSpace(input.DisplayNo)
            ? "M" + TimeZoneInfo.ConvertTimeFromUtc(now, Shanghai).ToString("MMddHHmmss") + dedupeKey[..6]
            : input.DisplayNo.Trim();
        var order = new FnbOrder
        {
            shop_id = input.ShopId, source_type = "manual", display_no = displayNo,
            business_date = TimeZoneInfo.ConvertTimeFromUtc(now, Shanghai).Date,
            ordered_at = now, table_no = input.TableNo?.Trim(), remark = input.Remark,
            order_status = "pending", review_status = verified || allPublished ? "verified" : "pending",
            refund_status = "none", created_at = now
        };
        db.fnbOrder.Add(order);
        await db.SaveChangesAsync();
        built.ForEach(x => x.order_id = order.id);
        db.fnbOrderLine.AddRange(built);
        db.fnbOrderImport.Add(new FnbOrderImport
        {
            shop_id = input.ShopId, source_method = "internal", dedupe_key = dedupeKey,
            order_id = order.id, process_status = "accepted", reviewed_by_staff_id = actor.Staff.id,
            captured_at = now, processed_at = now
        });
        await db.SaveChangesAsync();
        return new ManualKitchenOrderResult(order.id, displayNo, order.review_status, input.Lines.Count, false);
    }

    internal static void ValidateOrder(IReadOnlyList<ManualKitchenLineInput>? lines, string? tableNo, string? remark)
    {
        if (lines == null || lines.Count is < 1 or > 100 ||
            lines.Any(x => x.ProductId <= 0 || x.Quantity <= 0 || x.Quantity > 10000 ||
                x.Quantity != decimal.Round(x.Quantity, 6) || !FnbText.FitsChineseVarchar(x.Remark, 1000)) ||
            !FnbText.FitsChineseVarchar(tableNo, 100) || !FnbText.FitsChineseVarchar(remark, 2000))
            throw new ArgumentException("手动订单参数无效");
    }

    /// <summary>校验菜品（本店有效餐饮菜品）、取默认规格（没有就建「标准份」），生成未挂订单号的厨房单明细；
    /// 第二个返回值表示这些菜品是否都有已发布配方。建单和编辑已扣料厨房单共用。</summary>
    internal async Task<(List<FnbOrderLine> Lines, bool AllPublished)> BuildLinesAsync(int shopId, IReadOnlyList<ManualKitchenLineInput> input)
    {
        int[] productIds = input.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.product.AsNoTracking().Where(x => productIds.Contains(x.id) && x.valid == 1 &&
            x.shop_id == shopId).ToDictionaryAsync(x => x.id);
        if (products.Count != productIds.Length) throw new ArgumentException("存在无效或非本店菜品");
        int[] categoryIds = products.Values.Where(x => x.category_id != null).Select(x => x.category_id!.Value).Distinct().ToArray();
        int restaurantCategories = await db.category.CountAsync(x => categoryIds.Contains(x.id) &&
            x.biz_type == "餐饮" && x.valid == 1);
        if (restaurantCategories != categoryIds.Length || products.Values.Any(x => x.category_id == null))
            throw new ArgumentException("只能选择本店有效餐饮菜品");

        var specs = await db.fnbDishSpec.AsTracking().Where(x => x.shop_id == shopId &&
            productIds.Contains(x.product_id) && x.valid).ToListAsync();
        var selected = new Dictionary<int, FnbDishSpec>();
        foreach (int productId in productIds)
        {
            var candidates = specs.Where(x => x.product_id == productId).ToList();
            var spec = candidates.FirstOrDefault(x => x.is_default);
            if (spec == null && candidates.Count == 1) spec = candidates[0];
            if (spec == null && candidates.Count > 1)
                throw new ArgumentException($"菜品 {products[productId].name} 有多个规格但没有默认规格");
            if (spec == null)
            {
                spec = new FnbDishSpec
                {
                    shop_id = shopId, product_id = productId, spec_code = "default", name = "标准份",
                    is_default = true, valid = true, created_at = DateTime.UtcNow
                };
                db.fnbDishSpec.Add(spec);
            }
            selected[productId] = spec;
        }
        await db.SaveChangesAsync();

        int[] specIds = selected.Values.Select(x => x.id).ToArray();
        var published = await db.fnbRecipe.AsNoTracking().Where(x => x.shop_id == shopId &&
            x.recipe_type == "dish" && x.status == "published" && x.dish_spec_id != null &&
            specIds.Contains(x.dish_spec_id.Value)).Select(x => x.dish_spec_id!.Value).ToListAsync();
        var lines = input.Select((source, index) => new FnbOrderLine
        {
            shop_id = shopId, line_key = "manual:" + (index + 1),
            item_name = products[source.ProductId].name,
            spec_name = selected[source.ProductId].name,
            quantity = source.Quantity, cancelled_qty = 0, is_inventory_line = true,
            dish_spec_id = selected[source.ProductId].id, option_key = "", remark = source.Remark
        }).ToList();
        return (lines, specIds.All(published.Contains));
    }

    public async Task CancelAsync(int shopId, long orderId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var order = await db.fnbOrder.AsTracking().FirstOrDefaultAsync(x => x.id == orderId &&
            x.shop_id == shopId && x.source_type == "manual");
        if (order == null) throw new ArgumentException("手动订单不存在");
        if (order.order_status == "cancelled") { await tx.CommitAsync(); return; }
        if (await db.fnbStockDocument.AnyAsync(x => x.order_id == orderId && x.status == "posted"))
            throw new InvalidOperationException("订单已出餐，不能直接取消");
        order.order_status = "cancelled";
        order.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
