using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services
{
    /// <summary>
    /// 日养护工作列表：从库里取出统计要用的装备，交给 CareWorkloadRules 算。
    /// 范围：已生效的养护装备（care.valid = 1、task_flow_code 有值）、订单有效、是养护订单、不是测试单。
    /// 只取「窗口开始前没完成」且「截止前已经进来」的装备（= 当时的待完成工作集 + 当天新增），
    /// 昨天以前就发板的不用读，所以每次只读几百条。
    /// </summary>
    public class CareWorkloadService
    {
        private readonly ApplicationDBContext _db;

        public CareWorkloadService(ApplicationDBContext db)
        {
            _db = db;
        }

        /// <summary>某天各门店的统计（门店名 → 统计）；shop 不为空时只算这一家。</summary>
        public async Task<Dictionary<string, CareWorkloadStat>> ComputeByShopAsync(DateTime day, DateTime now, string? shop = null)
        {
            DateTime start = day.Date;
            DateTime asOf = CareWorkloadRules.AsOf(day, now);
            string? shopName = string.IsNullOrWhiteSpace(shop) ? null : shop.Trim();
            var query = _db.care.Where(c => c.valid == 1 && c.task_flow_code != null
                && c.order != null && c.order.valid == 1 && c.order.type == "养护" && c.order.is_test == 0);
            if (shopName != null) query = query.Where(c => c.order!.shop.Trim() == shopName);
            List<Care> cares = await query
                // 截止前已经进来
                .Where(c => c.tasks.Any(t => t.valid == 1 && t.create_date < asOf))
                // 窗口开始前就已经完成的不用读
                .Where(c => !c.tasks.Any(t => t.valid == 1 && (t.task_name == "发板" || t.task_name == "强行索回")
                    && (t.status == "已完成" || t.status == "强行中止") && t.end_time != null && t.end_time < start))
                .Include(c => c.order)
                .Include(c => c.tasks.Where(t => t.valid == 1)).ThenInclude(t => t.staff)
                .AsSplitQuery().AsNoTracking()
                .ToListAsync();
            var result = new Dictionary<string, CareWorkloadStat>();
            foreach (var group in cares.GroupBy(c => (c.order?.shop ?? "").Trim()))
            {
                CareWorkloadStat stat = CareWorkloadRules.Compute(group, day, asOf);
                stat.shop = group.Key;
                stat.computedAt = now;
                result[group.Key] = stat;
            }
            return result;
        }

        /// <summary>某天的统计：指定门店就是那一家，不指定就是各店相加（人员合并）。没有数据时各项都是 0。</summary>
        public async Task<CareWorkloadStat> ComputeAsync(DateTime day, DateTime now, string? shop = null)
        {
            DateTime asOf = CareWorkloadRules.AsOf(day, now);
            string? shopName = string.IsNullOrWhiteSpace(shop) ? null : shop.Trim();
            Dictionary<string, CareWorkloadStat> byShop = await ComputeByShopAsync(day, now, shopName);
            CareWorkloadStat stat = shopName != null && byShop.TryGetValue(shopName, out CareWorkloadStat? one)
                ? one
                : CareWorkloadRules.Merge(byShop.Values, day, asOf);
            stat.shop = shopName;
            stat.computedAt = now;
            return stat;
        }
        // ───────────── 每日快照 ─────────────

        /// <summary>D 日的数据已经最终（过了当天 22:00）才存 / 读快照；22:00 之前一律现算。</summary>
        public static bool IsFinal(DateTime day, DateTime now) => now >= day.Date.AddHours(CareWorkloadRules.CutoffHour);

        /// <summary>
        /// 接口用：22:00 之后的日子有快照就读快照，没有（或快照表读不出来，比如表还没建）就现算；今天 22:00 前现算。
        /// </summary>
        public async Task<CareWorkloadStat> GetAsync(DateTime day, DateTime now, string? shop = null)
        {
            if (IsFinal(day, now))
            {
                try
                {
                    CareWorkloadStat? snapshot = await GetSnapshotAsync(day, shop);
                    if (snapshot != null) return snapshot;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("养护工作量快照读取失败，改为现算：" + ex.Message);
                }
            }
            return await ComputeAsync(day, now, shop);
        }

        /// <summary>
        /// 读快照：那天一行都没存过返回 null（调用方现算）；那天存过但这家店没有行 = 这家店那天没有养护装备，各项为 0。
        /// 不指定门店 = 各店的行相加（人员合并）。
        /// </summary>
        public async Task<CareWorkloadStat?> GetSnapshotAsync(DateTime day, string? shop = null)
        {
            DateTime date = day.Date;
            string? shopName = string.IsNullOrWhiteSpace(shop) ? null : shop.Trim();
            List<CareDailyWorkload> rows = await _db.careDailyWorkload.AsNoTracking().Where(r => r.stat_date == date).ToListAsync();
            if (rows.Count == 0) return null;
            DateTime asOf = rows.Max(r => r.as_of);
            DateTime savedAt = rows.Max(r => r.create_date);
            CareWorkloadStat stat;
            if (shopName == null)
            {
                stat = CareWorkloadRules.Merge(rows.Select(ToStat), day, asOf);
            }
            else
            {
                CareDailyWorkload? row = rows.FirstOrDefault(r => r.shop == shopName);
                stat = row != null ? ToStat(row) : new CareWorkloadStat { date = date.ToString("yyyy-MM-dd"), asOf = asOf };
            }
            stat.shop = shopName;
            stat.source = "snapshot";
            stat.computedAt = savedAt;
            return stat;
        }

        /// <summary>
        /// 给 D 日存快照（各门店一行）：数据没最终（还没到 D 22:00）不存；那天已经存过不重复存。
        /// 那天一个门店都没有养护装备就一行也不存。返回新存的行数。
        /// </summary>
        public async Task<int> SaveSnapshotAsync(DateTime day, DateTime now, string source = "auto")
        {
            if (!IsFinal(day, now)) return 0;
            DateTime date = day.Date;
            if (await _db.careDailyWorkload.AnyAsync(r => r.stat_date == date)) return 0;
            Dictionary<string, CareWorkloadStat> byShop = await ComputeByShopAsync(day, now);
            foreach (var (shop, stat) in byShop) _db.careDailyWorkload.Add(ToRow(date, shop, stat, source, now));
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // 另一个实例刚好也存了同一天（(stat_date, shop) 唯一索引拦下的）：以先存的为准
                _db.ChangeTracker.Clear();
                return 0;
            }
            return byShop.Count;
        }

        /// <summary>
        /// 定时任务用：把最近 backDays 天到今天里缺快照的日子补上。今天要过了 22:10 才存（等 22:00 的数据稳定）。
        /// done 记着这个进程已经处理过的日子，反复调用时不再重复查；返回这次新存的行数。
        /// </summary>
        public async Task<int> EnsureSnapshotsAsync(DateTime now, int backDays, ISet<DateTime>? done = null)
        {
            int saved = 0;
            for (int ago = backDays; ago >= 0; ago--)
            {
                DateTime day = now.Date.AddDays(-ago);
                if (now < day.AddHours(CareWorkloadRules.CutoffHour).AddMinutes(10)) continue;
                if (done != null && done.Contains(day)) continue;
                saved += await SaveSnapshotAsync(day, now, ago == 0 ? "auto" : "catchup");
                done?.Add(day);
            }
            return saved;
        }

        static CareWorkloadStat ToStat(CareDailyWorkload row)
        {
            var stat = new CareWorkloadStat
            {
                date = row.stat_date.ToString("yyyy-MM-dd"), asOf = row.as_of, shop = row.shop, source = "snapshot", computedAt = row.create_date,
                initialPending = row.initial_pending, added = row.added, completed = row.completed, completedCancelled = row.completed_cancel,
                pending = row.pending, idlePending = row.idle_pending, worked = row.worked
            };
            stat.process[CareWorkloadRules.Edge] = row.edge_count;
            stat.process[CareWorkloadRules.Wax] = row.wax_count;
            stat.process[CareWorkloadRules.Scrape] = row.scrape_count;
            stat.process[CareWorkloadRules.PatchBottom] = row.patch_bottom_count;
            stat.process[CareWorkloadRules.Repair] = row.repair_count;
            if (!string.IsNullOrWhiteSpace(row.staff_json))
            {
                try
                {
                    stat.staff = JsonSerializer.Deserialize<List<CareWorkloadStaff>>(row.staff_json) ?? new List<CareWorkloadStaff>();
                }
                catch (JsonException)
                {
                    stat.staff = new List<CareWorkloadStaff>();   // 人员明细坏了不影响总数
                }
            }
            return stat;
        }

        static CareDailyWorkload ToRow(DateTime date, string shop, CareWorkloadStat stat, string source, DateTime now) => new()
        {
            stat_date = date, shop = shop, as_of = stat.asOf,
            initial_pending = stat.initialPending, added = stat.added, completed = stat.completed, completed_cancel = stat.completedCancelled,
            pending = stat.pending, idle_pending = stat.idlePending, worked = stat.worked,
            edge_count = stat.process[CareWorkloadRules.Edge], wax_count = stat.process[CareWorkloadRules.Wax],
            scrape_count = stat.process[CareWorkloadRules.Scrape], patch_bottom_count = stat.process[CareWorkloadRules.PatchBottom],
            repair_count = stat.process[CareWorkloadRules.Repair],
            staff_json = JsonSerializer.Serialize(stat.staff), source = source, create_date = now
        };
    }
}
