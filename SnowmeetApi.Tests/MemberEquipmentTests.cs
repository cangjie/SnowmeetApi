using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;
using Xunit;

namespace SnowmeetApi.Tests;

public class MemberEquipmentTests
{
    private sealed class Http : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }

    private const string Clerk = "session-7";      // 店员（职级 100，staff 10）
    private const string Admin = "session-1";      // 管理员（职级 300，staff 9）
    private const string Customer = "session-3";   // 普通顾客

    private static MemberEquipmentController EquipCtl(CoachFixture f) => new(f.NewContext(), f.Config);
    private static CareController CareCtl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    private static OrderController OrderCtl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    private static MemberAdminController MemberAdminCtl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    private static MemberController MemberCtl(CoachFixture f) => new(f.NewContext(), f.Config);

    private static async Task<int> AddUploadAsync(CoachFixture f, string path = "/upload/20261127/board1.jpg", string? thumb = "/upload/20261127/board1_t.jpg")
    {
        var db = f.NewContext();
        var uf = new UploadFile
        {
            file_path_name = path,
            thumb = thumb,
            purpose = "养护开单",
            is_web = 1,
            create_date = DateTime.Now
        };
        db.UploadFile.Add(uf);
        await db.SaveChangesAsync();
        return uf.id;
    }

    // ---------- 1. 历史纯规则 ----------

    [Fact]
    public void HistoryRules_FormatsCreateUpdateDeleteMerge_NewestFirst()
    {
        DateTime t0 = new DateTime(2026, 11, 27, 9, 0, 0);
        Staff clerk = new Staff { id = 10, name = "王店员" };
        var logs = new List<CoreDataModLog>
        {
            new() { id = 1, table_name = "member_equipment", field_name = "create", key_value = 5, current_value = "双板 · Atomic 165cm", staff_id = 10, staff = clerk, scene = "养护开单新增装备", create_date = t0 },
            new() { id = 2, table_name = "member_equipment", field_name = "scale", key_value = 5, prev_value = "165", current_value = "168", staff_id = 10, staff = clerk, scene = "养护开单修改装备", manual_memo = "关联养护单 CL_YH_001", create_date = t0.AddMinutes(5) },
            new() { id = 3, table_name = "member_equipment", field_name = "serials", key_value = 5, prev_value = null, current_value = "L01|R02", staff_id = 10, staff = clerk, scene = "养护开单修改装备", create_date = t0.AddMinutes(5) },
            new() { id = 4, table_name = "member_equipment", field_name = "image_id", key_value = 5, prev_value = "11", current_value = "22", staff_id = 10, staff = clerk, scene = "会员装备管理修改", create_date = t0.AddMinutes(10) },
            new() { id = 5, table_name = "member_equipment", field_name = "valid", key_value = 5, prev_value = "1", current_value = "0", staff_id = null, scene = "会员装备管理删除", create_date = t0.AddMinutes(20) },
        };

        var photos = new Dictionary<int, string>
        {
            [11] = "https://img.snowmeet.top/upload/old.jpg",
            [22] = "https://img.snowmeet.top/upload/new.jpg",
        };

        var items = MemberEquipmentHistoryRules.Build(logs, photos);
        Assert.Equal(5, items.Count);

        Assert.Equal(("delete", "删除装备", "未记录"), (items[0].kind, items[0].fieldLabel, items[0].staffName));
        Assert.Equal(("update", "image_id", "https://img.snowmeet.top/upload/old.jpg", "https://img.snowmeet.top/upload/new.jpg"),
            (items[1].kind, items[1].field, items[1].fromPhotoUrl, items[1].toPhotoUrl));
        // 同一时间 t0+5m，id=3 在 id=2 之前
        Assert.Equal(("serials", "未填", "左:L01 / 右:R02"), (items[2].field, items[2].from, items[2].to));
        Assert.Equal(("scale", "165cm", "168cm", "关联养护单 CL_YH_001"), (items[3].field, items[3].from, items[3].to, items[3].memo));
        Assert.Equal(("create", "新建装备", "双板 · Atomic 165cm"), (items[4].kind, items[4].fieldLabel, items[4].to));
    }

    // ---------- 2. 管理接口权限与增删改查 ----------

    [Fact]
    public async Task Controller_RejectsNonStaff()
    {
        await using var f = await CoachFixture.Create();
        var listRes = (ApiResult<List<MemberEquipmentView>?>)((OkObjectResult)(await EquipCtl(f).ListByStaff(CoachFixture.StudentA, null, Customer)).Result!).Value!;
        Assert.Equal(1, listRes.code);
        Assert.Equal("没有权限", listRes.message);
    }

    [Fact]
    public async Task Controller_CreateUpdateDeleteAndHistory_WorksEndToEnd()
    {
        await using var f = await CoachFixture.Create();
        int img1 = await AddUploadAsync(f, "/upload/20261127/first.jpg", "/upload/20261127/first_t.jpg");
        int img2 = await AddUploadAsync(f, "/upload/20261127/second.jpg", "/upload/20261127/second_t.jpg");

        var createRes = (ApiResult<MemberEquipmentView?>)((OkObjectResult)(await EquipCtl(f).CreateByStaff(
            new MemberEquipmentController.SaveMemberEquipmentRequest
            {
                memberId = CoachFixture.StudentA,
                equipment = "单板",
                brand = "Burton/伯顿",
                scale = "156",
                serials = "BT-001",
                imageId = img1
            }, Clerk)).Result!).Value!;
        Assert.Equal(0, createRes.code);
        int equipId = createRes.data!.id;
        Assert.Contains("/upload/20261127/first_t.jpg", createRes.data.photoUrl);

        // 会员详情返回 equipmentCount = 1
        var detailObj = (ApiResult<object>)((OkObjectResult)(await MemberAdminCtl(f).GetMemberDetailByStaff(CoachFixture.StudentA, Clerk)).Result!).Value!;
        Assert.Equal(0, detailObj.code);
        var countProp = detailObj.data!.GetType().GetProperty("equipmentCount")!;
        Assert.Equal(1, (int)countProp.GetValue(detailObj.data)!);

        // 按类型过滤：单板有 1 件，双板 0 件
        var boardList = (ApiResult<List<MemberEquipmentView>?>)((OkObjectResult)(await EquipCtl(f).ListByStaff(CoachFixture.StudentA, "单板", Clerk)).Result!).Value!;
        var skiList = (ApiResult<List<MemberEquipmentView>?>)((OkObjectResult)(await EquipCtl(f).ListByStaff(CoachFixture.StudentA, "双板", Clerk)).Result!).Value!;
        Assert.Single(boardList.data!);
        Assert.Empty(skiList.data!);

        // 编辑长度和照片
        var updateRes = (ApiResult<MemberEquipmentView?>)((OkObjectResult)(await EquipCtl(f).UpdateByStaff(
            new MemberEquipmentController.SaveMemberEquipmentRequest
            {
                id = equipId,
                memberId = CoachFixture.StudentA,
                equipment = "单板",
                brand = "Burton/伯顿",
                scale = "158",
                serials = "BT-001",
                imageId = img2
            }, Clerk)).Result!).Value!;
        Assert.Equal(0, updateRes.code);
        Assert.Equal("158", updateRes.data!.scale);

        // 删除后列表为空，但历史记录完整保留 create + 2 项 update + delete
        var delRes = (ApiResult<object?>)((OkObjectResult)(await EquipCtl(f).DeleteByStaff(
            new MemberEquipmentController.DeleteMemberEquipmentRequest { id = equipId }, Clerk)).Result!).Value!;
        Assert.Equal(0, delRes.code);

        var afterDelList = (ApiResult<List<MemberEquipmentView>?>)((OkObjectResult)(await EquipCtl(f).ListByStaff(CoachFixture.StudentA, null, Clerk)).Result!).Value!;
        Assert.Empty(afterDelList.data!);

        var histRes = (ApiResult<List<MemberEquipmentHistoryItem>?>)((OkObjectResult)(await EquipCtl(f).GetHistoryByStaff(equipId, Clerk)).Result!).Value!;
        Assert.Equal(0, histRes.code);
        Assert.Equal(4, histRes.data!.Count); // delete + scale/image_id + create
        Assert.Equal("delete", histRes.data[0].kind);
        Assert.Equal("create", histRes.data[^1].kind);
    }

    // ---------- 3. 养护开单联动（新增入库、首次照片保留、修改写历史、旧客户端防重） ----------

    [Fact]
    public async Task PlaceCareOrder_NewEquipment_CreatesRecordWithFirstOrdinaryPhoto_AndSubsequentOrderKeepsFirstPhoto()
    {
        await using var f = await CoachFixture.Create();
        int repairVideoId = await AddUploadAsync(f, "/upload/20261127/repair.mp4", "/upload/20261127/repair.jpg");
        int repairBeforeImgId = await AddUploadAsync(f, "/upload/20261127/before.jpg", "/upload/20261127/before_t.jpg");
        int firstEquipPhotoId = await AddUploadAsync(f, "/upload/20261127/equip_first.jpg", "/upload/20261127/equip_first_t.jpg");
        int secondOrderPhotoId = await AddUploadAsync(f, "/upload/20261127/equip_second.jpg", "/upload/20261127/equip_second_t.jpg");

        // 第 1 单：新装备（member_equipment_id = -1），同时含维修前图片与视频，应只取普通装备照片 firstEquipPhotoId
        var draft1 = new SnowmeetApi.Models.Order
        {
            shop = "崇礼旗舰店",
            member_id = CoachFixture.StudentA,
            type = "养护",
            cares = new List<Care>
            {
                new()
                {
                    member_equipment_id = MemberEquipmentService.ExplicitNewSentinel,
                    equipment = "双板",
                    brand = "Atomic/阿托米克",
                    scale = "165",
                    serials = "AT-165",
                    need_edge = 1,
                    edge_degree = "89",
                    careImages = new List<CareImage>
                    {
                        new() { image_id = repairVideoId, valid = true },
                        new() { image_id = repairBeforeImgId, title = "维修前", valid = true },
                        new() { image_id = firstEquipPhotoId, valid = true }
                    }
                }
            }
        };
        var saved1 = ((ApiResult<SnowmeetApi.Models.Order?>)((OkObjectResult)(await CareCtl(f).SaveCareRecept(draft1, Clerk)).Result!).Value!).data!;
        var placed1 = ((ApiResult<SnowmeetApi.Models.Order>)((OkObjectResult)(await OrderCtl(f).PlaceCareOrder(saved1.id, Clerk)).Result!).Value!).data!;

        int createdEquipId = placed1.cares![0].member_equipment_id ?? 0;
        Assert.True(createdEquipId > 0);

        var dbEquip = await f.NewContext().memberEquipment.AsNoTracking().SingleAsync(e => e.id == createdEquipId);
        Assert.Equal(firstEquipPhotoId, dbEquip.image_id);
        Assert.Equal("AT-165", dbEquip.serials);

        // 第 2 单：选中该已有装备（member_equipment_id = createdEquipId），拍了新板况照 secondOrderPhotoId，并把长度改为 168
        var draft2 = new SnowmeetApi.Models.Order
        {
            shop = "崇礼旗舰店",
            member_id = CoachFixture.StudentA,
            type = "养护",
            cares = new List<Care>
            {
                new()
                {
                    member_equipment_id = createdEquipId,
                    equipment = "双板",
                    brand = "Atomic/阿托米克",
                    scale = "168",
                    serials = "AT-165",
                    need_wax = 1,
                    need_unwax = 1,
                    careImages = new List<CareImage>
                    {
                        new() { image_id = secondOrderPhotoId, valid = true }
                    }
                }
            }
        };
        var saved2 = ((ApiResult<SnowmeetApi.Models.Order?>)((OkObjectResult)(await CareCtl(f).SaveCareRecept(draft2, Clerk)).Result!).Value!).data!;
        var placed2 = ((ApiResult<SnowmeetApi.Models.Order>)((OkObjectResult)(await OrderCtl(f).PlaceCareOrder(saved2.id, Clerk)).Result!).Value!).data!;
        Assert.Equal(createdEquipId, placed2.cares![0].member_equipment_id);

        // 验证：长度已更新为 168，但装备档案照片仍保持第一次开单时的 firstEquipPhotoId，且生成了修改历史
        var dbEquipAfter = await f.NewContext().memberEquipment.AsNoTracking().SingleAsync(e => e.id == createdEquipId);
        Assert.Equal("168", dbEquipAfter.scale);
        Assert.Equal(firstEquipPhotoId, dbEquipAfter.image_id);

        var hist = ((ApiResult<List<MemberEquipmentHistoryItem>?>)((OkObjectResult)(await EquipCtl(f).GetHistoryByStaff(createdEquipId, Clerk)).Result!).Value!).data!;
        Assert.Equal(2, hist.Count);
        Assert.Equal(("update", "scale", "165cm", "168cm"), (hist[0].kind, hist[0].field, hist[0].from, hist[0].to));
        Assert.Equal("养护开单修改装备", hist[0].scene);
        Assert.Equal("create", hist[1].kind);
    }

    [Fact]
    public async Task PlaceCareOrder_OldClientWithoutId_ReusesMatchingEquipment_WhileExplicitNewAddsSecondBoard()
    {
        await using var f = await CoachFixture.Create();
        int photoId = await AddUploadAsync(f);

        await EquipCtl(f).CreateByStaff(new MemberEquipmentController.SaveMemberEquipmentRequest
        {
            memberId = CoachFixture.StudentA,
            equipment = "单板",
            brand = "Salomon/萨洛蒙",
            scale = "155",
            imageId = photoId
        }, Clerk);

        // 旧版小程序未传 member_equipment_id（null）：自动复用同品牌同长度装备，不重复新建
        var oldClientDraft = new SnowmeetApi.Models.Order
        {
            shop = "崇礼旗舰店",
            member_id = CoachFixture.StudentA,
            type = "养护",
            cares = new List<Care>
            {
                new()
                {
                    member_equipment_id = null,
                    equipment = "单板",
                    brand = "Salomon/萨洛蒙",
                    scale = "155",
                    need_wax = 1,
                    need_unwax = 1,
                    careImages = new List<CareImage> { new() { image_id = photoId, valid = true } }
                }
            }
        };
        var savedOld = ((ApiResult<SnowmeetApi.Models.Order?>)((OkObjectResult)(await CareCtl(f).SaveCareRecept(oldClientDraft, Clerk)).Result!).Value!).data!;
        await OrderCtl(f).PlaceCareOrder(savedOld.id, Clerk);
        Assert.Equal(1, await f.NewContext().memberEquipment.CountAsync(e => e.member_id == CoachFixture.StudentA && e.valid == 1));

        // 新版小程序明确录入第二块同款新板（member_equipment_id = -1）：正常新增第二条记录
        var newClientDraft = new SnowmeetApi.Models.Order
        {
            shop = "崇礼旗舰店",
            member_id = CoachFixture.StudentA,
            type = "养护",
            cares = new List<Care>
            {
                new()
                {
                    member_equipment_id = MemberEquipmentService.ExplicitNewSentinel,
                    equipment = "单板",
                    brand = "Salomon/萨洛蒙",
                    scale = "155",
                    serials = "SECOND-BOARD",
                    need_wax = 1,
                    need_unwax = 1,
                    careImages = new List<CareImage> { new() { image_id = photoId, valid = true } }
                }
            }
        };
        var savedNew = ((ApiResult<SnowmeetApi.Models.Order?>)((OkObjectResult)(await CareCtl(f).SaveCareRecept(newClientDraft, Clerk)).Result!).Value!).data!;
        await OrderCtl(f).PlaceCareOrder(savedNew.id, Clerk);
        Assert.Equal(2, await f.NewContext().memberEquipment.CountAsync(e => e.member_id == CoachFixture.StudentA && e.valid == 1));
    }

    // ---------- 4. 会员合并迁移装备 ----------

    [Fact]
    public async Task MergeMember_MigratesEquipmentsAndLogsHistory()
    {
        await using var f = await CoachFixture.Create();
        int photoId = await AddUploadAsync(f);
        var created = ((ApiResult<MemberEquipmentView?>)((OkObjectResult)(await EquipCtl(f).CreateByStaff(
            new MemberEquipmentController.SaveMemberEquipmentRequest
            {
                memberId = CoachFixture.StudentB,
                equipment = "双板",
                brand = "Head/海德",
                scale = "170",
                imageId = photoId
            }, Clerk)).Result!).Value!).data!;

        await MemberCtl(f).MergeMember(CoachFixture.StudentB, CoachFixture.StudentA);

        var targetList = ((ApiResult<List<MemberEquipmentView>?>)((OkObjectResult)(await EquipCtl(f).ListByStaff(CoachFixture.StudentA, null, Clerk)).Result!).Value!).data!;
        Assert.Contains(targetList, e => e.id == created.id);

        var hist = ((ApiResult<List<MemberEquipmentHistoryItem>?>)((OkObjectResult)(await EquipCtl(f).GetHistoryByStaff(created.id, Admin)).Result!).Value!).data!;
        Assert.Contains(hist, h => h.kind == "merge" && h.to == "会员#" + CoachFixture.StudentA);
    }
}
