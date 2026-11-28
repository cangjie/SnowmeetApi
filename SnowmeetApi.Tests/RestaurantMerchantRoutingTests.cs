using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class RestaurantMerchantRoutingTests
{
    [Theory]
    [InlineData("多呆一会儿吧", "餐饮", 15)]
    [InlineData(" 多呆一会儿吧 ", " 餐饮 ", 15)]
    [InlineData("多呆一会儿吧", "零售", 9)]
    [InlineData("崇礼旗舰店", "餐饮", 9)]
    [InlineData("南山", "养护", 15)]
    public void RestaurantUsesTheSpecifiedMerchantOnlyForTheRestaurantShop(string shop, string type, int expected)
    {
        using var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Settings:AppId"] = "local-test"
        }).Build();
        var tenpay = new TenpayController(db, config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        Assert.Equal(expected, tenpay.GetMchId(new Order { shop = shop, type = type }));
    }
}
