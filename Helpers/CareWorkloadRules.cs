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
    ///   完成的时刻 = 有效的「发板」/「强行索回」任务（状态已完成 / 强行中止）的 end_time。
    ///   取消的装备走的也是这条发板任务（care.is_cancel），所以取消算「完成」，并单独标出取消了几块。
    ///
    /// 统计窗口 [D 00:00, asOf)：
    ///   ① 初始待完成 = D 00:00 时已经进来、还没完成的
    ///   ② 新增       = 窗口内进来的
    ///   ③ 完成       = 窗口内完成的（含取消）
    ///   ④ 待完成     = asOf 时已经进来、还没完成的；恒有 ① + ② − ③ = ④
    ///   无操作的待完成 = ④ 里窗口内一次操作（任务开始 / 结束，含安全检查）都没有的
    ///   今日养护雪板数 = 窗口内有过操作的装备（完成和没完成的都算）
    ///   工序数量     = 窗口内「已完成」的有效任务，每条算 1，按完成人（staff_id）分开
    ///
    /// 截止时刻：asOf = min(当前时刻, D 22:00)。过去的日子都按当天 22:00 截止，今天 22:00 前实时、22:00 后冻结。
    /// </summary>
    public static class CareWorkloadRules
    {
        /// <summary>营业截止时刻（小时）。22:00 以后的操作不计入任何一天。</summary>
        public const int CutoffHour = 22;

        // 工序的键（接口和快照里都用这些英文键，页面再映射成中文名）
        public const string Edge = "edge";               // 修刃
        public const string Wax = "wax";                 // 打蜡：热蜡 + 机打蜡 + 养板打蜡*
        public const string Scrape = "scrape";           // 刮蜡：刮蜡 + 养板刮蜡*
        public const string PatchBottom = "patchBottom"; // 补板底：维修任务且装备维修备注含「补板底」
        public const string Repair = "repair";           // 维修：其余的维修任务

        /// <summary>统计的工序，也是页面上展示的顺序。</summary>
        public static readonly string[] Processes = { Edge, Wax, Scrape, PatchBottom, Repair };

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

        public static bool IsFinishTask(CareTask task) => FinishTaskNames.Contains(Name(task));

        static bool IsFinishedStatus(CareTask task)
        {
            string status = (task.status ?? "").Trim();
            return status == "已完成" || status == "强行中止";
        }

        /// <summary>装备进入工作清单的时刻；没有任何有效任务（订单还没生效）返回 null。</summary>
        public static DateTime? EnteredAt(Care care)
        {
            DateTime? entered = null;
            foreach (CareTask task in care.tasks ?? new List<CareTask>())
            {
                if (!IsValid(task)) continue;
                if (entered == null || task.create_date < entered) entered = task.create_date;
            }
            return entered;
        }

        /// <summary>
        /// 装备完成（发板 / 强行索回已完成或强行中止）的时刻；还没完成返回 null。
        /// 取任务的 end_time，缺了退回 update_date、create_date；再不早于进入时刻，保证 ① + ② − ③ = ④。
        /// </summary>
        public static DateTime? FinishedAt(Care care)
        {
            DateTime? entered = EnteredAt(care);
            if (entered == null) return null;
            DateTime? finished = null;
            foreach (CareTask task in care.tasks ?? new List<CareTask>())
            {
                if (!IsValid(task) || !IsFinishTask(task) || !IsFinishedStatus(task)) continue;
                DateTime at = task.end_time ?? task.update_date ?? task.create_date;
                if (finished == null || at < finished) finished = at;
            }
            if (finished == null) return null;
            return finished < entered ? entered : finished;
        }

        /// <summary>
        /// 一条已完成任务算哪种工序；不计工作量的（安全检查、发板、寄存或快递……）返回 null。
        /// 补板底靠装备的维修备注（repair_memo）判断：任务自己的 memo 每次改状态都会被覆盖成场景字符串，不能用。
        /// 一条维修任务只归一类：备注里有「补板底」算补板底，否则算维修。
        /// </summary>
        public static string? Classify(CareTask task, Care care)
        {
            string name = Name(task);
            if (name == "修刃") return Edge;
            if (name == "热蜡" || name == "机打蜡" || name == "打蜡" || name.StartsWith("养板打蜡")) return Wax;
            if (name == "刮蜡" || name.StartsWith("养板刮蜡")) return Scrape;
            if (name == "维修")
                return (care.repair_memo ?? "").Contains("补板底") ? PatchBottom : Repair;
            return null;
        }

        static bool In(DateTime? time, DateTime start, DateTime end) => time != null && time >= start && time < end;

        /// <summary>窗口内这件装备有没有任何操作：任一有效任务开始过或结束过。</summary>
        static bool HasOperation(Care care, DateTime start, DateTime end) =>
            (care.tasks ?? new List<CareTask>()).Any(t => IsValid(t) && (In(t.start_time, start, end) || In(t.end_time, start, end)));

        public static CareWorkloadStat Compute(IEnumerable<Care> cares, DateTime day, DateTime asOf)
        {
            DateTime start = day.Date;
            var stat = new CareWorkloadStat { date = start.ToString("yyyy-MM-dd"), asOf = asOf };
            var people = new Dictionary<int, CareWorkloadStaff>();
            foreach (Care care in cares)
            {
                DateTime? entered = EnteredAt(care);
                if (entered == null) continue;
                DateTime? finished = FinishedAt(care);
                bool pendingAtStart = entered < start && (finished == null || finished >= start);
                bool pendingAtEnd = entered < asOf && (finished == null || finished >= asOf);
                if (pendingAtStart) stat.initialPending++;
                if (In(entered, start, asOf)) stat.added++;
                if (In(finished, start, asOf))
                {
                    stat.completed++;
                    if (care.is_cancel) stat.completedCancelled++;
                }
                if (pendingAtEnd) stat.pending++;
                bool operated = HasOperation(care, start, asOf);
                if (operated) stat.worked++;
                if (pendingAtEnd && !operated) stat.idlePending++;
                foreach (CareTask task in care.tasks ?? new List<CareTask>())
                {
                    if (!IsValid(task) || (task.status ?? "").Trim() != "已完成" || !In(task.end_time, start, asOf)) continue;
                    string? kind = Classify(task, care);
                    if (kind == null) continue;
                    stat.process[kind]++;
                    int key = task.staff_id ?? 0;
                    if (!people.TryGetValue(key, out CareWorkloadStaff? who))
                        people[key] = who = new CareWorkloadStaff { staffId = key, name = task.staff?.name ?? "" };
                    if (who.name == "" && task.staff != null) who.name = task.staff.name;
                    who.process[kind]++;
                }
            }
            stat.staff = people.Values.OrderBy(p => p.staffId == 0 ? 1 : 0).ThenByDescending(p => p.process.Values.Sum())
                .ThenBy(p => p.staffId).ToList();
            return stat;
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
        /// <summary>③ 窗口内完成（含取消）</summary>
        public int completed { get; set; }
        /// <summary>③ 里其中取消的</summary>
        public int completedCancelled { get; set; }
        /// <summary>④ asOf 时待完成（= ① + ② − ③）</summary>
        public int pending { get; set; }
        /// <summary>④ 里窗口内一次操作都没有的</summary>
        public int idlePending { get; set; }
        /// <summary>窗口内有过操作的装备数</summary>
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
