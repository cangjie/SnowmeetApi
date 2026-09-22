using SnowmeetApi.Models;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public class FnbAccessTests
{
    [Fact]
    public void ShopAndManagementPermissionsUseResolvedStaffOnly()
    {
        var worker = new Staff { id = 3, valid = 1, base_shop_id = 10, title_level = 100 };
        Assert.True(FnbAccess.CanAccess(worker, 10, false));
        Assert.False(FnbAccess.CanAccess(worker, 11, false));
        Assert.False(FnbAccess.CanAccess(worker, 10, true));
        worker.title_level = 200;
        Assert.True(FnbAccess.CanAccess(worker, 10, true));
        worker.valid = 0;
        Assert.False(FnbAccess.CanAccess(worker, 10, false));
    }
}
