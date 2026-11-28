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
    /// 员工查看养护订单时，给和顾客装备列表对不上的装备填上差异（只读，比较口径见 Helpers/CareEquipmentDiffRules）。
    /// 「匹配到」只认装备号：装备带着 member_equipment_id，且这条档案还有效、属于订单会员。
    /// 匹配不到就什么都不填，不按品牌、长度另找一件来比（用户 2026-11-28）。
    /// </summary>
    public static class CareEquipmentDiffService
    {
        public static async Task FillAsync(ApplicationDBContext db, SnowmeetApi.Models.Order order)
        {
            if (order.member_id == null || order.cares == null)
            {
                return;
            }
            try
            {
                List<Care> linked = order.cares.Where(c => !c.is_cancel && c.member_equipment_id > 0).ToList();
                if (linked.Count == 0)
                {
                    return;
                }
                int memberId = order.member_id.Value;
                List<int> ids = linked.Select(c => c.member_equipment_id!.Value).Distinct().ToList();
                Dictionary<int, MemberEquipment> records = await db.memberEquipment.AsNoTracking()
                    .Where(e => ids.Contains(e.id) && e.valid == 1 && e.member_id == memberId)
                    .ToDictionaryAsync(e => e.id);
                foreach (Care care in linked)
                {
                    if (records.TryGetValue(care.member_equipment_id!.Value, out MemberEquipment? record))
                    {
                        List<CareEquipmentDiff> diffs = CareEquipmentDiffRules.Compare(care, record);
                        care.equipmentDiffs = diffs.Count > 0 ? diffs : null;
                    }
                }
            }
            catch (Exception ex)
            {
                // 标注只是提示，读不到装备列表不能让订单打不开
                Console.WriteLine("养护订单装备比对失败 order=" + order.id + "：" + ex.Message);
            }
        }
    }
}
