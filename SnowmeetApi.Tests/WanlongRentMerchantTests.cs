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
    [InlineData("万龙服务中心", 5)]
    [InlineData("万龙体验中心", 5)]
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
}
