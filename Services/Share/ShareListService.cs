using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Services.Share;

/// <param name="Status">未分 / 已分，空 = 全部</param>
/// <param name="Mode">自动 / 手动 / 银行转账，空 = 全部</param>
/// <param name="Keyword">订单号或收款人里包含的文字</param>
/// <param name="StartDate">日期范围按 COALESCE(plan_date, create_date) 的日期，含首尾两天</param>
public sealed record ShareListQuery(string? Status, string? Type, string? Mode, string? Keyword, DateTime? StartDate, DateTime? EndDate,
    int PageIndex = 1, int PageSize = 20)
{
    public bool DistributorOnly { get; init; }
    public string? DistributorType { get; init; }
}

/// <param name="planDate">yyyy-MM-dd；没有计划日期时：租赁取关单次日，其他为空</param>
/// <param name="sharedTime">yyyy-MM-dd HH:mm，已分才有</param>
/// <param name="batch">银行转账的批次号；渠道分账为空</param>
/// <param name="listDate">COALESCE(plan_date, create_date) 的日期 yyyy-MM-dd，小程序用它判断「30 天前」</param>
public sealed record ShareListItem(int orderShareId, int orderId, string orderCode, string shop, string orderType, string payee, double amount,
    string applicant, string mode, string? manualReason, string status, string? planDate, string? sharedTime, string? batch, bool selectable,
    int? skipassId, string listDate, string applicationDate, bool autoShare, string? distributorType,
    string? approvalState, bool canApprove, bool canSubmitManual, string? channelState, string? channelMessage);

/// <param name="totalAmount">筛选后全部条目的金额合计（两位小数），不是当前页</param>
/// <param name="selectableCount">筛选后全部条目里可勾选的条数</param>
public sealed record ShareListPage(List<ShareListItem> items, int total, double totalAmount, int selectableCount);

public sealed record ManualResult(string batchNo, int count, double total);

/// <summary>分账列表与手动分账（批次记账）。设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-03-share-list-manual-batch-design.md</summary>
public sealed class ShareListService(ApplicationDBContext db)
{
    public const int MaxBatchSize = 200;
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>一条有效应分连同它的订单、有效分账记录、批次和按规则算出来的事实；列表和记账共用，保证判断同一套。</summary>
    internal sealed record Row(OrderShare Share, Models.Order Order, List<PaymentShare> Records, ShareBatch? Batch, ShareFacts Facts);

    private static bool SystemTicket(Row r) => r.Order.type == "雪票" && r.Share.share_method == null && r.Share.amount == 1;
    private static string Method(Row r) => ShareWorkflowRules.Method(r.Share, r.Facts, SystemTicket(r));
    private static string Status(Row r) => ShareListRules.Status(r.Facts) == "已分"
        && (r.Share.share_method == null || r.Share.batch_id != null || Math.Abs(Math.Round(r.Records.Sum(p => p.amount), 2) - Math.Round(r.Share.amount, 2)) <= 0.001)
            ? "已分" : "未分";
    private static bool BankSelectable(Row r) => Method(r) == ShareWorkflowRules.Bank && Status(r) == "未分"
        && !r.Records.Any(p => p.success == true || (p.submit_time != null && p.response_time == null));

