using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services;

// 顾客自助购卡与养护开单购卡共用同一发卡方法、销售来源及防重字段。
public static class RetailCardService
{
    public static async Task GrantAsync(ApplicationDBContext db, Models.Order order,
        IReadOnlyDictionary<int, CareCardPreviewItem>? snapshots = null)
    {
        await using var tx = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        try
        {
            await CareCardSaleService.LockAsync(db, "retail-card-order:" + order.id);
            var rows = await db.retail.AsTracking()
                .Where(r => r.order_id == order.id && r.valid == 1 && r.product_id != null).OrderBy(r => r.id).ToListAsync();
            foreach (var retail in rows)
            {
                await db.Entry(retail).ReloadAsync();
                if (retail.punch_card_id != null || order.member_id == null) continue;
                CareCardPreviewItem snapshot;
                string bizType;
                if (snapshots != null)
                {
                    if (!snapshots.TryGetValue(retail.id, out snapshot) || snapshot.productId != retail.product_id
                        || snapshot.price != Math.Round((decimal)retail.deal_price, 2))
                        throw new InvalidOperationException("待付卡商品记录不一致，请联系管理员核对");
                    bizType = "养护";
                }
                else
                {
                    var product = await db.product.AsNoTracking().FirstOrDefaultAsync(p => p.id == retail.product_id);
                    if (product == null || string.IsNullOrEmpty(product.category_code)) continue;
                    var category = await db.category.AsNoTracking().FirstOrDefaultAsync(c => c.code == product.category_code
                        && c.valid == 1 && (c.name == "次卡" || c.name == "季卡"));
                    if (category == null || (category.name != "季卡" && product.punch_total == null)) continue;
                    bizType = category.biz_type;
                    snapshot = new CareCardPreviewItem { name = product.name, total = category.name == "季卡" ? null : product.punch_total,
                        projectCount = product.care_project_count };
                }
                var card = new PunchCard { biz_type = bizType, card_name = snapshot.name, member_id = order.member_id.Value,
                    total = snapshot.total, punches = 0, care_project_count = snapshot.projectCount, source_retail_id = retail.id };
                db.punchCard.Add(card);
                await db.SaveChangesAsync();
                retail.punch_card_id = card.id;
                await db.SaveChangesAsync();
            }
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) { await tx.RollbackAsync(); db.ChangeTracker.Clear(); }
            throw;
        }
    }
}
