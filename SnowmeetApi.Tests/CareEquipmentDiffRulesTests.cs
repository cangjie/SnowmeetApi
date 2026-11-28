using System.Linq;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    /// <summary>养护订单装备和顾客装备列表里对应那件的比较口径（用户 2026-11-28）。</summary>
    public class CareEquipmentDiffRulesTests
    {
        private static Care C(string equipment = "双板", string? brand = "APO", string? scale = "178", string? serials = null)
            => new() { equipment = equipment, brand = brand, scale = scale, serials = serials };

        private static MemberEquipment E(string equipment = "双板", string? brand = "APO", string? scale = "178", string? serials = null)
            => new() { equipment = equipment, brand = brand, scale = scale, serials = serials };

        private static string[] Diffs(Care c, MemberEquipment e) => CareEquipmentDiffRules.Compare(c, e)
            .Select(d => $"{d.field}:{d.orderValue}>{d.listValue ?? "∅"}").ToArray();

        [Fact]
        public void 首尾空格和大小写不同不算差异()
            => Assert.Empty(Diffs(C(brand: " apo ", scale: "178 ", serials: " ab1 | cd2 "), E(serials: "AB1|CD2")));

        [Fact]
        public void 长度不同标一项()
            => Assert.Equal(new[] { "scale:178>180" }, Diffs(C(), E(scale: "180")));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("|")]
        [InlineData("无")]
        [InlineData("无|无")]
        [InlineData("无|")]
        public void 订单里没填的序列号不比较(string? serials)
            => Assert.Empty(Diffs(C(serials: serials), E(serials: "279")));

        [Fact]
        public void 订单里没填的品牌和长度不比较()
            => Assert.Empty(Diffs(C(brand: "无", scale: null), E(brand: "Burton", scale: "156")));

        [Theory]
        [InlineData(null)]
        [InlineData("无")]
        [InlineData("|")]
        public void 订单填了而列表空着算不一致(string? listSerials)
            => Assert.Equal(new[] { "serials:20556>∅" }, Diffs(C(serials: "20556"), E(serials: listSerials)));

        [Fact]
        public void 左右序列号按整理后的值比较()
        {
            Assert.Empty(Diffs(C(serials: "无|B2"), E(serials: "|B2")));
            Assert.Equal(new[] { "serials:A1|B2>A1|B3" }, Diffs(C(serials: "A1 | B2"), E(serials: "A1|B3")));
            Assert.Equal(new[] { "serials:123|>123" }, Diffs(C(serials: "123|"), E(serials: "123")));
        }

        [Fact]
        public void 四项都不同时按固定顺序给出()
            => Assert.Equal(new[] { "equipment:单板>双板", "brand:Burton>APO", "scale:156>178", "serials:S1>S2" },
                Diffs(C("单板", "Burton", "156", "S1"), E(serials: "S2")));
    }
}
