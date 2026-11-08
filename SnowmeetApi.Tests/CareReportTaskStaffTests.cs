using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Tests;

public class CareReportTaskStaffTests
{
    [Fact]
    public async Task OrderQueryLoadsValidCareTaskStaffOnlyWhenRequested()
    {
        await using var fixture = await CoachFixture.Create(withPricing: false);
        await using var db = fixture.NewContext();
        var executor = new Staff { name = "执行人", valid = 1 };
        var order = new Order { type = "养护", shop = "测试门店", valid = 1, biz_date = DateTime.Now };
        db.staff.Add(executor);
        db.order.Add(order);
        await db.SaveChangesAsync();
        var care = new Care { order_id = order.id, equipment = "雪板", valid = 1 };
        var invalidCare = new Care { order_id = order.id, equipment = "旧雪板", valid = 0 };
        db.care.AddRange(care, invalidCare);
        await db.SaveChangesAsync();
        db.careTask.AddRange(
            new CareTask { care_id = care.id, task_name = "修刃", staff_id = executor.id, valid = 1 },
            new CareTask { care_id = care.id, task_name = "发板", valid = 1 },
            new CareTask { care_id = care.id, task_name = "无效步骤", staff_id = executor.id, valid = 0 },
            new CareTask { care_id = invalidCare.id, task_name = "无效装备", staff_id = executor.id, valid = 1 });
        await db.SaveChangesAsync();

        await using var readDb = fixture.NewContext();
        var controller = new OrderController(readDb, fixture.Config, new HttpContextAccessor());
        var withStaff = Assert.Single(await controller.GetCommonOrders(order.id, null, null, null, "养护", null, null,
            includeCareTaskStaff: true));
        var tasks = Assert.Single(withStaff.cares).tasks;
        Assert.Equal(2, tasks.Count);
        Assert.Equal("执行人", tasks.Single(t => t.task_name == "修刃").staff!.name);
        Assert.Null(tasks.Single(t => t.task_name == "发板").staff);

        var withoutStaff = Assert.Single(await controller.GetCommonOrders(order.id, null, null, null, "养护", null, null));
        Assert.Null(Assert.Single(withoutStaff.cares).tasks.Single(t => t.task_name == "修刃").staff);

        var response = await controller.GetOrdersByStaff(order.id, null, "养护", null, DateTime.Today,
            DateTime.Today, "session-1", null, includeCareTaskStaff: true);
        var result = Assert.IsType<ApiResult<List<Order>>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, result.code);
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        });
        var returnedTasks = json.GetProperty("data")[0].GetProperty("cares")[0].GetProperty("tasks");
        var edge = returnedTasks.EnumerateArray().Single(t => t.GetProperty("task_name").GetString() == "修刃");
        Assert.Equal("执行人", edge.GetProperty("staff").GetProperty("name").GetString());
    }
}
