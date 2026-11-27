using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Tests;

public class InternalCareIntegrationTests
{
    [Fact]
    public async Task TwoBoardsCreateIndependentTasksWithoutCustomerOrPickupAndRetryIsIdempotent()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await using (var db = f.NewContext())
        {
            db.UploadFile.AddRange(new UploadFile { id = 101, staff_id = 9, purpose = "内部养护开单", file_path_name = "/a.jpg" },
                new UploadFile { id = 102, staff_id = 9, purpose = "内部养护开单", file_path_name = "/b.jpg" });
            await db.SaveChangesAsync();
        }
        var input = new InternalCareController.CreateRequest { requestId = Guid.NewGuid().ToString(), shop = "崇礼旗舰店", equipment = new()
        {
            new() { equipment = "单板", serials = "A", brand = "Burton", scale = "150", edge = true, edgeDegree = "89", imageIds = new() { 101 } },
            new() { equipment = "双板", serials = "B", brand = "Head", scale = "160", wax = true, unwax = true, imageIds = new() { 102 } }
        } };
        async Task<ApiResult<object>> Create()
        {
            await using var db = f.NewContext();
            var response = await new InternalCareController(db).Create(input, "session-1");
            return Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        }
        Assert.Equal(0, (await Create()).code);
        Assert.Equal(0, (await Create()).code);
        await using var read = f.NewContext();
        var order = await read.order.SingleAsync(o => o.type == InternalCareRules.OrderType);
        Assert.Null(order.member_id);
        Assert.Equal(0, order.total_amount);
        var cares = await read.care.Where(c => c.order_id == order.id).Include(c => c.tasks).Include(c => c.careImages).ToListAsync();
        Assert.Equal(2, cares.Count);
        Assert.Equal(2, cares.Select(c => c.task_flow_code).Distinct().Count());
        Assert.All(cares, c => { Assert.Single(c.careImages); Assert.DoesNotContain(c.tasks, t => t.task_name == "发板"); });
        Assert.Contains(cares.Single(c => c.serials == "A").tasks, t => t.task_name == "修刃");
        Assert.Contains(cares.Single(c => c.serials == "B").tasks, t => t.task_name == "刮蜡");
        var detail = await new OrderController(read, f.Config, new HttpContextAccessor()).GetOrder(order.id);
        Assert.Equal(2, detail.cares.Count);
        var detailJson = JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles });
        Assert.Equal("内部养护", detailJson.GetProperty("type").GetString());
        Assert.Equal(2, detailJson.GetProperty("cares").GetArrayLength());
        Assert.Empty(await new CareWorkloadService(read).ComputeByShopAsync(DateTime.Today, DateTime.Now));

        var one = cares.Single(c => c.serials == "A");
        foreach (var task in one.tasks.OrderBy(t => t.sort))
        {
            await using var updateDb = f.NewContext();
            var controller = new CareController(updateDb, f.Config, new HttpContextAccessor());
            if (task.task_name == "修刃")
            {
                await controller.SetTaskStatus(task.id, "强行中止", "测试", "session-1");
                await controller.SetTaskStatus(task.id, "已开始", "测试", "session-1");
                var restarted = await updateDb.careTask.AsNoTracking().SingleAsync(t => t.id == task.id);
                Assert.Null(restarted.end_time);
                Assert.Null(restarted.terminate_staff_id);
            }
            var response = await controller.SetTaskStatus(task.id, "已完成", "测试", "session-1");
            var result = Assert.IsType<ApiResult<Care>>(Assert.IsType<OkObjectResult>(response.Result).Value);
            Assert.Equal(0, result.code);
        }
        await using var listDb = f.NewContext();
        var listResponse = await new InternalCareController(listDb).List("session-1");
        var listResult = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(listResponse.Result).Value);
        var json = JsonSerializer.SerializeToElement(listResult.data);
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        Assert.Equal(1, json.GetProperty("completed").GetInt32());
        Assert.Equal(1, json.GetProperty("staffStats").GetArrayLength());
        Assert.Equal("修刃", json.GetProperty("staffStats")[0].GetProperty("task").GetString());
        Assert.False(await listDb.ticket.AnyAsync(t => t.biz_type == "养护"));
    }
}
