using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 养护订单列表里，用次卡的装备显示「用的是哪张次卡、还剩几次」（只用于展示，不改库；
    // 填到 Care.cardDisplayName / cardRemaining / cardUnlimited，由 OrderController.FillCareCardInfoAsync 调用）：
    //   · 卡名：优先取次卡本身的名字，取不到（卡找不到、名字是空的）退回装备上记的 card_name；
    //   · 剩余次数 = 这件装备「用完之后」卡里还剩几次：同一张卡上的两件装备先后各用一次，先用的那件显示得比后用的多 1。
    //     算法：卡现在的剩余（总次数 - 已用次数，和别处同口径）+ 这件装备之后又用掉的次数（核销明细 punch_card_used 里 id 比它大的有效记录）；
    //     所以这张卡上最后一次使用的那件装备，显示的就是卡现在的余量。
    //     这件装备自己没有有效的核销记录（订单还没生效、已取消返次）时，取卡现在的余量。不显示负数，也不超过总次数；
    //   · 季卡（总次数为空）不限次数：unlimited = true，没有剩余次数；
    //   · 装备没用次卡不显示；还没绑上卡（购卡处理中 card_id 为空）或卡找不到时只有卡名、没有剩余次数。
    public static class CareCardInfoRules
    {
        // 这张卡上，这件装备自己的核销之后又用掉了几次；装备自己没有有效核销记录时返回 null。
        // cardUses = 这张卡的有效核销明细（punch_card_used）。
        public static int? UsedAfter(IEnumerable<PunchCardUsed> cardUses, int careId)
        {
            List<PunchCardUsed> uses = cardUses.Where(u => u.valid).ToList();
            PunchCardUsed? own = uses.Where(u => u.biz_type == "养护" && u.biz_id == careId).OrderBy(u => u.id).FirstOrDefault();
            if (own == null)
            {
                return null;
            }
            return uses.Where(u => u.id > own.id).Sum(u => u.punch_count);
        }

        public static (string? name, int? remaining, bool unlimited) Build(Care care, PunchCard? card, int? usedAfterThis = null)
        {
            if (!care.use_card)
            {
                return (null, null, false);
            }
            string? name = !string.IsNullOrWhiteSpace(card?.card_name) ? card!.card_name : care.card_name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = null;
            }
            if (card == null)
            {
                return (name, null, false);
            }
            if (card.total == null)
            {
                return (name, null, true);
            }
            int remaining = card.total.Value - (card.punches ?? 0) + (usedAfterThis ?? 0);
            return (name, Math.Min(card.total.Value, Math.Max(0, remaining)), false);
        }
    }
}
