namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 租金的价目场景（门市价 / 预约价）。2026-11-15 用户要求：租赁开单可以选按门市价还是预约价，
    /// 价格预设（rental_price_preset.scene）和每天生成的日租金记录（rental_detail.scene）都要记下来。
    /// 价目表（rent_price.scene）里还有「会员」价，历史预设里也有 会员 和脏值（空串、单引号），这里只认 门市 / 预约 / 会员，其它都当门市。
    /// </summary>
    public static class RentSceneRules
    {
        public const string Shop = "门市";
        public const string Reserve = "预约";
        public const string Member = "会员";

        /// <summary>规范成 门市 / 预约 / 会员；空、脏值一律当门市。</summary>
        public static string Normalize(string? scene)
        {
            string s = (scene ?? "").Trim();
            if (s == Reserve) return Reserve;
            if (s == Member) return Member;
            return Shop;
        }

        public static bool IsReserve(string? scene) => (scene ?? "").Trim() == Reserve;
    }
}
