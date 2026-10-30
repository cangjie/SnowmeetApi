using Microsoft.Extensions.Configuration;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class CoachCardRulesTests
{
    private static IConfiguration Cfg(string? seasonEnd) => new ConfigurationBuilder()
        .AddInMemoryCollection(seasonEnd == null ? new Dictionary<string, string?>() : new() { ["Coach:SeasonEndDate"] = seasonEnd }).Build();

    [Theory]
    [InlineData("2026-10-30", 2027, 4, 30)]
    [InlineData("2027-04-30", 2027, 4, 30)]
    [InlineData("2027-05-01", 2028, 4, 30)]
    public void SeasonEnd_DefaultsToTicketSeasonEnd(string now, int y, int m, int d)
    {
        var end = CoachCardRules.SeasonEnd(DateTime.Parse(now), Cfg(null));
        Assert.Equal(new DateTime(y, m, d, 23, 59, 59), end);
    }

    [Fact]
    public void SeasonEnd_ConfigOverrideOnlyWhenInTheFuture()
    {
        var now = new DateTime(2026, 10, 30, 10, 0, 0);
        Assert.Equal(new DateTime(2027, 6, 1, 23, 59, 59), CoachCardRules.SeasonEnd(now, Cfg("2027-06-01")));
        // 配成过去的日期或乱写，回落到 4 月 30 日，不能发出一张一领取就过期的卡
        Assert.Equal(new DateTime(2027, 4, 30, 23, 59, 59), CoachCardRules.SeasonEnd(now, Cfg("2026-01-01")));
        Assert.Equal(new DateTime(2027, 4, 30, 23, 59, 59), CoachCardRules.SeasonEnd(now, Cfg("not-a-date")));
    }

    [Theory]
    [InlineData(1, 1, "双项")]
    [InlineData(1, 0, "单项")]
    [InlineData(0, 1, "单项")]
    [InlineData(0, 0, null)]
    public void Tier_CountsEdgeAndHotWaxOnly(int edge, int wax, string? expected)
    {
        var care = new Care { equipment = "双板", need_edge = edge, need_wax = wax };
        Assert.Equal(expected, CoachCardRules.ResolveTier(care));
    }

    [Fact]
    public void Tier_MachineWaxAndUnwaxAreNotChargeable()
    {
        var care = new Care { equipment = "双板", free_wax = 1, need_unwax = 1 };
        Assert.Null(CoachCardRules.ResolveTier(care));
    }

    [Theory]
    [InlineData("万龙服务中心", "万龙服务中心")]
    [InlineData("万龙体验中心", "万龙服务中心")] // 万龙养护统一由服务中心接单
    [InlineData("崇礼旗舰店", "崇礼旗舰店")]
    [InlineData("南山", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CareShop_OnlyTheTwoConfiguredShops(string? shop, string? expected)
    {
        Assert.Equal(expected, CoachCardRules.ResolveCareShop(shop));
    }

    [Theory]
    [InlineData("rate", 0.8, 168, 33.6)]
    [InlineData("rate", 0.85, 168, 25.2)]
    [InlineData("rate", 1, 168, 0)]
    [InlineData("fixed", 128, 188, 60)]
    [InlineData("fixed", 128, 100, 0)]   // 一口价高于原价不涨价
    [InlineData("fixed", 0, 100, 100)]   // 一口价 0 = 免费，优惠不超过原价
    [InlineData("none", 0.5, 168, 0)]
    [InlineData("rate", 0.8, 0, 0)]
    public void CareDiscount_RateAndFixed(string mode, double value, double basePrice, double expected)
    {
        Assert.Equal(expected, CoachCardRules.CareDiscount(mode, value, basePrice));
    }

    [Theory]
    [InlineData(0.9, 540, 54)]
    [InlineData(0.85, 180, 27)]
    [InlineData(1.0, 540, 0)]
    [InlineData(null, 540, 0)]
    [InlineData(0.9, 0, 0)]
    public void RentalDiscount_IsOneMinusRate(double? rate, double amount, double expected)
    {
        Assert.Equal(expected, CoachCardRules.RentalDiscount(rate, amount));
    }

    [Theory]
    [InlineData("rate", 0.8, true)]
    [InlineData("rate", 1, true)]
    [InlineData("rate", 0, false)]
    [InlineData("rate", 1.2, false)]
    [InlineData("fixed", 0, true)]
    [InlineData("fixed", -1, false)]
    [InlineData("none", 0, true)]
    [InlineData("bogus", 0.5, false)]
    public void ValidateCarePrice(string mode, double value, bool ok)
    {
        Assert.Equal(ok, CoachCardRules.TryValidateCarePrice(mode, value, out string error));
        Assert.Equal(ok, error == "");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0.9, true)]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    [InlineData(1.5, false)]
    public void ValidateRentalRate(double? rate, bool ok)
    {
        Assert.Equal(ok, CoachCardRules.TryValidateRentalRate(rate, out _));
    }

    [Fact]
    public void Snapshot_RoundTripsAndSkipsNoneCells()
    {
        var prices = new List<CoachCarePrice>
        {
            new() { shop = "万龙服务中心", tier = "单项", mode = "rate", value = 0.8 },
            new() { shop = "万龙服务中心", tier = "双项", mode = "none", value = 0 },
            new() { shop = "崇礼旗舰店", tier = "双项", mode = "fixed", value = 128 },
        };
        var cells = CoachCardRules.ParseCarePrices(CoachCardRules.SerializeCarePrices(prices));
        Assert.Equal(2, cells.Count);
        Assert.Equal("rate", CoachCardRules.FindCell(cells, "万龙服务中心", "单项")!.mode);
        Assert.Equal(128, CoachCardRules.FindCell(cells, "崇礼旗舰店", "双项")!.value);
        Assert.Null(CoachCardRules.FindCell(cells, "万龙服务中心", "双项")); // none 不算一格
        Assert.Null(CoachCardRules.FindCell(cells, "崇礼旗舰店", "单项"));
        // 万龙体验中心的养护归服务中心
        Assert.NotNull(CoachCardRules.FindCell(cells, "万龙体验中心", "单项"));
        Assert.Null(CoachCardRules.FindCell(cells, "南山", "单项"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    public void ParseSnapshot_ToleratesMissingOrBrokenJson(string? json)
    {
        Assert.Empty(CoachCardRules.ParseCarePrices(json));
    }

    [Fact]
    public void Effective_RequiresClaimedOwnerAndWindow()
    {
        var now = new DateTime(2026, 12, 1);
        var card = new CoachCard
        {
            status = CoachCard.StatusClaimed, member_id = 7,
            start_date = new DateTime(2026, 10, 30), end_date = new DateTime(2027, 4, 30, 23, 59, 59),
        };
        Assert.True(CoachCardRules.IsEffective(card, 7, now));
        Assert.False(CoachCardRules.IsEffective(card, 8, now));                       // 别人的卡
        Assert.False(CoachCardRules.IsEffective(card, 7, new DateTime(2027, 5, 1)));  // 过期
        Assert.False(CoachCardRules.IsEffective(card, 7, new DateTime(2026, 10, 1))); // 未到起始
        card.status = CoachCard.StatusRevoked;
        Assert.False(CoachCardRules.IsEffective(card, 7, now));
        card.status = CoachCard.StatusIssued;
        Assert.False(CoachCardRules.IsEffective(card, 7, now));
        Assert.False(CoachCardRules.IsEffective(null, 7, now));
    }

    [Fact]
    public void Claimable_OnlyIssuedAndNotPastDeadline()
    {
        var now = new DateTime(2026, 10, 30, 12, 0, 0);
        Assert.True(CoachCardRules.IsClaimable(new CoachCard { status = CoachCard.StatusIssued }, now));
        Assert.True(CoachCardRules.IsClaimable(new CoachCard { status = CoachCard.StatusIssued, claim_deadline = now.AddMinutes(1) }, now));
        Assert.False(CoachCardRules.IsClaimable(new CoachCard { status = CoachCard.StatusIssued, claim_deadline = now.AddMinutes(-1) }, now));
        Assert.False(CoachCardRules.IsClaimable(new CoachCard { status = CoachCard.StatusClaimed }, now));
        Assert.False(CoachCardRules.IsClaimable(new CoachCard { status = CoachCard.StatusRevoked }, now));
    }

    [Fact]
    public void Token_Is24LowerHexAndUnique()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => CoachCardRules.NewToken()).ToList();
        Assert.All(tokens, t => Assert.Matches("^[0-9a-f]{24}$", t));
        Assert.Equal(200, tokens.Distinct().Count());
    }

    [Theory]
    [InlineData("王同学", "昵称", "王同学")]
    [InlineData(null, "小王", "小王")]
    [InlineData("  ", "小王", "小王")]
    [InlineData(null, null, "学员")]
    public void StudentName_FallsBack(string? real, string? nick, string expected)
    {
        Assert.Equal(expected, CoachCardRules.StudentName(real, nick));
    }

    [Theory]
    [InlineData("13800006612", "6612")]
    [InlineData("138 0000 0231", "0231")]
    [InlineData("123", "")]
    [InlineData(null, "")]
    public void PhoneTail_LastFourDigits(string? cell, string expected)
    {
        Assert.Equal(expected, CoachCardRules.PhoneTail(cell));
    }
}
