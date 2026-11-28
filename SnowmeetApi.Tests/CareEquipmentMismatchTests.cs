using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;
using Xunit;

namespace SnowmeetApi.Tests;

// 员工查看养护订单时，订单里的装备信息和顾客装备列表里对应那件不一样要标出来；匹配不到那件时什么都不标（用户 2026-11-28）。
public class CareEquipmentMismatchTests
{
    private sealed class Http : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }

    private const string Clerk = "session-7";      // 店员（职级 100，staff 10）

    private static MemberEquipmentController EquipCtl(CoachFixture f) => new(f.NewContext(), f.Config);
    private static CareController CareCtl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    private static OrderController OrderCtl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());

    private static Care NewCare(string equipment, string brand, string scale, string serials) => new()
    {
        member_equipment_id = MemberEquipmentService.ExplicitNewSentinel,
        equipment = equipment,
        brand = brand,
        scale = scale,
        serials = serials,
        need_wax = 1,
        need_unwax = 1
    };

    private static async Task<SnowmeetApi.Models.Order> SaveAndPlaceAsync(CoachFixture f, SnowmeetApi.Models.Order draft)
    {
        var saved = ((ApiResult<SnowmeetApi.Models.Order?>)((OkObjectResult)(await CareCtl(f).SaveCareRecept(draft, Clerk)).Result!).Value!).data!;
        return ((ApiResult<SnowmeetApi.Models.Order>)((OkObjectResult)(await OrderCtl(f).PlaceCareOrder(saved.id, Clerk)).Result!).Value!).data!;
    }

    /// <summary>StudentA 开一单两件新装备（双板 APO 178 S-1、单板 Burton 156 S-2），下单后各有一条装备档案。</summary>
    private static async Task<(int orderId, Care ski, Care board)> PlaceTwoCaresAsync(CoachFixture f)
    {
        var placed = await SaveAndPlaceAsync(f, new SnowmeetApi.Models.Order
        {
            shop = "崇礼旗舰店",
            member_id = CoachFixture.StudentA,
            type = "养护",
            cares = new List<Care>
            {
                NewCare("双板", "APO", "178", "S-1"),
                NewCare("单板", "Burton", "156", "S-2")
            }
        });
        var cares = await f.NewContext().care.AsNoTracking().Where(c => c.order_id == placed.id).ToListAsync();
        Assert.All(cares, c => Assert.True(c.member_equipment_id > 0));
        return (placed.id, cares.Single(c => c.equipment == "双板"), cares.Single(c => c.equipment == "单板"));
    }

    private static async Task ChangeListScaleAsync(CoachFixture f, int equipmentId, string scale)
    {
        MemberEquipment e = await f.NewContext().memberEquipment.AsNoTracking().SingleAsync(x => x.id == equipmentId);
        var res = (ApiResult<MemberEquipmentView?>)((OkObjectResult)(await EquipCtl(f).UpdateByStaff(
            new MemberEquipmentController.SaveMemberEquipmentRequest
            {
                id = e.id,
                memberId = e.member_id,
                equipment = e.equipment,
                brand = e.brand,
                scale = scale,
                serials = e.serials,
                imageId = e.image_id
            }, Clerk)).Result!).Value!;
        Assert.Equal(0, res.code);
    }

    private static async Task<List<Care>> ReadCaresAsync(CoachFixture f, int orderId)
    {
        var res = (ApiResult<SnowmeetApi.Models.Order>)((OkObjectResult)(await OrderCtl(f).GetOrderByStaff(orderId, Clerk)).Result!).Value!;
        Assert.Equal(0, res.code);
        return res.data!.cares!.OrderBy(c => c.id).ToList();
    }

    private static string[] DiffText(Care care)
        => (care.equipmentDiffs ?? new()).Select(d => $"{d.field}:{d.orderValue}>{d.listValue}").ToArray();

    private static async Task<List<Care>> StoredCaresAsync(CoachFixture f, int orderId)
        => await f.NewContext().care.AsNoTracking().Where(c => c.order_id == orderId).OrderBy(c => c.id).ToListAsync();

    private static async Task EditCareAsync(CoachFixture f, int careId, System.Action<Care> edit)
    {
        var db = f.NewContext();
        edit(await db.care.AsTracking().SingleAsync(c => c.id == careId));
        await db.SaveChangesAsync();
    }

    private static async Task<List<MemberEquipment>> ValidRecordsAsync(CoachFixture f, int memberId)
        => await f.NewContext().memberEquipment.AsNoTracking().Where(e => e.member_id == memberId && e.valid == 1).OrderBy(e => e.id).ToListAsync();

    // ---------- 订单生效不能把旧订单的值写回装备列表 ----------

    [Fact]
    public async Task 订单再次生效_不把订单旧值写回装备列表()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, ski, board) = await PlaceTwoCaresAsync(f);
        await CareCtl(f).EffectCareOrder(orderId);
        Assert.All(await StoredCaresAsync(f, orderId), c => Assert.NotNull(c.task_flow_code));
        await ChangeListScaleAsync(f, ski.member_equipment_id!.Value, "180");
        // 单上有一件没有装备号时，再次生效（例如又收到一次支付成功）原来会整单重新同步
        await EditCareAsync(f, board.id, c => c.member_equipment_id = null);

        await CareCtl(f).EffectCareOrder(orderId);

        var records = await ValidRecordsAsync(f, CoachFixture.StudentA);
        Assert.Equal(2, records.Count);
        Assert.Equal("180", records.Single(e => e.id == ski.member_equipment_id).scale);
        Assert.Null((await StoredCaresAsync(f, orderId)).Single(c => c.id == board.id).member_equipment_id);
    }

    [Fact]
    public async Task 第一次生效_只同步还没有装备号的装备()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, ski, board) = await PlaceTwoCaresAsync(f);
        Assert.All(await StoredCaresAsync(f, orderId), c => Assert.Null(c.task_flow_code));
        await ChangeListScaleAsync(f, ski.member_equipment_id!.Value, "180");
        await EditCareAsync(f, board.id, c => { c.member_equipment_id = null; c.brand = "NewBrand"; });

        await CareCtl(f).EffectCareOrder(orderId);

        var records = await ValidRecordsAsync(f, CoachFixture.StudentA);
        Assert.Equal(3, records.Count);
        Assert.Equal("180", records.Single(e => e.id == ski.member_equipment_id).scale);
        int? boardLink = (await StoredCaresAsync(f, orderId)).Single(c => c.id == board.id).member_equipment_id;
        Assert.NotEqual(board.member_equipment_id, boardLink);
        Assert.Equal("NewBrand", records.Single(e => e.id == boardLink).brand);
    }

    [Fact]
    public async Task 第一次生效_散客单绑上会员后装备照常进列表()
    {
        await using var f = await CoachFixture.Create();
        var placed = await SaveAndPlaceAsync(f, new SnowmeetApi.Models.Order
        {
            shop = "崇礼旗舰店",
            member_id = null,
            type = "养护",
            cares = new List<Care> { NewCare("双板", "APO", "178", "S-1") }
        });
        Assert.All(await StoredCaresAsync(f, placed.id), c => Assert.Null(c.task_flow_code));
        Assert.Empty(await ValidRecordsAsync(f, CoachFixture.StudentC));
        var db = f.NewContext();
        (await db.order.AsTracking().SingleAsync(o => o.id == placed.id)).member_id = CoachFixture.StudentC;
        await db.SaveChangesAsync();

        await CareCtl(f).EffectCareOrder(placed.id);

        var record = Assert.Single(await ValidRecordsAsync(f, CoachFixture.StudentC));
        Assert.Equal(record.id, Assert.Single(await StoredCaresAsync(f, placed.id)).member_equipment_id);
    }

    // ---------- 订单详情带回差异 ----------

    [Fact]
    public async Task 订单详情_只给不一样的那件带差异()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, ski, board) = await PlaceTwoCaresAsync(f);
        await ChangeListScaleAsync(f, ski.member_equipment_id!.Value, "180");

        var cares = await ReadCaresAsync(f, orderId);

        Assert.Equal(new[] { "scale:178>180" }, DiffText(cares.Single(c => c.id == ski.id)));
        Assert.Null(cares.Single(c => c.id == board.id).equipmentDiffs);
    }

    [Theory]
    [InlineData("装备记录已删除")]
    [InlineData("装备记录在别的会员名下")]
    [InlineData("养护装备已取消")]
    [InlineData("订单没有会员")]
    [InlineData("没有装备号")]
    [InlineData("装备号是草稿哨兵")]
    [InlineData("装备号指向不存在的记录")]
    public async Task 订单详情_匹配不到或已取消的装备不标(string scenario)
    {
        await using var f = await CoachFixture.Create();
        var (orderId, ski, _) = await PlaceTwoCaresAsync(f);
        int equipmentId = ski.member_equipment_id!.Value;
        await ChangeListScaleAsync(f, equipmentId, "180");
        // 对照：动手之前这件是会被标的，免得下面的「没有标注」是空过
        Assert.NotNull((await ReadCaresAsync(f, orderId)).Single(c => c.id == ski.id).equipmentDiffs);

        var db = f.NewContext();
        var care = await db.care.AsTracking().SingleAsync(c => c.id == ski.id);
        var record = await db.memberEquipment.AsTracking().SingleAsync(e => e.id == equipmentId);
        var order = await db.order.AsTracking().SingleAsync(o => o.id == orderId);
        switch (scenario)
        {
            case "装备记录已删除": record.valid = 0; break;
            case "装备记录在别的会员名下": record.member_id = CoachFixture.StudentB; break;
            case "养护装备已取消": care.is_cancel = true; break;
            case "订单没有会员": order.member_id = null; break;
            // 下面三种：列表里仍有同类型同品牌、长度已改的那件，查看时不能另找它来比
            case "没有装备号": care.member_equipment_id = null; break;
            case "装备号是草稿哨兵": care.member_equipment_id = -1; break;
            case "装备号指向不存在的记录": care.member_equipment_id = 999999; break;
        }
        await db.SaveChangesAsync();

        Assert.Null((await ReadCaresAsync(f, orderId)).Single(c => c.id == ski.id).equipmentDiffs);
    }

    [Fact]
    public async Task 订单详情_两件指向同一条档案时各比各的()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, ski, board) = await PlaceTwoCaresAsync(f);
        await ChangeListScaleAsync(f, ski.member_equipment_id!.Value, "180");
        var db = f.NewContext();
        (await db.care.AsTracking().SingleAsync(c => c.id == board.id)).member_equipment_id = ski.member_equipment_id;
        await db.SaveChangesAsync();

        var cares = await ReadCaresAsync(f, orderId);

        Assert.Equal(new[] { "scale:178>180" }, DiffText(cares.Single(c => c.id == ski.id)));
        Assert.Equal(new[] { "equipment", "brand", "scale", "serials" },
            cares.Single(c => c.id == board.id).equipmentDiffs!.Select(d => d.field).ToArray());
    }

    [Fact]
    public async Task 订单详情_读装备列表失败时照常返回订单()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, _, _) = await PlaceTwoCaresAsync(f);
        await f.NewContext().Database.ExecuteSqlRawAsync("DROP TABLE member_equipment");

        var cares = await ReadCaresAsync(f, orderId);

        Assert.Equal(2, cares.Count);
        Assert.All(cares, c => Assert.Null(c.equipmentDiffs));
    }

    // 和 MVC 绑定 [FromBody] 之后做的验证一样（同 RentalPostBackValidationTests）
    private static ModelStateDictionary Validate(object model)
    {
        var provider = new ServiceCollection().AddLogging().AddControllers().Services.BuildServiceProvider();
        var context = new ActionContext(new DefaultHttpContext { RequestServices = provider }, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        provider.GetRequiredService<IObjectModelValidator>().Validate(context, null, "", model);
        return context.ModelState;
    }

    [Fact]
    public async Task 页面把装备整份传回保存_不报错_不多记日志()
    {
        await using var f = await CoachFixture.Create();
        var (orderId, ski, _) = await PlaceTwoCaresAsync(f);
        await ChangeListScaleAsync(f, ski.member_equipment_id!.Value, "180");
        Care loaded = (await ReadCaresAsync(f, orderId)).Single(c => c.id == ski.id);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

        string json = JsonSerializer.Serialize(loaded, options);
        Assert.Contains("\"equipmentDiffs\":[{\"field\":\"scale\",\"orderValue\":\"178\",\"listValue\":\"180\"}]", json);

        Care posted = JsonSerializer.Deserialize<Care>(json, options)!;
        Assert.Null(posted.equipmentDiffs);
        // 页面实际回传时不带整张订单图和照片的导航对象
        posted.order = null;
        posted.pickImage = null;
        foreach (CareImage image in posted.careImages)
        {
            image.care = null!;
            image.image = null!;
        }
        Assert.DoesNotContain(Validate(posted).Keys, k => k.Contains("equipmentDiffs"));

        await CareCtl(f).UpdateCare(posted, null, 10, "测试");

        Assert.Equal(0, await f.NewContext().coreDataModLog.CountAsync(l => l.field_name == "equipmentDiffs"));
    }
}
