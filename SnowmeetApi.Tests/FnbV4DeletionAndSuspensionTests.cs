using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

// In-memory SQLite only: never starts the application or reads its connection configuration.
public class FnbV4DeletionAndSuspensionTests
{
    private static async Task<ApplicationDBContext> CreateDb(SqliteConnection connection)
    {
        await connection.OpenAsync();
        var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE fnb_v4_area (id INTEGER PRIMARY KEY, shop_id INTEGER NOT NULL,
              parent_id INTEGER, name TEXT NOT NULL, area_type TEXT NOT NULL DEFAULT 'other',
              valid INTEGER NOT NULL DEFAULT 1, enabled INTEGER NOT NULL DEFAULT 1,
              sort INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL DEFAULT '2026-10-13', updated_at TEXT);
            CREATE TABLE fnb_v4_area_image (area_id INTEGER, upload_id INTEGER, staff_id INTEGER,
              created_at TEXT, valid INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE mini_upload (id INTEGER PRIMARY KEY, file_path_name TEXT);
            CREATE TABLE fnb_v4_batch_detail (batch_id INTEGER, area_id INTEGER, valid INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE fnb_v4_batch (id INTEGER PRIMARY KEY, shop_id INTEGER, quantity TEXT, valid INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE fnb_v4_supply (area_id INTEGER, shop_id INTEGER, valid INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE fnb_v4_tool (area_id INTEGER, shop_id INTEGER, status TEXT, valid INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE fnb_v4_tool_log (from_area_id INTEGER, to_area_id INTEGER, valid INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE fnb_v4_check_item (id INTEGER PRIMARY KEY, area_id INTEGER, name TEXT, kind TEXT,
              method TEXT, required INTEGER, photo_suggested INTEGER, unit TEXT, minimum TEXT, maximum TEXT,
              tool_id INTEGER, supply_id INTEGER, valid INTEGER NOT NULL DEFAULT 1,
              enabled INTEGER NOT NULL DEFAULT 1, updated_at TEXT);
            CREATE TABLE fnb_v4_category (id INTEGER PRIMARY KEY, parent_id INTEGER, level INTEGER NOT NULL,
              name TEXT NOT NULL, batch_code TEXT, measure_type TEXT, default_storage TEXT,
              warn_days INTEGER, open_days INTEGER, is_prepared INTEGER NOT NULL DEFAULT 0,
              sort INTEGER NOT NULL DEFAULT 0, valid INTEGER NOT NULL DEFAULT 1,
              enabled INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL DEFAULT '2026-10-13', updated_at TEXT);
            CREATE TABLE fnb_v4_item (category_id INTEGER, valid INTEGER NOT NULL DEFAULT 1);
            """);
        return db;
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static int[] AreaIds(object value) => Json(value).EnumerateArray()
        .Select(x => x.GetProperty("area").GetProperty("id").GetInt32()).ToArray();

    [Fact]
    public async Task IncludingSuspendedAreasNeverIncludesDeletedAreas()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDb(connection);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO fnb_v4_area (id,shop_id,name,valid,enabled) VALUES
              (1,10,'启用区域',1,1),(2,10,'停用区域',1,0),(3,10,'删除区域',0,1);
            """);
        var service = new FnbV4Service(db, 10);
        var active = await service.ListAreas();
        Assert.Equal(new[] { 1 }, AreaIds(active));
        var all = await service.ListAreas(true);
        Assert.Equal(new[] { 1, 2 }, AreaIds(all));
        Assert.False(Json(all)[1].GetProperty("effectiveValid").GetBoolean());
        Assert.Equal(3, await db.fnbArea.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task AreaSuspensionCanBeReversedWithoutChangingDeletionState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDb(connection);
        var service = new FnbV4Service(db, 10);
        var area = (FnbArea)await service.SaveArea(new(10, 0, null, "可停用区域"));
        await service.SaveArea(new(10, area.id, null, area.name, Enabled: false));
        Assert.Empty(AreaIds(await service.ListAreas()));
        Assert.Single(AreaIds(await service.ListAreas(true)));
        var disabled = await db.fnbArea.SingleAsync();
        Assert.True(disabled.valid);
        Assert.False(disabled.enabled);
        await service.SaveArea(new(10, area.id, null, area.name, Enabled: true));
        Assert.Single(AreaIds(await service.ListAreas()));
        Assert.True((await db.fnbArea.SingleAsync()).valid);
    }

    [Fact]
    public async Task DeletedAreaCannotBeRestoredByNewOrLegacySaveRequests()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDb(connection);
        var service = new FnbV4Service(db, 10);
        var area = (FnbArea)await service.SaveArea(new(10, 0, null, "删除区域"));
        await service.DeleteArea(area.id);
        Assert.Empty(AreaIds(await service.ListAreas(true)));
        Assert.False((await db.fnbArea.IgnoreQueryFilters().SingleAsync()).valid);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveArea(new(10, area.id, null, area.name, Enabled: true)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveArea(new(10, area.id, null, area.name, Valid: true)));
        Assert.False((await db.fnbArea.IgnoreQueryFilters().SingleAsync()).valid);
        // Recovery only happens after a direct database edit, never through a save/enable action.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fnb_v4_area SET valid=1 WHERE id={area.id}");
        Assert.Single(AreaIds(await service.ListAreas(true)));
    }

    [Fact]
    public async Task CategorySuspensionAndDeletionHaveDifferentSaveAndListBehavior()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDb(connection);
        var service = new FnbV4CatalogService(db);
        var category = await service.SaveCategoryAsync(new(10, 0, null, 1, "分类"));
        db.ChangeTracker.Clear();
        await service.SaveCategoryAsync(new(10, category.id, null, 1, category.name, Enabled: false));
        db.ChangeTracker.Clear();
        var stopped = await db.fnbCategory.SingleAsync();
        Assert.True(stopped.valid);
        Assert.False(stopped.enabled);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveCategoryAsync(new(10, 0, null, 1, category.name)));
        await service.SaveCategoryAsync(new(10, category.id, null, 1, category.name, Enabled: true));
        db.ChangeTracker.Clear();
        Assert.True((await db.fnbCategory.SingleAsync()).enabled);
        await service.DeleteCategoryAsync(category.id);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.fnbCategory.ToArrayAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveCategoryAsync(new(10, category.id, null, 1, category.name, Valid: true)));
        var replacement = await service.SaveCategoryAsync(new(10, 0, null, 1, category.name));
        Assert.NotEqual(category.id, replacement.id);
        Assert.False((await db.fnbCategory.IgnoreQueryFilters().SingleAsync(x => x.id == category.id)).valid);
    }
}
