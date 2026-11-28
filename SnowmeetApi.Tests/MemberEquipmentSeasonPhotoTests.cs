using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

public class MemberEquipmentSeasonPhotoTests
{
    private static readonly DateTime Now = new(2026, 11, 28, 12, 0, 0);

    private static async Task<Care> AddCare(CoachFixture f, int? equipmentId, DateTime date, string brand = "Atomic", bool cancelled = false)
    {
        await using var db = f.NewContext();
        var order = new Order { member_id = CoachFixture.StudentA, type = "养护", valid = 1, create_date = date };
        db.order.Add(order); await db.SaveChangesAsync();
        var care = new Care { order_id = order.id, member_equipment_id = equipmentId, equipment = "双板",
            brand = brand, scale = "165", valid = 1, is_cancel = cancelled, create_date = date };
        db.care.Add(care); await db.SaveChangesAsync();
        return care;
    }

    private static async Task AddPhoto(CoachFixture f, int careId, string path, string? title = null)
    {
        await using var db = f.NewContext();
        var file = new UploadFile { file_path_name = path, thumb = path + ".thumb", purpose = "养护开单" };
        db.UploadFile.Add(file); await db.SaveChangesAsync();
        db.careImage.Add(new CareImage { care_id = careId, image_id = file.id, valid = true, title = title! });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task 本季首次养护的第一张普通照片作为封面_原档案照片保持不变()
    {
        await using var f = await CoachFixture.Create();
        await using var db = f.NewContext();
        var original = new UploadFile { file_path_name = "/upload/original.jpg", thumb = "/upload/original_t.jpg" };
        db.UploadFile.Add(original); await db.SaveChangesAsync();
        var equipment = new MemberEquipment { member_id = CoachFixture.StudentA, equipment = "双板", brand = "Atomic", scale = "165", image_id = original.id };
        db.memberEquipment.Add(equipment); await db.SaveChangesAsync();
        var old = await AddCare(f, equipment.id, new DateTime(2026, 9, 30, 9, 0, 0));
        var first = await AddCare(f, equipment.id, new DateTime(2026, 10, 1, 9, 0, 0));
        var next = await AddCare(f, equipment.id, new DateTime(2026, 11, 1, 9, 0, 0));
        await AddPhoto(f, old.id, "/upload/old.jpg");
        await AddPhoto(f, first.id, "/upload/before.jpg", "维修前");
        await AddPhoto(f, first.id, "/upload/video.mp4");
        await AddPhoto(f, first.id, "/upload/season.jpg");
        await AddPhoto(f, next.id, "/upload/later.jpg");
        var view = Assert.Single(await new MemberEquipmentService(f.NewContext(), f.Config).ListAsync(CoachFixture.StudentA, null, Now));
        Assert.EndsWith("/upload/season.jpg.thumb", view.seasonPhotoUrl);
        Assert.EndsWith("/upload/season.jpg", view.seasonPhotoFullUrl);
        Assert.EndsWith("/upload/original_t.jpg", view.photoUrl);
        Assert.Equal(original.id, view.image_id);
        Assert.Equal(original.id, (await f.NewContext().memberEquipment.AsNoTracking().SingleAsync()).image_id);
    }

    [Fact]
    public async Task 本季第一单没有普通照片时保留原图_不借用后续养护照片()
    {
        await using var f = await CoachFixture.Create();
        await using var db = f.NewContext();
        var equipment = new MemberEquipment { member_id = CoachFixture.StudentA, equipment = "双板", brand = "Atomic", scale = "165" };
        db.memberEquipment.Add(equipment); await db.SaveChangesAsync();
        var first = await AddCare(f, equipment.id, new DateTime(2026, 10, 1));
        var later = await AddCare(f, equipment.id, new DateTime(2026, 10, 2));
        await AddPhoto(f, first.id, "/upload/repair.jpg", "维修后");
        await AddPhoto(f, later.id, "/upload/later.jpg");
        var view = Assert.Single(await new MemberEquipmentService(f.NewContext(), f.Config).ListAsync(CoachFixture.StudentA, null, Now));
        Assert.Null(view.seasonPhotoUrl);
    }

    [Fact]
    public async Task 历史养护仅在装备唯一匹配时选图_取消或未来记录不参与()
    {
        await using var f = await CoachFixture.Create();
        await using var db = f.NewContext();
        var first = new MemberEquipment { member_id = CoachFixture.StudentA, equipment = "双板", brand = "Atomic", scale = "165" };
        db.memberEquipment.Add(first); await db.SaveChangesAsync();
        var cancelled = await AddCare(f, null, new DateTime(2026, 10, 1), cancelled: true);
        var legacy = await AddCare(f, null, new DateTime(2026, 10, 2));
        var future = await AddCare(f, null, Now.AddDays(1));
        await AddPhoto(f, cancelled.id, "/upload/cancelled.jpg");
        await AddPhoto(f, legacy.id, "/upload/legacy.jpg");
        await AddPhoto(f, future.id, "/upload/future.jpg");
        var service = new MemberEquipmentService(f.NewContext(), f.Config);
        var unique = Assert.Single(await service.ListAsync(CoachFixture.StudentA, null, Now));
        Assert.EndsWith("/upload/legacy.jpg", unique.seasonPhotoFullUrl);
        db.memberEquipment.Add(new MemberEquipment { member_id = CoachFixture.StudentA, equipment = "双板", brand = "Atomic", scale = "165" });
        await db.SaveChangesAsync();
        var ambiguous = await service.ListAsync(CoachFixture.StudentA, null, Now);
        Assert.Equal(2, ambiguous.Count);
        Assert.All(ambiguous, x => Assert.Null(x.seasonPhotoUrl));
        await db.memberEquipment.Where(e => e.id == first.id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.source_care_id, legacy.id));
        var identified = await service.ListAsync(CoachFixture.StudentA, null, Now);
        Assert.EndsWith("/upload/legacy.jpg", identified.Single(e => e.id == first.id).seasonPhotoFullUrl);
        Assert.Null(identified.Single(e => e.id != first.id).seasonPhotoUrl);
    }

    [Fact]
    public async Task 已删除的同规格装备仍会阻止不明确的历史记录被错配()
    {
        await using var f = await CoachFixture.Create();
        await using var db = f.NewContext();
        db.memberEquipment.AddRange(
            new MemberEquipment { member_id = CoachFixture.StudentA, equipment = "双板", brand = "Atomic", scale = "165", valid = 1 },
            new MemberEquipment { member_id = CoachFixture.StudentA, equipment = "双板", brand = "Atomic", scale = "165", valid = 0 });
        await db.SaveChangesAsync();
        var legacy = await AddCare(f, null, new DateTime(2026, 10, 2));
        await AddPhoto(f, legacy.id, "/upload/legacy.jpg");
        var result = Assert.Single(await new MemberEquipmentService(f.NewContext(), f.Config).ListAsync(CoachFixture.StudentA, null, Now));
        Assert.Null(result.seasonPhotoUrl);
    }
}
