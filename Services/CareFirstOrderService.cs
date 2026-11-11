using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services
{
    /// <summary>
    /// 会员养护首单（member.care_first_order_id / care_first_order_date）的读取与维护。
    /// 「新」标识、报表要知道某会员的养护首单，只通过这里：
    ///   · GetFirstOrderIdsAsync：读。记录仍有效就直接用；没有记录、或记录的订单已失效的，当场校正（所以不依赖回填做全）；
    ///     记录的订单行已不在订单表里的保留不动（旧单被归档 / 删除不改写历史）；
    ///   · ReconcileAsync：校正某个会员的记录（订单生效后、或发现不一致时调用）；
    ///   · MergeAsync：合并会员后，首单跟着走（目标会员取两边更早的，来源会员清空）；
    ///   · TryReconcileAsync / TryMergeAsync：同上，但出错只记日志不抛——挂在下单、合并主流程后面用，
    ///     记录出问题不能连累主流程，读的时候会再补。
    /// 判定规则在 CareFirstOrderRules / CareNewCustomerRules；这里只管查库和落库。
    /// 落库是直接 UPDATE member 的这两列，不走 SaveChanges，
    /// 所以不会把调用方上下文里还没保存的别的改动顺带存掉，也能在调用方的事务里用。
    /// </summary>
    public static class CareFirstOrderService
    {
        public static async Task<Dictionary<int, int>> GetFirstOrderIdsAsync(ApplicationDBContext db, IEnumerable<int> memberIds)
        {
            var result = new Dictionary<int, int>();
            List<int> ids = memberIds.Distinct().ToList();
            if (ids.Count == 0) return result;

            Dictionary<int, FirstOrderPoint> stored = await ReadStoredAsync(db, ids);
            List<int> storedOrderIds = stored.Values.Select(p => p.OrderId).Distinct().ToList();
            HashSet<(int OrderId, int MemberId)> valid = new();
            HashSet<int> present = new();
            if (storedOrderIds.Count > 0)
            {
                var validRows = await db.order.AsNoTracking().Where(o => storedOrderIds.Contains(o.id))
                    .Where(CareNewCustomerRules.ValidCareOrderExpression)
                    .Select(o => new { o.id, o.member_id }).ToListAsync();
                foreach (var v in validRows) if (v.member_id != null) valid.Add((v.id, (int)v.member_id));
                present = (await db.order.AsNoTracking().Where(o => storedOrderIds.Contains(o.id)).Select(o => o.id).ToListAsync()).ToHashSet();
            }

            foreach (int memberId in ids)
            {
                if (stored.TryGetValue(memberId, out FirstOrderPoint? rec)
                    && (valid.Contains((rec.OrderId, memberId)) || !present.Contains(rec.OrderId)))
                {
                    result[memberId] = rec.OrderId;     // 仍有效；或订单行已不在表里（保留历史）
                    continue;
                }
                FirstOrderPoint? point = await ReconcileAsync(db, memberId);
                if (point != null) result[memberId] = point.OrderId;
            }
            return result;
        }

        /// <summary>按规则校正某会员的首单记录，返回校正后记录的订单点（没有记录返回 null）。</summary>
        public static async Task<FirstOrderPoint?> ReconcileAsync(ApplicationDBContext db, int memberId)
        {
            FirstOrderPoint? stored = (await ReadStoredAsync(db, new List<int> { memberId })).GetValueOrDefault(memberId);

            var earliest = await db.order.AsNoTracking().Where(CareNewCustomerRules.ValidCareOrderExpression)
                .Where(o => o.member_id == memberId)
                .OrderBy(o => o.biz_date).ThenBy(o => o.id)
                .Select(o => new { o.id, o.biz_date }).FirstOrDefaultAsync();
            FirstOrderPoint? earliestPoint = earliest == null ? null : new FirstOrderPoint(earliest.id, earliest.biz_date);

            StoredOrderState state = StoredOrderState.Missing;
            if (stored != null)
            {
                int orderId = stored.OrderId;
                if (await db.order.AsNoTracking().Where(o => o.id == orderId && o.member_id == memberId)
                        .Where(CareNewCustomerRules.ValidCareOrderExpression).AnyAsync()) state = StoredOrderState.Valid;
                else if (await db.order.AsNoTracking().AnyAsync(o => o.id == orderId)) state = StoredOrderState.Invalid;
            }

            FirstOrderDecision decision = CareFirstOrderRules.Resolve(stored, state, earliestPoint);
            switch (decision.Action)
            {
                case FirstOrderAction.Upsert:
                    await WriteAsync(db, memberId, decision.OrderId, decision.BizDate);
                    return new FirstOrderPoint(decision.OrderId, decision.BizDate);
                case FirstOrderAction.Delete:
                    await WriteAsync(db, memberId, null, null);
                    return null;
                default:
                    return stored;
            }
        }

        public static async Task TryReconcileAsync(ApplicationDBContext db, int? memberId)
        {
            if (memberId == null) return;
            try
            {
                await ReconcileAsync(db, (int)memberId);
            }
            catch (Exception ex)
            {
                Console.WriteLine("会员养护首单校正失败 member=" + memberId + "：" + ex.Message);
            }
        }

        /// <summary>
        /// 会员合并（来源 → 目标）之后调用，订单已经改挂到目标会员。
        /// 目标会员的首单取两边记录里更早的那条（来源的首单订单可能已被归档、订单表里查不到，所以要带上它的记录而不只靠现算），
        /// 再按规则校正；来源会员的记录清空（它已不再是任何订单的会员）。
        /// </summary>
        public static async Task MergeAsync(ApplicationDBContext db, int sourceId, int targetId)
        {
            Dictionary<int, FirstOrderPoint> stored = await ReadStoredAsync(db, new List<int> { sourceId, targetId });
            if (stored.TryGetValue(sourceId, out FirstOrderPoint? source)
                && (!stored.TryGetValue(targetId, out FirstOrderPoint? target) || CareFirstOrderRules.IsEarlier(source, target)))
            {
                await WriteAsync(db, targetId, source.OrderId, source.BizDate);
            }
            await WriteAsync(db, sourceId, null, null);
            await ReconcileAsync(db, targetId);
        }

        public static async Task TryMergeAsync(ApplicationDBContext db, int sourceId, int targetId)
        {
            try
            {
                await MergeAsync(db, sourceId, targetId);
            }
            catch (Exception ex)
            {
                Console.WriteLine("合并会员后校正养护首单失败 source=" + sourceId + " target=" + targetId + "：" + ex.Message);
            }
        }

        // 只要订单 id 有值就算有记录；开单时间缺失（不该出现）按「无限晚」处理，让现算出的真实首单把它补上
        private static async Task<Dictionary<int, FirstOrderPoint>> ReadStoredAsync(ApplicationDBContext db, List<int> memberIds)
        {
            var rows = await db.memberCareFirstOrder.AsNoTracking()
                .Where(r => memberIds.Contains(r.member_id) && r.order_id != null)
                .Select(r => new { r.member_id, r.order_id, r.biz_date }).ToListAsync();
            return rows.ToDictionary(r => r.member_id, r => new FirstOrderPoint((int)r.order_id!, r.biz_date ?? DateTime.MaxValue));
        }

        // 直接改 member 这两列：不走 SaveChanges（不顺带存调用方上下文里别的改动），也不碰 member 的其它列（包括 update_date）。
        // 重复写同一个值无害，两个请求同时校正同一个会员也只会写出同一个结果。
        private static async Task WriteAsync(ApplicationDBContext db, int memberId, int? orderId, DateTime? bizDate)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE member SET care_first_order_id = {orderId}, care_first_order_date = {bizDate} WHERE id = {memberId}");
        }
    }
}
