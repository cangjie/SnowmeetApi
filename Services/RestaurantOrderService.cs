using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services;

public sealed class RestaurantOrderService(ApplicationDBContext db)
{
    public async Task<Order> CreateAsync(RestaurantOrderRequest request, int staffId)
    {
        if (!Guid.TryParse(request.requestId, out var requestGuid)) throw new InvalidOperationException("下单请求标识无效");
        var requestId = requestGuid.ToString();
        var table = (request.tableNo ?? "").Trim();
        if (table.Length == 0 || table.Length > 30) throw new InvalidOperationException("请输入正确桌号");
        if (request.amount <= 0 || request.amount > 10000000m || decimal.Round(request.amount, 2) != request.amount)
            throw new InvalidOperationException("请输入正确金额");
        if (request.discount < 0 || request.discount > request.amount || decimal.Round(request.discount, 2) != request.discount)
            throw new InvalidOperationException("请输入正确的减免金额");
        var name = (request.customerName ?? "").Trim();
        var cell = (request.customerCell ?? "").Trim();
        var gender = (request.gender ?? "").Trim();
        if (name.Length > 20 || cell.Length > 20 || (gender != "" && gender != "男" && gender != "女"))
            throw new InvalidOperationException("顾客信息不正确");

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await CareCardSaleService.LockAsync(db, "restaurant-request:" + requestId);
        var now = DateTime.Now;
        var prefix = "QJ_CY_" + now.ToString("yyMMdd") + "_";
        // 先取得当日编号锁再查缺失的 request_id，避免并发建单各自持有范围锁后互相等待。
        await CareCardSaleService.LockAsync(db, "restaurant-code:" + prefix);
        var existing = await db.restaurantOrder.AsNoTracking().FirstOrDefaultAsync(x => x.request_id == requestId);
        if (existing != null)
        {
            var saved = await db.order.AsNoTracking().SingleAsync(x => x.id == existing.order_id);
            saved.discounts = await db.discount.AsNoTracking().Where(d => d.order_id == saved.id && d.valid == 1).ToListAsync();
            if (saved.staff_id != staffId || saved.type != "餐饮" || saved.valid != 1 || existing.table_no != table
                || (decimal)saved.total_amount != request.amount || saved.is_test != (request.isTest || staffId == 28 || staffId == 31 || staffId == 34 ? 1 : 0)
                || existing.discount_amount != request.discount || (decimal)saved.discountAmount != request.discount
                || (saved.contact_name ?? "") != name || (saved.contact_num ?? "") != cell || (saved.contact_gender ?? "") != gender
                || (request.memberId != null && saved.member_id != request.memberId))
                throw new InvalidOperationException("下单内容已变化，请重新开单");
            saved.restaurantInfo = existing;
            await tx.CommitAsync();
            return saved;
        }
        if (request.memberId != null && !await db.member.AnyAsync(m => m.id == request.memberId && m.valid == 1 && m.merge_id == null))
            throw new InvalidOperationException("会员不存在或已失效");

        var codes = await db.order.AsNoTracking().Where(o => o.code != null && o.code.StartsWith(prefix)).Select(o => o.code).ToListAsync();
        var sequence = codes.Select(c => int.TryParse(c!.Substring(prefix.Length), out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        if (sequence > 99999) throw new InvalidOperationException("当日餐饮订单编号已用完");
        var order = new Order {
            shop = "多呆一会儿吧", type = "餐饮", code = prefix + sequence.ToString("D5"),
            order_source = RestaurantOrder.OrderSource, source_order_no = requestId,
            member_id = request.memberId, staff_id = staffId, contact_name = name, contact_num = cell,
            contact_gender = gender, total_amount = (double)request.amount, paying_amount = (double)(request.amount - request.discount),
            create_date = now, biz_date = now, valid = 1, recepting = 0,
            is_test = request.isTest || staffId == 28 || staffId == 31 || staffId == 34 ? 1 : 0
        };
        var linked = await new ReceptOrderMemberService(db).PrepareAsync(order, staffId, request.receptCheckinId);
        db.order.Add(order);
        await db.SaveChangesAsync();
        var info = new RestaurantOrder { order_id = order.id, table_no = table, discount_amount = request.discount, request_id = requestId, create_date = now };
        db.restaurantOrder.Add(info);
        await db.SaveChangesAsync();
        if (request.discount > 0)
        {
            // 整单减免不使用 biz_id：该字段被旧餐饮菜品明细 fd_order 的 id 占用。
            var discount = new Discount { order_id = order.id, biz_type = "餐饮", biz_id = null,
                amount = (double)request.discount, staff_id = staffId, valid = 1, memo = "餐饮开单减免", create_date = now };
            db.discount.Add(discount);
        }
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("Order", "OrderState", order.id, "餐饮开单", null, staffId, null, "待支付", "桌号 " + table));
        if (linked) db.coreDataModLog.Add(ReceptOrderMemberService.ChangeLog(order.id, order.member_id!.Value, staffId, request.receptCheckinId!.Value));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        order.restaurantInfo = info;
        return order;
    }
}