    internal async Task<List<Row>> LoadRowsAsync(IReadOnlyCollection<int>? ids, string? type, DateTime? start, DateTime? end)
    {
        IQueryable<OrderShare> q = db.orderShare.AsNoTracking().Where(s => s.valid);
        if (ids != null) q = q.Where(s => ids.Contains(s.id));
        if (start != null)
        {
            DateTime from = start.Value.Date;
            q = q.Where(s => (s.plan_date ?? s.create_date) >= from);
        }
        if (end != null)
        {
            DateTime before = end.Value.Date.AddDays(1);
            q = q.Where(s => (s.plan_date ?? s.create_date) < before);
        }
        List<OrderShare> shares = await q.ToListAsync();
        List<int> orderIds = shares.Select(s => s.order_id).Distinct().ToList();
        Dictionary<int, Models.Order> orders = (await db.order.AsNoTracking().Where(o => orderIds.Contains(o.id)).ToListAsync()).ToDictionary(o => o.id);
        string kind = (type ?? "").Trim();
        shares = shares.Where(s => orders.TryGetValue(s.order_id, out Models.Order? o) && (kind == "" || o.type == kind)).ToList();

        List<int> shareIds = shares.Select(s => s.id).ToList();
        Dictionary<int, List<PaymentShare>> records = (await db.paymentShare.AsNoTracking().Where(p => p.valid && shareIds.Contains(p.share_id)).ToListAsync())
            .GroupBy(p => p.share_id).ToDictionary(g => g.Key, g => g.ToList());
        List<int> batchIds = shares.Where(s => s.batch_id != null).Select(s => s.batch_id!.Value).Distinct().ToList();
        Dictionary<int, ShareBatch> batches = (await db.shareBatch.AsNoTracking().Where(b => batchIds.Contains(b.id)).ToListAsync()).ToDictionary(b => b.id);

        return shares.Select(s =>
        {
            List<PaymentShare> mine = records.GetValueOrDefault(s.id) ?? new List<PaymentShare>();
            ShareFacts facts = new(s.auto_share, s.batch_id, mine.Select(r => new RecordFacts(r.success, r.submit_time, r.response_time, r.can_not_share)).ToList());
            return new Row(s, orders[s.order_id], mine, s.batch_id != null ? batches.GetValueOrDefault(s.batch_id.Value) : null, facts);
        }).ToList();
    }

