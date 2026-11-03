using System.Text.Json;
using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// api/ShareList：店长以上；筛选参数传到服务；记账后列表看得到。
public class ShareListControllerTests
{
    private static JsonElement D(ApiResult<object> r) => JsonSerializer.SerializeToElement(r.data);

    private static async Task<(SnowmeetApi.Data.ApplicationDBContext db, Func<ShareListController> ctl)> SetupAsync()
    {
        var db = await ShareListServiceTests.SeedAsync();
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        await ShareFixture.AddStaffSessionAsync(db, 303, 100, "clerk-s");
        return (db, () => new ShareListController(db, ShareFixture.Config, ShareFixture.Http));
    }

    private static int[] Ids(JsonElement items) => items.EnumerateArray().Select(i => i.GetProperty("orderShareId").GetInt32()).OrderBy(x => x).ToArray();

    [Fact]
    public async Task Permissions_for_all_three_actions()
    {
        var (db, ctl) = await SetupAsync();
        await using var _ = db;
        var body = new ShareListController.CompleteManualInput(new List<int> { 11 }, "MS-PERM");

        Assert.Equal(2, (await ctl().List("bad")).code);
        Assert.Equal(2, (await ctl().NextBatchNo("bad")).code);
        Assert.Equal(2, (await ctl().CompleteManual("bad", body)).code);
        Assert.Equal(3, (await ctl().List("clerk-s")).code);
        Assert.Equal(3, (await ctl().NextBatchNo("clerk-s")).code);
        Assert.Equal(3, (await ctl().CompleteManual("clerk-s", body)).code);
        Assert.Equal(0, (await ctl().List("manager-s")).code);
        Assert.Equal(0, (await ctl().NextBatchNo("manager-s")).code);
    }

    [Fact]
    public async Task List_passes_the_filters_to_the_service_and_validates_dates()
    {
        var (db, ctl) = await SetupAsync();
        await using var _ = db;

        var done = D(await ctl().List("manager-s", status: "已分"));
        Assert.Equal(new[] { 12, 15 }, Ids(done.GetProperty("items")));
        Assert.Equal(2, done.GetProperty("total").GetInt32());

        var snow = D(await ctl().List("manager-s", status: "未分", type: "雪票"));
        Assert.Equal(new[] { 13, 14 }, Ids(snow.GetProperty("items")));
        Assert.Equal(2, snow.GetProperty("selectableCount").GetInt32());

        var byKeyword = D(await ctl().List("manager-s", keyword: "李教练"));
        Assert.Equal(new[] { 11 }, Ids(byKeyword.GetProperty("items")));

        var byDate = D(await ctl().List("manager-s", startDate: "2026-11-03", endDate: "2026-11-05"));
        Assert.Equal(new[] { 11, 15 }, Ids(byDate.GetProperty("items")));

        var bad = await ctl().List("manager-s", startDate: "2026/11/03");
        Assert.Equal(1, bad.code);
        Assert.Equal("日期格式不对", bad.message);

        var huge = D(await ctl().List("manager-s", pageSize: 100000));          // 页大小被夹住，不会一次吐全库
        Assert.Equal(6, huge.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Completing_through_the_api_shows_up_in_the_list_and_a_reused_batch_number_is_refused()
    {
        var (db, ctl) = await SetupAsync();
        await using var _ = db;

        var next = D(await ctl().NextBatchNo("manager-s")).GetProperty("batchNo").GetString()!;
        Assert.StartsWith("MS-", next);

        var done = await ctl().CompleteManual("manager-s", new ShareListController.CompleteManualInput(new List<int> { 11, 13, 14 }, next));
        Assert.Equal(0, done.code);
        var result = D(done);
        Assert.Equal((next, 3, 42d), (result.GetProperty("batchNo").GetString(), result.GetProperty("count").GetInt32(), result.GetProperty("total").GetDouble()));

        var listed = D(await ctl().List("manager-s", status: "已分"));
        Assert.Equal(new[] { 11, 12, 13, 14, 15 }, Ids(listed.GetProperty("items")));

        var again = await ctl().CompleteManual("manager-s", new ShareListController.CompleteManualInput(new List<int> { 10 }, next));
        Assert.Equal(1, again.code);
        Assert.Equal("批次号已存在", again.message);
    }
}
