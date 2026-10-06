#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;
public sealed partial class FnbV4Service
{
    private sealed record OperationPlan(FnbBatch Source, FnbItemForm From, FnbItemForm To, decimal InputBase, decimal ExpectedDisplay,
        decimal Ratio, decimal Yield, decimal Hours, string Storage, DateTime? OpExpiry, string BatchNo);
    private async Task<OperationPlan> PlanOperation(int batchId, decimal quantity)
    {
        var b = await Batch(batchId); var forms = await Forms(b.item_id);
        if (b.quantity <= 0 || b.dispose_status != null || b.effective_expire < Today || b.ready_at > Now) throw new ArgumentException("来源批次不可作业");
        var from = forms.Single(x => x.id == b.form_id); FnbItemForm to; decimal n, ratio, yield, hours; string storage; DateTime? opExpiry;
        if (b.state == "sealed")
        {
            if (quantity != decimal.Truncate(quantity)) throw new ArgumentException("开封数量须为整数包装数");
            n = Qty(Round(Qty(quantity) * b.pack_size!.Value)); to = from; ratio = b.pack_size.Value / to.per_base; yield = 1; hours = 0;
            var detail = await db.fnbBatchDetail.SingleOrDefaultAsync(x => x.batch_id == b.id);
            storage = detail?.open_storage ?? throw new ArgumentException("缺少开封后储存方式");
            opExpiry = Today.AddDays(detail.open_days ?? throw new ArgumentException("缺少开封后保质天数"));
        }
        else
        {
            to = forms.FirstOrDefault(x => x.seq == from.seq + 1) ?? throw new ArgumentException("该批次已是最终出品态");
            n = Qty(Round(Qty(quantity) * from.per_base)); ratio = to.in_op_ratio!.Value; yield = to.in_op_yield!.Value; hours = to.in_op_hours!.Value;
            storage = to.storage_type; opExpiry = to.shelf_after_op_days == null ? null : Today.AddDays(to.shelf_after_op_days.Value);
        }
        if (n > b.quantity) throw new ArgumentException("投入超过批次余额");
        return new(b, from, to, n, Qty(Round(quantity * ratio * yield), true), ratio, yield, hours, storage,
            opExpiry == null ? b.op_expire_date : b.op_expire_date == null ? opExpiry : (opExpiry < b.op_expire_date ? opExpiry : b.op_expire_date), await DerivedNo(b, b.state == "sealed" ? "OPEN" : to.form_code));
    }
    public async Task<object> PreviewOperation(int batchId, decimal quantity)
    {
        var p = await PlanOperation(batchId, quantity);
        return new { batchId, inputQuantity = quantity, inputBaseQuantity = p.InputBase, inputUnit = p.Source.state == "sealed" ? p.Source.pack_label : p.From.unit_name,
            outputFormId = p.To.id, outputFormName = p.To.name, outputUnit = p.To.unit_name, expectedQuantity = p.ExpectedDisplay,
            standardYield = p.Yield, conversionRatio = p.Ratio, durationHours = p.Hours, readyAt = p.Hours == 0 ? (DateTime?)null : Now.AddHours((double)p.Hours),
            batchNo = p.BatchNo, storageType = p.Storage, expireDate = p.Source.expire_date,
            effectiveExpire = p.OpExpiry == null || p.OpExpiry > p.Source.expire_date ? p.Source.expire_date : p.OpExpiry };
    }
    public async Task<object> Operation(FnbOperationRequest input)
    {
        var p = await PlanOperation(input.BatchId, input.InputQuantity); await Area(input.AreaId, required: true);
        decimal outputBase = Qty(Round(Qty(input.ActualQuantity, true) * p.To.per_base), true);
        if (outputBase > p.InputBase) throw new ArgumentException("实际产出基本量不能超过投入基本量");
        var item = await Item(p.Source.item_id); var d = await Document("op", input.RequestId);
        decimal cost = p.InputBase == p.Source.quantity ? p.Source.amount : Round(p.Source.amount * p.InputBase / p.Source.quantity);
        var consumed = await Line(d, item, -1, input.InputQuantity, p.Source.state == "sealed" ? p.Source.pack_size!.Value : p.From.per_base, p.From.id, p.Source.id);
        await Move(consumed, p.Source, -1, p.InputBase, cost);
        var forms = await Forms(item.id);
        var b = new FnbBatch { shop_id = shopId, item_id = item.id, form_id = p.To.id, state = p.To.id == forms.Last().id ? "final" : "staged",
            batch_no = p.BatchNo, quantity = 0, amount = 0, storage_type = p.Storage, production_date = p.Source.production_date,
            expire_date = p.Source.expire_date, op_date = Today, op_expire_date = p.OpExpiry, expiry_source = "operation",
            parent_batch_id = p.Source.id, ready_at = p.Hours == 0 ? null : Now.AddHours((double)p.Hours), received_at = Now };
        db.fnbBatch.Add(b); await db.SaveChangesAsync();
        db.fnbBatchDetail.Add(new FnbBatchDetail { batch_id = b.id, area_id = input.AreaId });
        var produced = await Line(d, item, 1, input.ActualQuantity, p.To.per_base, p.To.id, b.id); await Move(produced, b, 1, outputBase, outputBase == 0 ? 0 : cost);
        var op = new FnbStockOperation { document_id = d.id, item_id = item.id, from_form_id = p.From.id, to_form_id = p.To.id,
            source_batch_id = p.Source.id, output_batch_id = b.id, op_name = p.Source.state == "sealed" ? "开封" : p.To.in_op_name!, input_qty = input.InputQuantity,
            std_ratio = p.Ratio, std_yield = p.Yield, expected_qty = p.ExpectedDisplay, actual_qty = input.ActualQuantity,
            loss_base_qty = Round(p.InputBase - outputBase), duration_hours = p.Hours, ready_at = b.ready_at, status = p.Hours == 0 ? "done" : "running",
            completed_at = p.Hours == 0 ? Now : null, staff_id = StaffId };
        db.fnbStockOperation.Add(op); await db.SaveChangesAsync();
        return new { operation = op, actualYield = Round(outputBase / p.InputBase), lowYield = outputBase / p.InputBase < p.Yield, batch = await BatchData(b) };
    }
    public async Task<object> CompleteOperation(FnbCompleteOperationRequest input)
    {
        var op = await (from o in db.fnbStockOperation join d in db.fnbStockDocument on o.document_id equals d.id where d.shop_id == shopId && o.id == input.OperationId select o).SingleOrDefaultAsync()
            ?? throw new ArgumentException("作业不存在");
        if (op.status == "done") return op;
        if (op.ready_at > Now) Manager();
        var b = await Batch(op.output_batch_id); b.ready_at = Now; Modified(b);
        op.ready_at = Now; op.completed_at = Now; op.status = "done"; Modified(op); await db.SaveChangesAsync(); return op;
    }
    public async Task<object> Workbench()
    {
        var ops = await (from o in db.fnbStockOperation join d in db.fnbStockDocument on o.document_id equals d.id where d.shop_id == shopId select new { operation = o, d.occurred_at }).ToArrayAsync();
        var batches = await db.fnbBatch.Where(x => x.shop_id == shopId && x.quantity > 0 && x.dispose_status == null && x.effective_expire >= Today && (x.ready_at == null || x.ready_at <= Now)).ToArrayAsync();
        var forms = await db.fnbItemForm.Where(x => x.valid).ToArrayAsync();
        var can = batches.Where(b => b.state == "sealed" || forms.Any(f => f.item_id == b.item_id && f.seq > forms.Single(x => x.id == b.form_id).seq)).ToArray();
        var stocks = await Stock();
        return new { ongoing = ops.Where(x => x.operation.status == "running" && x.operation.ready_at > Now).Select(x => x.operation).ToArray(),
            recommended = stocks.Where(x => x.LowStock && can.Any(b => b.item_id == x.ItemId)).Select(x => new { stock = x, batches = can.Where(b => b.item_id == x.ItemId).ToArray() }).ToArray(),
            canOperate = can, todayRecords = ops.Where(x => x.occurred_at.AddHours(8).Date == Today).Select(x => new { x.operation, ready = x.operation.ready_at == null || x.operation.ready_at <= Now }).ToArray() };
    }
    public async Task<object> Home()
    {
        var stock = await Stock(); var batches = await db.fnbBatch.Where(x => x.shop_id == shopId && x.quantity > 0 && x.dispose_status == null).ToArrayAsync();
        var ops = await (from o in db.fnbStockOperation join d in db.fnbStockDocument on o.document_id equals d.id where d.shop_id == shopId && o.ready_at > Now && o.status == "running" select o.id).CountAsync();
        var forms = await db.fnbItemForm.Where(x => x.valid).ToArrayAsync();
        var items = await db.fnbItem.ToArrayAsync(); var cats = await db.fnbCategory.ToArrayAsync();
        var supplies = await db.fnbSupply.Where(x => x.shop_id == shopId && x.valid).ToArrayAsync();
        return new { lowStockCount = stock.Count(x => x.LowStock) + supplies.Count(x => x.quantity < (x.low_stock_qty ?? Round(x.last_receipt_qty * (x.low_stock_ratio ?? .1m)))),
            nearExpiryCount = batches.Count(b => b.effective_expire >= Today && b.effective_expire <= Today.AddDays(FnbV4Rules.Defaults(items.Single(i => i.id == b.item_id), cats.Single(c => c.id == items.Single(i => i.id == b.item_id).category_id)).WarnDays)),
            expiredCount = batches.Count(x => x.effective_expire < Today), stockItemCount = stock.Count(x => x.TotalQuantity > 0), batchCount = batches.Length,
            openedBatchCount = batches.Count(x => x.parent_batch_id != null), ongoingOperationCount = ops,
            recommendedOperationCount = stock.Count(x => x.LowStock && batches.Any(b => b.item_id == x.ItemId && b.effective_expire >= Today && (b.ready_at == null || b.ready_at <= Now) && (b.state == "sealed" || forms.Any(f => f.item_id == b.item_id && f.seq > forms.Single(t => t.id == b.form_id).seq)))),
            businessDate = Today, checkStatus = await db.fnbCheckSheet.Where(x => x.shop_id == shopId && x.business_date == Today).Select(x => x.status).SingleOrDefaultAsync() ?? "none" };
    }
}
