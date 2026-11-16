using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

public class MemberAdminProfileDisplayTests
{
    private static MemberAdminController Controller(CoachFixture f) => new(f.NewContext(), f.Config, new HttpContextAccessor());
    private static ApiResult<object> Result(ActionResult<ApiResult<object>> result) =>
        Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(result.Result).Value);
    private static JsonElement Data(ApiResult<object> result) => JsonSerializer.SerializeToElement(result.data);

    [Fact]
    public async Task 列表和详情显示昵称头像_空资料保留原姓名_图片地址按配置生成()
    {
        await using var f = await CoachFixture.Create();
        f.Config["FileStorage:PublicBaseUrl"] = "https://images.example.test";
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE member SET nick_name = 'Snow Friend', avatar = '/upload/avatar.jpg' WHERE id = 3");
        var controller = Controller(f);
        var list = Data(Result(await controller.SearchMembersByStaff("session-7", pageSize: 20)));
        var first = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == CoachFixture.StudentA);
        Assert.Equal("学员A", first.GetProperty("name").GetString());
        Assert.Equal("Snow Friend", first.GetProperty("nickName").GetString());
        Assert.Equal("https://images.example.test/upload/avatar.jpg", first.GetProperty("avatarUrl").GetString());

        var missing = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == CoachFixture.StudentB);
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("nickName").ValueKind);
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("avatarUrl").ValueKind);

        var detail = Data(Result(await controller.GetMemberDetailByStaff(CoachFixture.StudentA, "session-7")));
        Assert.Equal("Snow Friend", detail.GetProperty("nickName").GetString());
        Assert.Equal("https://images.example.test/upload/avatar.jpg", detail.GetProperty("avatarUrl").GetString());
        var emptyDetail = Data(Result(await controller.GetMemberDetailByStaff(CoachFixture.StudentB, "session-7")));
        Assert.Equal(JsonValueKind.Null, emptyDetail.GetProperty("avatarUrl").ValueKind);
    }

    [Fact]
    public async Task 已是完整头像地址不重复拼接_权限校验仍生效()
    {
        await using var f = await CoachFixture.Create();
        await f.Db.Database.ExecuteSqlRawAsync("UPDATE member SET avatar = 'https://other.example.test/a.png' WHERE id = 3");
        var controller = Controller(f);
        var detail = Data(Result(await controller.GetMemberDetailByStaff(CoachFixture.StudentA, "session-7")));
        Assert.Equal("https://other.example.test/a.png", detail.GetProperty("avatarUrl").GetString());
        Assert.Equal(1, Result(await controller.GetMemberDetailByStaff(CoachFixture.StudentA, "session-3")).code);
        Assert.Equal(1, Result(await controller.SearchMembersByStaff("session-3")).code);
    }
}
