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
/// <param name="Mode">自动 / 手动，空 = 全部</param>
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
/// <param name="batch">手动 = 批次号；自动已分 = 自动提交日期 yyyyMMdd；未分为空</param>
/// <param name="listDate">COALESCE(plan_date, create_date) 的日期 yyyy-MM-dd，小程序用它判断「30 天前」</param>
public sealed record ShareListItem(int orderShareId, int orderId, string orderCode, string shop, string orderType, string payee, double amount,
    string applicant, string mode, string? manualReason, string status, string? planDate, string? sharedTime, string? batch, bool selectable,
    int? skipassId, string listDate, string applicationDate, bool autoShare, string? distributorType);

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
        List<Row> rows = await LoadRowsAsync(null, q.Type, q.StartDate, q.EndDate);
        List<int> shareIds = rows.Select(r => r.Share.id).ToList();

        // 收款人：介绍人取介绍人名称，否则取收款方名称
        List<int> relationIds = rows.Select(r => r.Share.relation_id).Distinct().ToList();
        Dictionary<int, string> relationNames = (await db.orderShareRelation.AsNoTracking().Where(r => relationIds.Contains(r.id)).ToListAsync())
            .ToDictionary(r => r.id, r => r.name ?? "");
        Dictionary<int, Introducer> introducers = await ShareQueries.IntroducersByRelationAsync(db, relationIds);

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
            string status = ShareListRules.Status(r.Facts);
            string? done = status == "已分" ? SharedAt(r) : null;
            Introducer? distributor = introducers.GetValueOrDefault(s.relation_id);
            string payee = distributor?.name ?? relationNames.GetValueOrDefault(s.relation_id) ?? "";
            string? plan = s.plan_date?.ToString(DateFormat)
                ?? (r.Order.type == "租赁" && r.Order.close_date != null ? r.Order.close_date.Value.Date.AddDays(1).ToString(DateFormat) : null);
            string applicant = applicantIds.TryGetValue(s.id, out int sid) ? staffNames.GetValueOrDefault(sid, "未知") : "系统";
            return new ShareListItem(s.id, s.order_id, r.Order.code ?? r.Order.id.ToString(), r.Order.shop ?? "", r.Order.type ?? "", payee, s.amount, applicant,
                ShareListRules.Mode(r.Facts), ShareListRules.ManualReason(r.Facts), status, plan, done, BatchColumn(r, status),
                ShareListRules.Selectable(r.Facts), skipassIds.TryGetValue(r.Order.id, out int k) ? k : null, (s.plan_date ?? s.create_date).ToString(DateFormat),
                s.create_date.ToString(TimeFormat), s.auto_share, distributor?.type);
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

    /// <summary>
    /// 手动分账：把「未分 + 手动」的应分一次记成已分，归到同一个新批次。只记账，不调用任何支付接口。
    /// 在一个事务里：任何一条已不满足条件、批次号重复，整批拒绝、什么都不写。
    /// </summary>
    public async Task<ManualResult> CompleteManualAsync(Staff op, IReadOnlyList<int> orderShareIds, string? batchNo)
    {
        if (!StaffAccountRules.IsManager(op)) throw new ShareException("只有店长以上可以处理分账", 3);
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
        int bad = ids.Count(id => rows.FirstOrDefault(r => r.Share.id == id) is not Row r || !ShareListRules.Selectable(r.Facts));
        if (bad > 0) throw new ShareException("有 " + bad + " 条已被处理或不能手动分账，请刷新后重试");

        double total = Math.Round(rows.Sum(r => r.Share.amount), 2);
        ShareBatch batch = new() { batch_no = no, share_count = rows.Count, total_amount = total, created_by_staff_id = op.id, create_date = DateTime.Now };
        db.shareBatch.Add(batch);
        await db.SaveChangesAsync();

        foreach (Row r in rows)
        {
            r.Share.batch_id = batch.id;
            db.orderShare.Update(r.Share);
            foreach (PaymentShare p in r.Records.Where(p => p.success != true))
            {
                p.success = true;
                p.submit_time ??= batch.create_date;      // 被拒绝过的保留当时的提交时间和拒绝回执，方便以后追溯
                p.response_time = batch.create_date;      // 微信冻结资金按「最后一笔成功回执 + 4 天」解冻，这里用记账时间
                string tag = "手动分账 批次 " + no;
                p.memo = string.IsNullOrWhiteSpace(p.memo) ? tag : p.memo + "；" + tag;
                p.update_date = batch.create_date;
                db.paymentShare.Update(p);
            }
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order_share", "batch_id", r.Share.id, "手动分账", null, op.id, null,
                batch.id.ToString(), "批次 " + no + "，金额 " + r.Share.amount.ToString("0.##")));
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

    // 手动 = 批次号；自动已分 = 自动提交的日期；未分为空
    private static string? BatchColumn(Row r, string status)
    {
        if (r.Batch != null) return r.Batch.batch_no;
        if (status != "已分") return null;
        return r.Records.Where(p => p.success == true && p.submit_time != null).Max(p => p.submit_time)?.ToString("yyyyMMdd");
    }
}
