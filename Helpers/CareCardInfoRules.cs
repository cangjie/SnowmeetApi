using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 养护订单列表里，用次卡的装备显示「用的是哪张次卡、还剩几次」（只用于展示，不改库；
    // 填到 Care.cardDisplayName / cardRemaining / cardUnlimited，由 OrderController.FillCareCardInfoAsync 调用）：
    //   · 卡名：优先取次卡本身的名字，取不到（卡找不到、名字是空的）退回装备上记的 card_name；
    //   · 剩余次数 = 总次数 - 已用次数，是「现在这一刻」的余量，不显示负数；
    //   · 季卡（总次数为空）不限次数：unlimited = true，没有剩余次数；
    //   · 装备没用次卡不显示；还没绑上卡（购卡处理中 card_id 为空）或卡找不到时只有卡名、没有剩余次数。
    public static class CareCardInfoRules
    {
        public static (string? name, int? remaining, bool unlimited) Build(Care care, PunchCard? card)
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
            return (name, Math.Max(0, card.total.Value - (card.punches ?? 0)), false);
        }
    }
}
