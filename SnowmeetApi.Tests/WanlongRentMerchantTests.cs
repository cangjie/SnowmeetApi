using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class WanlongRentMerchantTests
{
    [Theory]
    [InlineData("万龙服务中心", 1018)]   // 2026-11-22 起万龙租赁走新商户 1006012310（wepay_key 1018）
    [InlineData("万龙体验中心", 1018)]
    [InlineData("崇礼旗舰店", 10)]
    public void RentalPaymentsUseTheMerchantOfTheOrderShop(string shop, int expected)
    {
        using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Settings:AppId"] = "local-test"
        }).Build();
        var tenpay = new TenpayController(db, config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        Assert.Equal(expected, tenpay.GetMchId(new Order { shop = shop, type = "租赁" }));
    }

    // 2026-11-22 万龙租赁换新微信商户 1006012310（wepay_key 1018）：体验中心、服务中心的租赁订单一律走新账号，测试单和真实单一样
    [Theory]
    [InlineData("万龙服务中心", "租赁", 1, 1018)]
    [InlineData("万龙体验中心", "租赁", 1, 1018)]
    [InlineData("万龙服务中心", "租赁", 0, 1018)]
    [InlineData("万龙体验中心", "租赁", 0, 1018)]
    [InlineData("万龙体验中心", "养护", 0, 3)]     // 只有万龙租赁换账号，养护还是服务中心的养护商户
    [InlineData("万龙体验中心", "养护", 1, 3)]
    [InlineData("万龙体验中心", "零售", 0, 12)]
    [InlineData("崇礼旗舰店", "租赁", 0, 10)]      // 旗舰租赁不换
    [InlineData("崇礼旗舰店", "租赁", 1, 10)]
    public void AllWanlongRentalsUseTheNewMerchant(string shop, string type, int isTest, int expected)
    {
        using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Settings:AppId"] = "local-test"
        }).Build();
        var tenpay = new TenpayController(db, config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        Assert.Equal(expected, tenpay.GetMchId(new Order { shop = shop, type = type, is_test = isTest }));
    }
}