    public async Task<ShareListPage> ListAsync(ShareListQuery q)
    {
        List<Row> rows = (await LoadRowsAsync(null, q.Type, q.StartDate, q.EndDate))
            .Where(r => r.Share.relation_id != WanlongShareRules.WanlongRelationId).ToList();
        List<int> shareIds = rows.Select(r => r.Share.id).ToList();

        // 收款人：介绍人取介绍人名称，否则取收款方名称
        List<int> relationIds = rows.Select(r => r.Share.relation_id).Distinct().ToList();
        Dictionary<int, OrderShareRelation> relations = (await db.orderShareRelation.AsNoTracking().Include(r => r.binds)
            .Where(r => relationIds.Contains(r.id)).ToListAsync()).ToDictionary(r => r.id);
        Dictionary<int, Introducer> introducers = await ShareQueries.IntroducersByRelationAsync(db, relationIds);
        List<int> receiverStaffIds = relations.Values.Where(r => r.staff_id != null).Select(r => r.staff_id!.Value).Distinct().ToList();
        HashSet<int> activeReceiverStaffIds = (await db.staff.AsNoTracking()
            .Where(s => receiverStaffIds.Contains(s.id) && s.valid == 1).Select(s => s.id).ToListAsync()).ToHashSet();
        List<int> paymentIds = rows.SelectMany(r => r.Records).Select(p => p.payment_id).Distinct().ToList();
        Dictionary<int, OrderPayment> payments = (await db.orderPayment.AsNoTracking().Include(p => p.refunds)
            .Where(p => paymentIds.Contains(p.id)).ToListAsync()).ToDictionary(p => p.id);

        // 申请人：介绍人分账是店员在订单上设的，操作日志里有人；万龙、雪票是系统生成的
        Dictionary<int, int> applicantIds = (await db.coreDataModLog.AsNoTracking()
            .Where(l => l.table_name == "order_share" && l.scene == "设置介绍人分账" && l.staff_id != null && shareIds.Contains(l.key_value)).ToListAsync())
            .GroupBy(l => l.key_value).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.id).First().staff_id!.Value);
        List<int> staffIds = applicantIds.Values.Distinct().ToList();
        Dictionary<int, string> staffNames = (await db.staff.AsNoTracking().Where(s => staffIds.Contains(s.id)).ToListAsync()).ToDictionary(s => s.id, s => s.name ?? "");

        // 雪票详情页要的是雪票 id：取这张订单上最小的一个
        List<int> skiOrderIds = rows.Where(r => r.Order.type == "雪票").Select(r => r.Order.id).Distinct().ToList();
        Dictionary<int, int> skipassIds = (await db.skiPass.AsNoTracking().Where(k => k.order_id != null && skiOrderIds.Contains(k.order_id.Value)).ToListAsync())
            .GroupBy(k => k.order_id!.Value).ToDictionary(g => g.Key, g => g.Min(k => k.id));

        List<ShareListItem> items = rows.Select(r =>
        {
            OrderShare s = r.Share;
            string status = Status(r);
            string? done = status == "已分" ? SharedAt(r) : null;
            Introducer? distributor = introducers.GetValueOrDefault(s.relation_id);
            string method = Method(r);
            string mode = method == ShareWorkflowRules.Auto ? "自动" : method == ShareWorkflowRules.Manual ? "手动" : "银行转账";
            bool isUnsent = status == "未分" && !r.Records.Any(p => p.submit_time != null);
            string? channelState = method == ShareWorkflowRules.Bank ? (status == "已分" ? "已记账" : "待银行转账")
                : status == "已分" ? "渠道成功"
                : r.Records.Any(p => p.submit_time != null && p.response_time == null) ? "处理中"
                : r.Records.Any(p => p.submit_time != null && p.response_time != null && p.success != true) ? "渠道失败"
                : r.Records.Any(p => p.success == true) ? "部分成功，待核对"
                : method == ShareWorkflowRules.Manual ? "待管理员提交"
                : distributor != null && !SystemTicket(r) && s.approved_at == null ? "待审核" : "待自动提交";
            string? channelMessage = r.Records.FirstOrDefault(p => p.submit_time != null && p.response_time != null && p.success != true)?.response_content;
            if (SystemTicket(r) && isUnsent && relations.TryGetValue(s.relation_id, out var ticketReceiver)
                && ticketReceiver.staff_id is int ticketStaffId && !activeReceiverStaffIds.Contains(ticketStaffId))
            {
                channelState = "已停止";
                channelMessage = "员工已离职，停止雪票 1 元分账";
            }
            string? channelProblem = null;
            if (distributor != null && !SystemTicket(r) && method != ShareWorkflowRules.Bank && isUnsent)
            {
                if (r.Records.Count == 0 || Math.Abs(Math.Round(r.Records.Sum(p => p.amount), 2) - Math.Round(s.amount, 2)) > 0.001)
                    channelProblem = "渠道分账记录不完整，请核对金额或改选银行转账";
                else if (!relations.TryGetValue(s.relation_id, out var relation)) channelProblem = "分销商收款信息不存在";
                else foreach (var record in r.Records)
                {
                    if (!payments.TryGetValue(record.payment_id, out var payment) || !ShareWorkflowRules.CanUsePayment(r.Order, payment))
                    { channelProblem = "订单支付已不可分账，请核对支付状态或改选银行转账"; break; }
                    channelProblem = ShareReceiverRules.Problem(relation, relation.binds ?? new List<ShareRelationBind>(), payment.pay_method);
                    if (channelProblem != null) break;
                }
                if (channelProblem == null && r.Records.GroupBy(p => p.payment_id)
                    .Any(g => g.Sum(p => p.amount) > WanlongShareRules.ShareBase(payments[g.Key]) + 0.001))
                    channelProblem = "付款可分金额不足，请核对退款或分账金额";
                if (channelProblem == null && method == ShareWorkflowRules.Auto && r.Records.Any(p => p.can_not_share))
                    channelProblem = r.Records.First(p => p.can_not_share).memo ?? "分账记录当前不可自动提交";
                if (channelProblem != null) { channelState = "无法提交"; channelMessage = channelProblem; }
            }
            if (channelMessage?.Length > 200) channelMessage = channelMessage[..200];
            string payee = distributor?.name ?? relations.GetValueOrDefault(s.relation_id)?.name ?? "";
            string? plan = s.plan_date?.ToString(DateFormat)
                ?? (r.Order.type == "租赁" && r.Order.close_date != null ? r.Order.close_date.Value.Date.AddDays(1).ToString(DateFormat) : null);
            string applicant = applicantIds.TryGetValue(s.id, out int sid) ? staffNames.GetValueOrDefault(sid, "未知") : "系统";
            return new ShareListItem(s.id, s.order_id, r.Order.code ?? r.Order.id.ToString(), r.Order.shop ?? "", r.Order.type ?? "", payee, s.amount, applicant,
                mode, s.share_method == null && method == ShareWorkflowRules.Bank ? ShareListRules.ManualReason(r.Facts) : null,
                status, plan, done, BatchColumn(r, status), BankSelectable(r),
                skipassIds.TryGetValue(r.Order.id, out int k) ? k : null, (s.plan_date ?? s.create_date).ToString(DateFormat),
                s.create_date.ToString(TimeFormat), s.auto_share, distributor?.type,
                distributor != null && !SystemTicket(r) && method == ShareWorkflowRules.Auto ? (s.approved_at == null ? "待审核" : "已通过") : null,
                distributor != null && !SystemTicket(r) && method == ShareWorkflowRules.Auto && s.approved_at == null && isUnsent && channelProblem == null,
                distributor != null && method == ShareWorkflowRules.Manual && isUnsent && channelProblem == null, channelState, channelMessage);
        }).ToList();

        if (q.DistributorOnly) items = items.Where(i => i.distributorType != null).ToList();
        if (!string.IsNullOrWhiteSpace(q.DistributorType)) items = items.Where(i => i.distributorType == q.DistributorType.Trim()).ToList();
        if (!string.IsNullOrWhiteSpace(q.Status)) items = items.Where(i => i.status == q.Status).ToList();
        if (!string.IsNullOrWhiteSpace(q.Mode)) items = items.Where(i => i.mode == q.Mode).ToList();
        string key = (q.Keyword ?? "").Trim();
        if (key != "") items = items.Where(i => i.orderCode.Contains(key, StringComparison.OrdinalIgnoreCase) || i.payee.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();

        items = items.OrderBy(i => i.status == "已分" ? 1 : 0).ThenByDescending(i => i.listDate).ThenByDescending(i => i.orderShareId).ToList();
        int size = Math.Clamp(q.PageSize, 1, 200);
        int page = Math.Max(1, q.PageIndex);
        return new ShareListPage(items.Skip((page - 1) * size).Take(size).ToList(), items.Count, Math.Round(items.Sum(i => i.amount), 2), items.Count(i => i.selectable));
    }

    /// <summary>默认批次号 MS-yyyyMMdd-NN：今天已有几个批次，NN 就是下一个序号。</summary>
    public async Task<string> NextBatchNoAsync()
    {
        DateTime today = DateTime.Today;
        int count = await db.shareBatch.AsNoTracking().CountAsync(b => b.create_date >= today && b.create_date < today.AddDays(1));
        return ShareListRules.DefaultBatchNo(today, count);
    }

    public async Task ApproveAutoAsync(Staff op, int orderShareId)
    {
        if (!StaffAccountRules.IsAdmin(op)) throw new ShareException("只有管理员可以审核分账", 3);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        Row r = (await LoadRowsAsync(new[] { orderShareId }, null, null, null)).SingleOrDefault()
            ?? throw new ShareException("分账申请不存在");
        if (SystemTicket(r) || !await ShareQueries.IsIntroducerRelationAsync(db, r.Share.relation_id) || Method(r) != ShareWorkflowRules.Auto
            || r.Share.approved_at != null || Status(r) == "已分")
            throw new ShareException("这条申请不需要审核或已处理");
        await ShareChannelValidation.CheckAsync(db, r.Share, r.Records);
        r.Share.approved_at = DateTime.Now;
        r.Share.approved_by_staff_id = op.id;
        db.orderShare.Update(r.Share);
        foreach (var paymentShare in r.Records.Where(p => p.can_not_share))
        {
            paymentShare.can_not_share = false;
            paymentShare.update_date = DateTime.Now;
            db.paymentShare.Update(paymentShare);
        }
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order_share", "approved_at", r.Share.id, "审核自动分账", null, op.id,
            null, r.Share.approved_at.Value.ToString("yyyy-MM-dd HH:mm:ss"), "管理员审核通过"));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// 银行转账：把可记账的应分归到同一个新批次。只记账，不调用任何支付接口。
    /// 在一个事务里：任何一条已不满足条件、批次号重复，整批拒绝、什么都不写。
    /// </summary>
    public async Task<ManualResult> CompleteManualAsync(Staff op, IReadOnlyList<int> orderShareIds, string? batchNo)
    {
        if (!StaffAccountRules.IsAdmin(op)) throw new ShareException("只有管理员可以处理分账", 3);
        List<int> ids = (orderShareIds ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0) throw new ShareException("请先勾选要分账的条目");
        if (ids.Count > MaxBatchSize) throw new ShareException("一次最多处理 " + MaxBatchSize + " 条");
        string? problem = ShareListRules.BatchNoProblem(batchNo);
        if (problem != null) throw new ShareException(problem);
        string no = batchNo!.Trim();
        try
        {
            return await CompleteCoreAsync(op, ids, no);
        }
        catch (DbUpdateException)
        {
            // 并发时唯一索引兜底：别人刚用了这个批次号
            db.ChangeTracker.Clear();
            if (await db.shareBatch.AsNoTracking().AnyAsync(b => b.batch_no == no)) throw new ShareException("批次号已存在");
            throw;
        }
    }

    private async Task<ManualResult> CompleteCoreAsync(Staff op, List<int> ids, string no)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        if (await db.shareBatch.AsNoTracking().AnyAsync(b => b.batch_no == no)) throw new ShareException("批次号已存在");
        List<Row> rows = await LoadRowsAsync(ids, null, null, null);
        int bad = ids.Count(id => rows.FirstOrDefault(r => r.Share.id == id) is not Row r || !BankSelectable(r));
        if (bad > 0) throw new ShareException("有 " + bad + " 条已被处理或不能银行转账记账，请刷新后重试");

        double total = Math.Round(rows.Sum(r => r.Share.amount), 2);
        ShareBatch batch = new() { batch_no = no, share_count = rows.Count, total_amount = total, created_by_staff_id = op.id, create_date = DateTime.Now };
        db.shareBatch.Add(batch);
        await db.SaveChangesAsync();

        foreach (Row r in rows)
        {
            r.Share.batch_id = batch.id;
            db.orderShare.Update(r.Share);
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order_share", "batch_id", r.Share.id, "手动分账", null, op.id, null,
                batch.id.ToString(), "银行转账批次 " + no + "，金额 " + r.Share.amount.ToString("0.##")));
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        db.ChangeTracker.Clear();
        return new ManualResult(no, rows.Count, total);
    }

    // 手动批次取批次创建时间；自动取最后一笔成功记录的回执时间（没有则提交时间）
    private static string? SharedAt(Row r)
    {
        if (r.Batch != null) return r.Batch.create_date.ToString(TimeFormat);
        DateTime? last = r.Records.Where(p => p.success == true).Select(p => p.response_time ?? p.submit_time).Where(t => t != null).Max();
        return last?.ToString(TimeFormat);
    }

    // 银行转账才显示批次号。
    private static string? BatchColumn(Row r, string status)
    {
        if (r.Batch != null) return r.Batch.batch_no;
        return null;
    }
}
