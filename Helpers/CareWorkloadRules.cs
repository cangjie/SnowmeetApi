using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 日养护工作列表的统计规则（崔洋的需求，2026-11-13）。纯函数，不碰数据库：输入一批已带任务的装备（care）、
    /// 要看的日子 D 和截止时刻 asOf，输出当天的进出统计和各工序工作量。
    ///
    /// 「一件雪板」= 一条 care。
    ///   进入工作清单的时刻 = 该装备有效任务里最早的 create_date（订单生效时一次性生成整条任务链）；
    ///   完成的时刻 = 养护工序都做完、只差发板的那一刻（除「发板」/「强行索回」外的有效任务全部已完成 / 强行中止，
    ///     取其中最晚的结束时间）——崔洋 2026-11-13：只差发板按完成算，顾客什么时候来取不影响工作量；
    ///     工序没做完就发板了（取消、强行索回）的，按发板那一刻算完成。
    ///   取消的装备走的也是发板任务（care.is_cancel），所以取消算「完成」，并单独标出取消了几块。
    ///
    /// 统计窗口 [D 00:00, asOf)：
    ///   ① 初始待完成 = D 00:00 时已经进来、还没完成的
    ///   ② 新增       = 窗口内进来的
    ///   ③ 完成       = 窗口内完成的（含取消）
    ///   ④ 待完成     = asOf 时已经进来、还没完成的；恒有 ① + ② − ③ = ④
    ///   无操作的待完成 = ④ 里窗口内一次养护操作都没有的
    ///   今日养护雪板数 = 窗口内有过养护操作的装备（完成和没完成的都算）
    ///     「养护操作」= 除「发板」/「强行索回」外的有效任务在窗口内开始或结束（含安全检查）。
    ///     发板只是把板子交还顾客，不算养护操作（崔洋 2026-11-13）：昨天做完、今天来取的板子不算今天养护过。
    ///   工序数量     = 窗口内「已完成」的有效任务，每条算 1，按完成人（staff_id）分开
    ///
    /// 截止时刻：asOf = min(当前时刻, D 22:00)。过去的日子都按当天 22:00 截止，今天 22:00 前实时、22:00 后冻结。
    ///
    /// 不统计的装备（崔洋 2026-11-13）：
    ///   · 开单日期早于 LegacyCutoff（2026-04-30）的：库里有四千多块上个雪季及更早的装备一直没发板
    ///     （其中 2025-05 一批是批量导入的），不排除的话「初始待完成」会是几千；
    ///   · 非雪季养护（寄存过夏、顾客回来取时才刮蜡发板）：一百多块长期压着，和当天的养护工作量无关，整类排除。
    ///   这两类装备不进任何数字（进出、有无操作、工序数量都不算）。
    /// </summary>
    public static class CareWorkloadRules
    {
        /// <summary>营业截止时刻（小时）。22:00 以后的操作不计入任何一天。</summary>
        public const int CutoffHour = 22;

        /// <summary>老装备分界：开单日期早于这一天的装备不统计。</summary>
        public static readonly DateTime LegacyCutoff = new(2026, 4, 30);

        /// <summary>非雪季养护（寄存过季、之后再取）的装备类型：整类不统计。</summary>
        public const string SummerCareType = "非雪季养护";

        /// <summary>这件装备要不要统计：开单日期不早于 2026-04-30，并且不是非雪季养护。</summary>
        public static bool IsConsidered(DateTime orderDate, string? bizType) =>
            orderDate >= LegacyCutoff && (bizType ?? "").Trim() != SummerCareType;

        // 工序的键（接口和快照里都用这些英文键，页面再映射成中文名）
        public const string Edge = "edge";               // 修刃
        public const string Wax = "wax";                 // 打蜡：热蜡 + 机打蜡 + 养板打蜡*
        public const string Scrape = "scrape";           // 刮蜡：刮蜡 + 养板刮蜡*
        public const string PatchBottom = "patchBottom"; // 补板底：维修任务且装备维修备注含「补板底」
        public const string Repair = "repair";           // 维修：其余的维修任务

        /// <summary>统计的工序，也是页面上展示的顺序。</summary>
        public static readonly string[] Processes = { Edge, Wax, Scrape, PatchBottom, Repair };

        // 进出和有无操作这几个数的键（点页面上的数字看明细时用，和 CareWorkloadStat 的字段同名）
        public const string MetricInitialPending = "initialPending";
        public const string MetricAdded = "added";
        public const string MetricCompleted = "completed";
        public const string MetricPending = "pending";
        public const string MetricIdlePending = "idlePending";
        public const string MetricWorked = "worked";

        /// <summary>按装备数的那几个数。</summary>
        public static readonly string[] BoardMetrics =
            { MetricInitialPending, MetricAdded, MetricCompleted, MetricPending, MetricIdlePending, MetricWorked };

        /// <summary>页面上能点开看明细的数：进出、有无操作（按装备），或者某个工序（按完成的任务）。</summary>
        public static bool IsMetric(string? metric) => metric != null && (BoardMetrics.Contains(metric) || Processes.Contains(metric));

        /// <summary>工序键 → 页面上的中文名。</summary>
        public static readonly Dictionary<string, string> Labels = new()
        {
            [Edge] = "修刃", [Wax] = "打蜡", [Scrape] = "刮蜡", [PatchBottom] = "补板底", [Repair] = "维修"
        };

        static readonly string[] FinishTaskNames = { "发板", "强行索回" };

        /// <summary>D 日的截止时刻：当天 22:00；今天还没到 22:00 就取当前时刻；还没到的日子窗口为空（取当天 00:00）。</summary>
        public static DateTime AsOf(DateTime day, DateTime now)
        {
            DateTime cutoff = day.Date.AddHours(CutoffHour);
            DateTime at = now < cutoff ? now : cutoff;
            return at < day.Date ? day.Date : at;
        }

        static string Name(CareTask task) => (task.task_name ?? "").Trim();

        static bool IsValid(CareTask task) => task.valid == 1;

        public static bool IsFinishTask(CareTask task) => IsFinishTaskName(Name(task));

        /// <summary>是不是「发板」/「强行索回」（已去掉首尾空白的任务名）。</summary>
        public static bool IsFinishTaskName(string name) => FinishTaskNames.Contains(name);

        /// <summary>发板 / 强行索回的状态算不算「已结束」：已完成、强行中止。</summary>
        public static bool IsFinishedStatusName(string status) => status == "已完成" || status == "强行中止";

        static CareWorkloadProgress ProgressOf(Care care)
        {
            var progress = new CareWorkloadProgress();
            foreach (CareTask task in care.tasks ?? new List<CareTask>())
            {
                if (!IsValid(task)) continue;
                progress.Add(Name(task), (task.status ?? "").Trim(), task.create_date, task.end_time, task.update_date);
            }
            return progress;
        }

        /// <summary>装备进入工作清单的时刻；没有任何有效任务（订单还没生效）返回 null。</summary>
        public static DateTime? EnteredAt(Care care) => ProgressOf(care).Entered;

        /// <summary>装备完成的时刻（工序都做完只差发板，或已经发板 / 强行索回）；还没完成返回 null。算法见 CareWorkloadProgress。</summary>
        public static DateTime? FinishedAt(Care care) => ProgressOf(care).Finished;

        /// <summary>
        /// 一条已完成任务算哪种工序；不计工作量的（安全检查、发板、寄存或快递……）返回 null。
        /// 补板底靠装备的维修备注（repair_memo）判断：任务自己的 memo 每次改状态都会被覆盖成场景字符串，不能用。
        /// 一条维修任务只归一类：备注里有「补板底」算补板底，否则算维修。
        /// </summary>
        public static string? Classify(CareTask task, Care care) => Classify(Name(task), care.repair_memo);

        public static string? Classify(string name, string? repairMemo)
        {
            if (name == "修刃") return Edge;
            if (name == "热蜡" || name == "机打蜡" || name == "打蜡" || name.StartsWith("养板打蜡")) return Wax;
            if (name == "刮蜡" || name.StartsWith("养板刮蜡")) return Scrape;
            if (name == "维修")
                return (repairMemo ?? "").Contains("补板底") ? PatchBottom : Repair;
            return null;
        }

        static bool In(DateTime? time, DateTime start, DateTime end) => time != null && time >= start && time < end;

        /// <summary>把一件带任务的装备整理成统计用的精简输入（测试和小批量用；服务从库里直接取精简数据，不走这里）。</summary>
        public static CareWorkloadInput ToInput(Care care) => new()
        {
            careId = care.id,
            shop = (care.order?.shop ?? "").Trim(),
            orderDate = care.order?.biz_date ?? care.create_date,
            bizType = care.biz_type,
            entered = EnteredAt(care),
            finished = FinishedAt(care),
            isCancel = care.is_cancel,
            repairMemo = care.repair_memo,
            tasks = (care.tasks ?? new List<CareTask>()).Where(IsValid).Select(t => new CareWorkloadTask
            {
                name = Name(t), status = (t.status ?? "").Trim(), start = t.start_time, end = t.end_time,
                staffId = t.staff_id, staffName = t.staff?.name
            }).ToList()
        };

        public static CareWorkloadStat Compute(IEnumerable<Care> cares, DateTime day, DateTime asOf) =>
            Compute(cares.Select(ToInput), day, asOf);

        /// <summary>
        /// 一件装备在这一天的统计里算什么：进出、有无养护操作、当天完成了哪些工序。不统计的装备（还没生效、老装备、非雪季养护）返回 null。
        /// 页面上的每个数、以及点开数字看到的明细，都从这里来，所以数字和明细不会对不上。
        /// tasks 只需要带窗口内开始 / 结束过的任务；进入时刻 entered 和完成时刻 finished 由调用方算好（finished 已不早于 entered），
        /// 这样库里几千块「早就进来、一直没发板」的老装备不用把任务一条条读出来。
        /// </summary>
        public static CareWorkloadFacts? Judge(CareWorkloadInput care, DateTime day, DateTime asOf)
        {
            if (care.entered == null) return null;
            if (!IsConsidered(care.orderDate, care.bizType)) return null;      // 老装备、非雪季养护不统计
            DateTime start = day.Date;
            DateTime entered = care.entered.Value;
            DateTime? finished = care.finished;
            var facts = new CareWorkloadFacts
            {
                initialPending = entered < start && (finished == null || finished >= start),
                added = In(entered, start, asOf),
                completed = In(finished, start, asOf),
                pending = entered < asOf && (finished == null || finished >= asOf),
                // 养护操作：发板 / 强行索回不算（只是把板子交还顾客）
                worked = care.tasks.Any(t => !IsFinishTaskName(t.name) && (In(t.start, start, asOf) || In(t.end, start, asOf)))
            };
            facts.cancelled = facts.completed && care.isCancel;
            facts.idlePending = facts.pending && !facts.worked;
            foreach (CareWorkloadTask task in care.tasks)
            {
                if (task.status != "已完成" || !In(task.end, start, asOf)) continue;
                string? kind = Classify(task.name, care.repairMemo);
                if (kind != null) facts.hits.Add(new CareWorkloadHit { kind = kind, task = task });
            }
            return facts;
        }

        /// <summary>统计的核心：把每件装备的判定（Judge）加起来。</summary>
        public static CareWorkloadStat Compute(IEnumerable<CareWorkloadInput> cares, DateTime day, DateTime asOf)
        {
            var stat = new CareWorkloadStat { date = day.Date.ToString("yyyy-MM-dd"), asOf = asOf };
            var people = new Dictionary<int, CareWorkloadStaff>();
            foreach (CareWorkloadInput care in cares)
            {
                CareWorkloadFacts? facts = Judge(care, day, asOf);
                if (facts == null) continue;
                if (facts.initialPending) stat.initialPending++;
                if (facts.added) stat.added++;
                if (facts.completed) stat.completed++;
                if (facts.cancelled) stat.completedCancelled++;
                if (facts.pending) stat.pending++;
                if (facts.worked) stat.worked++;
                if (facts.idlePending) stat.idlePending++;
                foreach (CareWorkloadHit hit in facts.hits)
                {
                    stat.process[hit.kind]++;
                    int key = hit.task.staffId ?? 0;
                    if (!people.TryGetValue(key, out CareWorkloadStaff? who))
                        people[key] = who = new CareWorkloadStaff { staffId = key, name = hit.task.staffName ?? "" };
                    if (who.name == "" && !string.IsNullOrEmpty(hit.task.staffName)) who.name = hit.task.staffName;
                    who.process[hit.kind]++;
                }
            }
            stat.staff = people.Values.OrderBy(p => p.staffId == 0 ? 1 : 0).ThenByDescending(p => p.process.Values.Sum())
                .ThenBy(p => p.staffId).ToList();
            return stat;
        }

        /// <summary>
        /// 页面上某个数对应的明细：哪些装备，以及（工序类的数）是哪几条完成的任务。
        /// metric 是 BoardMetrics 里的键或工序键；staffId 只对工序有效：只看这个人完成的（0 = 没记录完成人的）。
        /// 装备类的数：一件装备一条，条数 = 页面上的数；工序类的数：一件装备一条，hits 是它当天完成的这类任务，hits 的总条数 = 页面上的数。
        /// </summary>
        public static List<CareWorkloadMatch> Select(IEnumerable<CareWorkloadInput> cares, DateTime day, DateTime asOf, string metric, int? staffId = null)
        {
            var matches = new List<CareWorkloadMatch>();
            bool isProcess = Processes.Contains(metric);
            foreach (CareWorkloadInput care in cares)
            {
                CareWorkloadFacts? facts = Judge(care, day, asOf);
                if (facts == null) continue;
                if (isProcess)
                {
                    List<CareWorkloadHit> hits = facts.hits
                        .Where(h => h.kind == metric && (staffId == null || (h.task.staffId ?? 0) == staffId)).ToList();
                    if (hits.Count > 0) matches.Add(new CareWorkloadMatch { care = care, facts = facts, hits = hits });
                    continue;
                }
                bool hit = metric switch
                {
                    MetricInitialPending => facts.initialPending,
                    MetricAdded => facts.added,
                    MetricCompleted => facts.completed,
                    MetricPending => facts.pending,
                    MetricIdlePending => facts.idlePending,
                    MetricWorked => facts.worked,
                    _ => false
                };
                if (hit) matches.Add(new CareWorkloadMatch { care = care, facts = facts });
            }
            return matches;
        }

        /// <summary>
        /// 把几个门店的统计加成「全部门店」：数量相加，同一个人的各工序合并。
        /// 所有指标都是按装备累加的，所以各店相加和直接对全部装备算结果一样。
        /// </summary>
        public static CareWorkloadStat Merge(IEnumerable<CareWorkloadStat> parts, DateTime day, DateTime asOf)
        {
            var total = new CareWorkloadStat { date = day.Date.ToString("yyyy-MM-dd"), asOf = asOf };
            var people = new Dictionary<int, CareWorkloadStaff>();
            foreach (CareWorkloadStat part in parts)
            {
                total.initialPending += part.initialPending;
                total.added += part.added;
                total.completed += part.completed;
                total.completedCancelled += part.completedCancelled;
                total.pending += part.pending;
                total.idlePending += part.idlePending;
                total.worked += part.worked;
                foreach (string kind in Processes) total.process[kind] += part.process.GetValueOrDefault(kind);
                foreach (CareWorkloadStaff p in part.staff)
                {
                    if (!people.TryGetValue(p.staffId, out CareWorkloadStaff? who))
                        people[p.staffId] = who = new CareWorkloadStaff { staffId = p.staffId, name = p.name };
                    if (who.name == "") who.name = p.name;
                    foreach (string kind in Processes) who.process[kind] += p.process.GetValueOrDefault(kind);
                }
            }
            total.staff = people.Values.OrderBy(p => p.staffId == 0 ? 1 : 0).ThenByDescending(p => p.process.Values.Sum())
                .ThenBy(p => p.staffId).ToList();
            return total;
        }
    }

    /// <summary>
    /// 一件装备的进度累加器：把它的有效任务一条条喂进来，得到进入时刻和完成时刻。规则类（按 Care 算）和服务（按库里的任务行算）共用。
    /// 完成时刻取下面两个里较早的一个，都没有就是还没完成：
    ///   · 工序都做完、只差发板：除「发板」/「强行索回」外至少有一条有效任务，且全部已完成 / 强行中止，取其中最晚的结束时间；
    ///   · 已经发板 / 强行索回（含取消）：该任务的结束时间。
    /// 时间取 end_time，缺了退回 update_date、create_date；完成时刻不早于进入时刻，保证 ① + ② − ③ = ④。
    /// </summary>
    public sealed class CareWorkloadProgress
    {
        DateTime? _entered, _handedOut, _workDone;
        int _serviceTasks, _openServiceTasks;

        /// <param name="name">任务名（已去首尾空白）</param>
        /// <param name="status">任务状态（已去首尾空白）</param>
        public void Add(string name, string status, DateTime created, DateTime? end, DateTime? updated)
        {
            if (_entered == null || created < _entered) _entered = created;
            bool done = CareWorkloadRules.IsFinishedStatusName(status);
            DateTime at = end ?? updated ?? created;
            if (CareWorkloadRules.IsFinishTaskName(name))
            {
                if (done && (_handedOut == null || at < _handedOut)) _handedOut = at;
                return;
            }
            _serviceTasks++;
            if (!done)
            {
                _openServiceTasks++;
                return;
            }
            if (_workDone == null || at > _workDone) _workDone = at;
        }

        /// <summary>进入工作清单的时刻；一条任务都没有为 null。</summary>
        public DateTime? Entered => _entered;

        /// <summary>完成时刻；还没完成为 null。</summary>
        public DateTime? Finished
        {
            get
            {
                if (_entered == null) return null;
                DateTime? finished = _handedOut;
                bool onlyHandOutLeft = _serviceTasks > 0 && _openServiceTasks == 0 && _workDone != null;
                if (onlyHandOutLeft && (finished == null || _workDone < finished)) finished = _workDone;
                if (finished == null) return null;
                return finished < _entered ? _entered : finished;
            }
        }
    }

    /// <summary>一件装备在某一天的判定结果（CareWorkloadRules.Judge）。</summary>
    public sealed class CareWorkloadFacts
    {
        public bool initialPending { get; set; }
        public bool added { get; set; }
        public bool completed { get; set; }
        /// <summary>当天完成、而且是取消的</summary>
        public bool cancelled { get; set; }
        public bool pending { get; set; }
        /// <summary>当天有过养护操作（发板不算）</summary>
        public bool worked { get; set; }
        public bool idlePending { get; set; }
        /// <summary>当天完成的、计工作量的任务</summary>
        public List<CareWorkloadHit> hits { get; set; } = new();
    }

    /// <summary>当天完成的一条计工作量的任务，和它算哪种工序。</summary>
    public sealed class CareWorkloadHit
    {
        public string kind { get; set; } = "";
        public CareWorkloadTask task { get; set; } = new();
    }

    /// <summary>某个数的明细里的一件装备（CareWorkloadRules.Select）。</summary>
    public sealed class CareWorkloadMatch
    {
        public CareWorkloadInput care { get; set; } = new();
        public CareWorkloadFacts facts { get; set; } = new();
        /// <summary>工序类的数：这件装备当天完成的这类任务；装备类的数为空</summary>
        public List<CareWorkloadHit> hits { get; set; } = new();
    }

    /// <summary>统计用的精简输入：一件装备（见 CareWorkloadRules.Compute）。</summary>
    public class CareWorkloadInput
    {
        public int careId { get; set; }
        public string shop { get; set; } = "";
        /// <summary>开单日期（订单的 biz_date）；早于 CareWorkloadRules.LegacyCutoff 的不统计</summary>
        public DateTime orderDate { get; set; }
        /// <summary>care.biz_type；「非雪季养护」整类不统计</summary>
        public string? bizType { get; set; }
        /// <summary>进入工作清单的时刻（最早的有效任务创建时间）；没有有效任务 = 订单还没生效，不统计</summary>
        public DateTime? entered { get; set; }
        /// <summary>完成的时刻（工序都做完只差发板，或已发板 / 强行索回）；还没完成为 null；不早于 entered</summary>
        public DateTime? finished { get; set; }
        public bool isCancel { get; set; }
        public string? repairMemo { get; set; }
        /// <summary>只需带统计窗口内开始 / 结束过的有效任务</summary>
        public List<CareWorkloadTask> tasks { get; set; } = new();
    }

    public class CareWorkloadTask
    {
        public string name { get; set; } = "";
        public string status { get; set; } = "";
        public DateTime? start { get; set; }
        public DateTime? end { get; set; }
        public int? staffId { get; set; }
        public string? staffName { get; set; }
    }

    /// <summary>某一天（或某一天某门店）的统计结果。</summary>
    public class CareWorkloadStat
    {
        public string date { get; set; } = "";
        public DateTime asOf { get; set; }
        /// <summary>统计的门店；null = 全部门店</summary>
        public string? shop { get; set; }
        /// <summary>数据来源：live 现算 / snapshot 每天存下来的快照</summary>
        public string source { get; set; } = "live";
        /// <summary>这份数据的生成时间（现算 = 当前，快照 = 存快照的时刻）</summary>
        public DateTime computedAt { get; set; } = DateTime.Now;
        /// <summary>① D 00:00 时待完成的装备数</summary>
        public int initialPending { get; set; }
        /// <summary>② 窗口内新增</summary>
        public int added { get; set; }
        /// <summary>③ 窗口内完成：工序都做完只差发板，或已发板（含取消）</summary>
        public int completed { get; set; }
        /// <summary>③ 里其中取消的</summary>
        public int completedCancelled { get; set; }
        /// <summary>④ asOf 时待完成（= ① + ② − ③）</summary>
        public int pending { get; set; }
        /// <summary>④ 里窗口内一次养护操作都没有的（发板不算操作）</summary>
        public int idlePending { get; set; }
        /// <summary>窗口内有过养护操作的装备数（发板不算操作）</summary>
        public int worked { get; set; }
        /// <summary>各工序完成数，键见 CareWorkloadRules.Processes：edge 修刃 / wax 打蜡 / scrape 刮蜡 / patchBottom 补板底 / repair 维修</summary>
        public Dictionary<string, int> process { get; set; } = CareWorkloadRules.Processes.ToDictionary(p => p, p => 0);
        /// <summary>按完成人拆开的各工序数；staffId = 0 表示任务上没记录完成人</summary>
        public List<CareWorkloadStaff> staff { get; set; } = new();
    }

    public class CareWorkloadStaff
    {
        public int staffId { get; set; }
        public string name { get; set; } = "";
        public Dictionary<string, int> process { get; set; } = CareWorkloadRules.Processes.ToDictionary(p => p, p => 0);
    }
}
