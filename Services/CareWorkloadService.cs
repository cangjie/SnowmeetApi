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
    /// 范围：已生效的养护装备（care.valid = 1、task_flow_code 有值）、订单有效、是养护订单、不是测试单；
    /// 开单日期早于 2026-04-30 的老装备和非雪季养护不统计（见 CareWorkloadRules.IsConsidered）。
    /// 只算「窗口开始前没完成」且「截止前已经进来」的装备（= 当时的待完成工作集 + 当天新增）。
    /// 取数方式见 ComputeByShopAsync：三条窄查询 + 内存里筛，库里那几千块一直没发板的老装备不会拖慢它。
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
            DateTime asOf = CareWorkloadRules.AsOf(day, now);
            List<CareWorkloadInput> inputs = await LoadInputsAsync(day, asOf, shop);
            var result = new Dictionary<string, CareWorkloadStat>();
            foreach (var group in inputs.GroupBy(c => c.shop))
            {
                CareWorkloadStat stat = CareWorkloadRules.Compute(group, day, asOf);
                stat.shop = group.Key;
                stat.computedAt = now;
                result[group.Key] = stat;
            }
            return result;
        }

        /// <summary>取出这一天要参与统计的装备（统计和点数字看明细共用）。shop 不为空时只取这一家。</summary>
        private async Task<List<CareWorkloadInput>> LoadInputsAsync(DateTime day, DateTime asOf, string? shop)
        {
            DateTime start = day.Date;
            string? shopName = string.IsNullOrWhiteSpace(shop) ? null : shop.Trim();

            // 不能一次把「装备 + 全部任务」读出来、再在 SQL 里用 EXISTS / NOT EXISTS 去筛：生产库里有七千多块
            // 「早就进来、一直没发板」的老装备，那样的相关子查询要扫任务表几千遍，一条查询占满一个核好几分钟。
            // 所以拆成三条窄查询，筛选放在内存里做：
            //   ① 装备基本信息（不碰任务表）  ② 每件装备的进入 / 完成时刻（任务表扫一遍，CareWorkloadProgress 累加）  ③ 统计窗口内有过开始 / 结束的任务（很少）
            var careQuery = _db.care.Where(c => c.valid == 1 && c.task_flow_code != null
                && c.order != null && c.order.valid == 1 && c.order.type == "养护" && c.order.is_test == 0);
            if (shopName != null) careQuery = careQuery.Where(c => c.order!.shop.Trim() == shopName);
            // 不统计的不读：开单日期早于 2026-04-30 的老装备、非雪季养护（规则见 CareWorkloadRules.IsConsidered，内存里还会再判一次）
            DateTime legacyCutoff = CareWorkloadRules.LegacyCutoff;
            careQuery = careQuery.Where(c => c.order!.biz_date >= legacyCutoff && c.biz_type != CareWorkloadRules.SummerCareType);
            var cares = await careQuery
                .Select(c => new { c.id, shop = c.order!.shop, orderDate = c.order!.biz_date, c.biz_type, c.is_cancel, c.repair_memo })
                .AsNoTracking().ToListAsync();

            var taskRows = await _db.careTask.Where(t => t.valid == 1)
                .Select(t => new { t.care_id, t.task_name, t.status, t.create_date, t.end_time, t.update_date })
                .AsNoTracking().ToListAsync();
            var progress = new Dictionary<int, CareWorkloadProgress>();
            foreach (var t in taskRows)
            {
                if (!progress.TryGetValue(t.care_id, out CareWorkloadProgress? p)) progress[t.care_id] = p = new CareWorkloadProgress();
                p.Add((t.task_name ?? "").Trim(), (t.status ?? "").Trim(), t.create_date, t.end_time, t.update_date);
            }

            var activeRows = await _db.careTask.Where(t => t.valid == 1
                    && ((t.start_time != null && t.start_time >= start && t.start_time < asOf)
                        || (t.end_time != null && t.end_time >= start && t.end_time < asOf)))
                .Select(t => new { t.care_id, t.task_name, t.status, t.start_time, t.end_time, t.staff_id, staffName = t.staff != null ? t.staff.name : null })
                .AsNoTracking().ToListAsync();
            var active = activeRows.ToLookup(t => t.care_id);

            var inputs = new List<CareWorkloadInput>();
            foreach (var care in cares)
            {
                if (!progress.TryGetValue(care.id, out CareWorkloadProgress? p) || p.Entered == null) continue;   // 还没生效（没有有效任务）
                DateTime entered = p.Entered.Value;
                if (entered >= asOf) continue;                                                   // 截止时刻之后才进来
                DateTime? finished = p.Finished;                                                 // 工序都做完只差发板，或已发板
                if (finished != null && finished < start) continue;                              // 窗口开始前就已经完成的，和这天无关
                inputs.Add(new CareWorkloadInput
                {
                    careId = care.id, shop = (care.shop ?? "").Trim(), orderDate = care.orderDate, bizType = care.biz_type,
                    entered = entered, finished = finished,
                    isCancel = care.is_cancel, repairMemo = care.repair_memo,
                    tasks = active[care.id].Select(t => new CareWorkloadTask
                    {
                        name = (t.task_name ?? "").Trim(), status = (t.status ?? "").Trim(), start = t.start_time, end = t.end_time,
                        staffId = t.staff_id, staffName = t.staffName
                    }).ToList()
                });
            }

            return inputs;
        }

        /// <summary>
        /// 页面上某个数的明细：对应哪些装备（订单号、装备、顾客、进店 / 完成时间、任务进度），工序类的数还带上是谁在几点完成的。
        /// 永远按当前数据现算（截止时刻和统计一样）；过去的日子统计读的是快照，订单后来被改过时明细条数可能和快照里的数差一点。
        /// </summary>
        public async Task<CareWorkloadItems> GetItemsAsync(DateTime day, DateTime now, string? shop, string metric, int? staffId = null)
        {
            DateTime asOf = CareWorkloadRules.AsOf(day, now);
            string? shopName = string.IsNullOrWhiteSpace(shop) ? null : shop.Trim();
            bool isProcess = CareWorkloadRules.Processes.Contains(metric);
            List<CareWorkloadInput> inputs = await LoadInputsAsync(day, asOf, shopName);
            List<CareWorkloadMatch> matches = CareWorkloadRules.Select(inputs, day, asOf, metric, isProcess ? staffId : null);
            var result = new CareWorkloadItems
            {
                date = day.Date.ToString("yyyy-MM-dd"), asOf = asOf, shop = shopName, metric = metric, staffId = isProcess ? staffId : null,
                total = isProcess ? matches.Sum(m => m.hits.Count) : matches.Count
            };
            if (matches.Count == 0) return result;

            List<int> ids = matches.Select(m => m.care.careId).ToList();
            var cares = await _db.care.Where(c => ids.Contains(c.id))
                .Select(c => new
                {
                    c.id, c.order_id, c.equipment, c.brand, c.scale,
                    orderCode = c.order!.code, shop = c.order!.shop, orderDate = c.order!.biz_date,
                    contactName = c.order!.contact_name, contactGender = c.order!.contact_gender,
                    memberName = c.order!.member != null ? c.order!.member.real_name : null,
                    memberGender = c.order!.member != null ? c.order!.member.gender : null
                }).AsNoTracking().ToListAsync();
            var careById = cares.ToDictionary(c => c.id);
            var tasks = (await _db.careTask.Where(t => t.valid == 1 && ids.Contains(t.care_id))
                    .Select(t => new { t.id, t.care_id, t.task_name, t.status, t.sort }).AsNoTracking().ToListAsync())
                .ToLookup(t => t.care_id);

            foreach (CareWorkloadMatch match in matches)
            {
                if (!careById.TryGetValue(match.care.careId, out var care)) continue;
                result.items.Add(new CareWorkloadItem
                {
                    careId = care.id, orderId = care.order_id ?? 0, orderCode = care.orderCode ?? "", shop = (care.shop ?? "").Trim(),
                    equipment = care.equipment ?? "", brand = care.brand, scale = care.scale,
                    customerName = CalledName(care.contactName, care.contactGender, care.memberName, care.memberGender),
                    orderDate = care.orderDate, enteredAt = match.care.entered!.Value, finishedAt = match.care.finished,
                    isCancel = match.care.isCancel,
                    tasks = tasks[care.id].OrderBy(t => t.sort).ThenBy(t => t.id)
                        .Select(t => new CareWorkloadItemTask { id = t.id, name = (t.task_name ?? "").Trim(), status = (t.status ?? "").Trim(), sort = t.sort }).ToList(),
                    hits = match.hits.OrderBy(h => h.task.end).Select(h => new CareWorkloadItemHit
                    {
                        kind = h.kind, taskName = h.task.name, staffId = h.task.staffId ?? 0, staffName = h.task.staffName ?? "", endTime = h.task.end
                    }).ToList()
                });
            }
            // 排序：完成的按完成时间、工序按做完的时间、其余按进店时间，都是早的在前（压得最久的排最上面）
            result.items = (metric == CareWorkloadRules.MetricCompleted
                    ? result.items.OrderBy(i => i.finishedAt)
                    : isProcess ? result.items.OrderBy(i => i.hits.Count > 0 ? i.hits[0].endTime : null)
                    : result.items.OrderBy(i => (DateTime?)i.enteredAt))
                .ThenBy(i => i.careId).ToList();
            return result;
        }

        // 顾客称呼：订单上的联系人优先，没有就用会员姓名；带「先生 / 女士」
        static string CalledName(string? contactName, string? contactGender, string? memberName, string? memberGender)
        {
            string name = (contactName ?? "").Trim();
            string? gender = contactGender;
            if (name == "")
            {
                name = (memberName ?? "").Trim();
                gender = memberGender;
            }
            if (name == "") return "";
            return name + (gender == "男" ? " 先生" : gender == "女" ? " 女士" : "");
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

        /// <summary>D 日的数据已经最终（过了当天 22:00）才存快照；22:00 之前一律现算。</summary>
        public static bool IsFinal(DateTime day, DateTime now) => now >= day.Date.AddHours(CareWorkloadRules.CutoffHour);

        /// <summary>
        /// 接口用：**今天的数据永远现算**（22:00 以后也一样，截止时刻冻结在 22:00，结果和之后存下的快照一致）；
        /// 过去的日子有快照读快照，没有（那天服务没在 22:00 跑、或快照表读不出来比如表还没建）就按当天 22:00 现算。
        /// </summary>
        public async Task<CareWorkloadStat> GetAsync(DateTime day, DateTime now, string? shop = null)
        {
            if (day.Date < now.Date)
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
        /// 定时任务用：到了 22:00，把今天缺的快照存上（今天过了 22:00 才存；已经存过的不重复）。
        /// done 记着这个进程已经处理过的日子，一分钟一次反复调用时不再碰库；返回这次新存的行数。
        /// 没有往回补的功能：服务停了几天，那几天的历史查询会按当天 22:00 现算，数字和存快照一样。
        /// </summary>
        public async Task<int> EnsureSnapshotsAsync(DateTime now, ISet<DateTime>? done = null)
        {
            DateTime day = now.Date;
            if (!IsFinal(day, now)) return 0;
            if (done != null && done.Contains(day)) return 0;
            int saved = await SaveSnapshotAsync(day, now, "auto");
            done?.Add(day);
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

namespace SnowmeetApi.Services
{
    /// <summary>日养护工作列表里某个数的明细（CareWorkloadService.GetItemsAsync）。</summary>
    public class CareWorkloadItems
    {
        public string date { get; set; } = "";
        public DateTime asOf { get; set; }
        /// <summary>null = 全部门店</summary>
        public string? shop { get; set; }
        public string metric { get; set; } = "";
        /// <summary>只看这个人完成的工序；null = 不限</summary>
        public int? staffId { get; set; }
        /// <summary>和页面上那个数同口径：装备类的数 = 装备件数，工序类的数 = 完成的任务条数</summary>
        public int total { get; set; }
        public List<CareWorkloadItem> items { get; set; } = new();
    }

    public class CareWorkloadItem
    {
        public int careId { get; set; }
        public int orderId { get; set; }
        public string orderCode { get; set; } = "";
        public string shop { get; set; } = "";
        public string equipment { get; set; } = "";
        public string? brand { get; set; }
        public string? scale { get; set; }
        public string customerName { get; set; } = "";
        public DateTime orderDate { get; set; }
        /// <summary>进入工作清单（订单生效）的时刻</summary>
        public DateTime enteredAt { get; set; }
        /// <summary>完成的时刻（工序都做完只差发板，或已发板）；还没完成为 null</summary>
        public DateTime? finishedAt { get; set; }
        public bool isCancel { get; set; }
        /// <summary>这件装备的全部有效任务，按做的先后</summary>
        public List<CareWorkloadItemTask> tasks { get; set; } = new();
        /// <summary>工序类的数：当天完成的这类任务（谁、几点）</summary>
        public List<CareWorkloadItemHit> hits { get; set; } = new();
    }

    public class CareWorkloadItemTask
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public string status { get; set; } = "";
        public int sort { get; set; }
    }

    public class CareWorkloadItemHit
    {
        public string kind { get; set; } = "";
        public string taskName { get; set; } = "";
        public int staffId { get; set; }
        public string staffName { get; set; } = "";
        public DateTime? endTime { get; set; }
    }
}
