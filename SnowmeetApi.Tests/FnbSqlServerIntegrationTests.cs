using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Tests;

public sealed class FnbSqlServerFactAttribute : FactAttribute
{
    public FnbSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SNOWMEET_FNB_TEST_SQLSERVER")))
            Skip = "仅通过 LocalDB 临时库运行器执行";
    }
}

public class FnbSqlServerIntegrationTests
{
    private static int _sequence;
    private static ApplicationDBContext Open()
    {
        string connection = Environment.GetEnvironmentVariable("SNOWMEET_FNB_TEST_SQLSERVER") ?? throw new InvalidOperationException("缺少隔离测试连接");
        var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
        if (!b.DataSource.Equals(@"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase) ||
            !b.InitialCatalog.StartsWith("snowmeet_fnb_test_", StringComparison.Ordinal) || !b.IntegratedSecurity)
            throw new InvalidOperationException("集成测试仅允许本机 LocalDB + snowmeet_fnb_test_ 临时库");
        return new(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlServer(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
    }
    private sealed record Seed(int ShopId, int ManagerId, int WorkerId, string Mini, string WeCom,
        string WorkerMini, string WeComId, int ParentId, int CategoryId, int ItemId, int FinalFormId);
    private static async Task<T> Catalog<T>(Seed s, Func<FnbCatalogController, Task<ApiResult<object>>> call)
    {
        await using var db = Open(); var result = await call(new(db));
        Assert.True(result.code == 0, $"code={result.code}: {result.message}"); return Assert.IsType<T>(result.data);
    }
    private static async Task<ApiResult<object>> Route(Seed s, Func<FnbRouteController, Task<ApiResult<object>>> call)
    { await using var db = Open(); return await call(new(db)); }
    private static async Task<Seed> SeedAsync()
    {
        await using var db = Open(); int seq = Interlocked.Increment(ref _sequence);
        string key = Guid.NewGuid().ToString("N")[..12];
        var shop = new Shop { name = "v4测试店" + key, code = "T" + seq.ToString("D3") };
        db.shop.Add(shop); await db.SaveChangesAsync();
        var manager = new Staff { name = "店长" + key, gender = "男", title_level = 200, base_shop_id = shop.id, valid = 1 };
        var worker = new Staff { name = "店员" + key, gender = "女", title_level = 100, base_shop_id = shop.id, valid = 1 };
        db.staff.AddRange(manager, worker); await db.SaveChangesAsync();
        var job = new SocialAccountForJob { cell = "1390000" + seq.ToString("D4"), wechat_mini_openid = "mini" + key, member_id = 100000 + seq, is_private = 0 };
        var workerJob = new SocialAccountForJob { cell = "1380000" + seq.ToString("D4"), wechat_mini_openid = "worker" + key, member_id = 200000 + seq, is_private = 0 };
        db.socialAccountForJob.AddRange(job, workerJob); await db.SaveChangesAsync();
        db.staffSocialAccount.AddRange(
            new StaffSocialAccount { staff_id = manager.id, social_account_id = job.id, valid = 1, start_date = DateTime.Now.AddDays(-1), season_memo = "" },
            new StaffSocialAccount { staff_id = worker.id, social_account_id = workerJob.id, valid = 1, start_date = DateTime.Now.AddDays(-1), season_memo = "" });
        db.memberSocialAccount.Add(new MemberSocialAccount { member_id = job.member_id, type = MemberSocialAccount.TYPE_WECOM, num = "wecom" + key, valid = 1, memo = "" });
        string mini = "M" + key, wecom = "W" + key, workerMini = "E" + key;
        db.miniSession.AddRange(
            new MiniSession { session_key = mini, session_type = "wechat_mini_openid", wechat_openid = job.wechat_mini_openid, valid = 1, expire_date = DateTime.Now.AddDays(1) },
            new MiniSession { session_key = wecom, session_type = "wecom_userid", wechat_openid = "wecom" + key, valid = 1, expire_date = DateTime.Now.AddDays(1) },
            new MiniSession { session_key = workerMini, session_type = "wechat_mini_openid", wechat_openid = workerJob.wechat_mini_openid, valid = 1, expire_date = DateTime.Now.AddDays(1) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var catalog = new FnbCatalogController(db);
        var p = await catalog.SaveCategory(mini, new(shop.id, 0, null, 1, "原料" + key));
        Assert.Equal(0, p.code); var parent = Assert.IsType<FnbCategory>(p.data); db.ChangeTracker.Clear();
        var c = await catalog.SaveCategory(mini, new(shop.id, 0, parent.id, 2, "肉类" + key, "B" + key.ToUpperInvariant(), "weight", "frozen", 2, 3));
        Assert.Equal(0, c.code); var child = Assert.IsType<FnbCategory>(c.data); db.ChangeTracker.Clear();
        var r = await new FnbRouteController(db).CreateItem(mini, new(shop.id, child.id, "牛肉", "牛肉片", "g"));
        Assert.Equal(0, r.code); var item = Assert.IsType<FnbItem>(r.data);
        var final = await db.fnbItemForm.SingleAsync(x => x.item_id == item.id);
        return new(shop.id, manager.id, worker.id, mini, wecom, workerMini, "wecom" + key, parent.id, child.id, item.id, final.id);
    }
    private static IConfiguration Config => new ConfigurationBuilder().Build();
    private sealed class Gateway(string? id) : IFnbWeComLoginGateway
    { public Task<string?> ExchangeCodeAsync(string code) => Task.FromResult(id); }

    private sealed class ConflictProbe(ApplicationDBContext db) : FnbV4ControllerBase(db)
    {
        public Task<ApiResult<object>> SaveThenFail(Seed seed, string name, TaskCompletionSource locked) => Execute(seed.Mini, seed.ShopId, true, async () =>
        {
            await db.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY LOW");
            db.fnbCategory.Add(new FnbCategory { level = 1, name = name, valid = true, created_at = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE fnb_v4_category SET sort=sort+1 WHERE id={0}", seed.ParentId);
            locked.SetResult();
            try { await db.Database.ExecuteSqlRawAsync("UPDATE fnb_v4_category SET sort=sort+1 WHERE id={0}", seed.CategoryId); }
            catch (Microsoft.Data.SqlClient.SqlException ex) { throw new DbUpdateException("包装真实 SQL 死锁", ex); }
            return new object();
        });
    }

    [FnbSqlServerFact]
    public async Task SqlDeadlockMapsToConflictAndRollsBackMasterDataWrite()
    {
        var s = await SeedAsync(); await using var db = Open();
        string name = "死锁回滚" + Guid.NewGuid().ToString("N");
        await using var competitor = Open();
        await using var transaction = await competitor.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await competitor.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY HIGH");
        await competitor.Database.ExecuteSqlRawAsync("UPDATE fnb_v4_category SET sort=sort+1 WHERE id={0}", s.CategoryId);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new ConflictProbe(db).SaveThenFail(s, name, locked);
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(45));
        // 两个事务反向获取分类行锁，真实制造 1205；所有数据均在临时 LocalDB 中。
        await competitor.Database.ExecuteSqlRawAsync("UPDATE fnb_v4_category SET sort=sort+1 WHERE id={0}", s.ParentId);
        await transaction.RollbackAsync();
        Assert.Equal(4, (await pending).code);
        Assert.Empty(db.ChangeTracker.Entries());
        await using var fresh = Open();
        Assert.False(await fresh.fnbCategory.AnyAsync(x => x.name == name));
    }

    [FnbSqlServerFact]
    public async Task BothSessionsResolveSameIdentityAndEnforceRoleAndShop()
    {
        var s = await SeedAsync(); await using var db = Open(); var auth = new FnbAuthController(db, Config);
        var mini = await auth.GetMe(s.Mini); var wecom = await auth.GetMe(s.WeCom);
        Assert.Equal(0, mini.code); Assert.Equal(0, wecom.code);
        Assert.Equal(s.ManagerId, mini.data!.StaffId); Assert.Equal(s.ManagerId, wecom.data!.StaffId);
        Assert.Equal("mini", mini.data.ClientType); Assert.Equal("wecom", wecom.data.ClientType); Assert.True(mini.data.IsManager);
        var catalog = new FnbCatalogController(db);
        Assert.Equal(2, (await catalog.ListCategories("missing", s.ShopId)).code);
        Assert.Equal(3, (await catalog.ListCategories(s.Mini, s.ShopId + 100000)).code);
        Assert.Equal(0, (await catalog.ListCategories(s.WorkerMini, s.ShopId)).code);
        Assert.Equal(3, (await catalog.SaveCategory(s.WorkerMini, new(s.ShopId, 0, null, 1, "无权限"))).code);
    }
    [FnbSqlServerFact]
    public async Task WeComLoginUsesStaffGateAndDoesNotIssueSessionForNonEmployees()
    {
        var s = await SeedAsync(); await using var db = Open();
        int before = await db.miniSession.CountAsync();
        var denied = await new FnbAuthController(db, Config, new Gateway("unknown")).WeComLogin(new("test-code"));
        Assert.Equal(3, denied.code); Assert.Equal(before, await db.miniSession.CountAsync());
        Assert.Equal(1, (await new FnbAuthController(db, Config, new Gateway(null)).WeComLogin(new("test-code"))).code);
        var login = await new FnbAuthController(db, Config, new Gateway(s.WeComId)).WeComLogin(new("test-code"));
        Assert.Equal(0, login.code); Assert.Equal(s.ManagerId, login.data!.StaffId); Assert.False(string.IsNullOrWhiteSpace(login.data.SessionKey));
        Assert.Equal(0, (await new FnbAuthController(db, Config).GetMe(login.data.SessionKey!)).code);
    }
    [FnbSqlServerFact]
    public async Task ExpiredResignedAndUnassignedStaffCannotUseV4()
    {
        var s = await SeedAsync(); await using var db = Open();
        var session = await db.miniSession.SingleAsync(x => x.session_key == s.Mini);
        session.expire_date = DateTime.Now.AddDays(-1); db.Entry(session).State = EntityState.Modified; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(2, (await new FnbAuthController(db, Config).GetMe(s.Mini)).code);
        var staff = await db.staff.SingleAsync(x => x.id == s.ManagerId); staff.base_shop_id = null;
        db.Entry(staff).State = EntityState.Modified; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(3, (await new FnbAuthController(db, Config).GetMe(s.WeCom)).code);
        staff = await db.staff.SingleAsync(x => x.id == s.ManagerId); staff.valid = 0;
        db.Entry(staff).State = EntityState.Modified; await db.SaveChangesAsync();
        Assert.Equal(2, (await new FnbAuthController(db, Config).GetMe(s.WeCom)).code);
    }
    [FnbSqlServerFact]
    public async Task NoTrackingUpdatesCategoryDefaultsAndItemOverrides()
    {
        var s = await SeedAsync();
        await Catalog<FnbCategory>(s, c => c.SaveCategory(s.WeCom, new(s.ShopId, s.CategoryId, s.ParentId, 2, "更新肉类", "C" + s.ItemId, "weight", "chilled", 5, 7)));
        var save = await Route(s, r => r.SaveItemDefaults(s.Mini, new(s.ShopId, s.ItemId, 0, 2))); Assert.Equal(0, save.code);
        await using var db = Open(); var item = await db.fnbItem.SingleAsync(x => x.id == s.ItemId); var category = await db.fnbCategory.SingleAsync(x => x.id == s.CategoryId);
        Assert.Equal("更新肉类", category.name); Assert.Equal((0, (int?)2), FnbV4Rules.Defaults(item, category));
        Assert.Equal(0, (await Route(s, r => r.SaveItemDefaults(s.Mini, new(s.ShopId, s.ItemId, null, null)))).code);
        await using var fresh = Open(); Assert.Equal((5, (int?)7), FnbV4Rules.Defaults(await fresh.fnbItem.SingleAsync(x => x.id == s.ItemId), category));
    }
    [FnbSqlServerFact]
    public async Task CategoriesCannotDeleteOrChangeDimensionWithItemsAndCanReuseDisabledName()
    {
        var s = await SeedAsync(); await using var db = Open(); var c = new FnbCatalogController(db);
        Assert.Equal(1, (await c.DeleteCategory(s.Mini, new(s.ShopId, s.ParentId))).code);
        Assert.Equal(1, (await c.SaveCategory(s.Mini, new(s.ShopId, s.CategoryId, s.ParentId, 2, "肉", "D" + s.ItemId, "volume", "chilled"))).code);
        Assert.Equal(1, (await c.SaveCategory(s.Mini, new(s.ShopId, s.CategoryId, s.ParentId, 2, "肉", "D" + s.ItemId, "weight", "chilled", IsPrepared: true))).code);
        var created = await c.SaveCategory(s.Mini, new(s.ShopId, 0, null, 1, "可删除" + s.ItemId));
        Assert.Equal(0, created.code); var empty = Assert.IsType<FnbCategory>(created.data); db.ChangeTracker.Clear();
        Assert.Equal(0, (await c.DeleteCategory(s.Mini, new(s.ShopId, empty.id))).code); db.ChangeTracker.Clear();
        Assert.False((await db.fnbCategory.SingleAsync(x => x.id == empty.id)).valid);
        Assert.Equal(0, (await c.SaveCategory(s.Mini, new(s.ShopId, 0, null, 1, empty.name))).code);
    }
    [FnbSqlServerFact]
    public async Task CategoryDeterminesItemTypeAndUnitsForAllDimensions()
    {
        var s = await SeedAsync();
        foreach (var (measure, unit, prepared) in new[] { ("weight", "g", false), ("volume", "ml", false), ("count", "piece", true) })
        {
            var category = await Catalog<FnbCategory>(s, c => c.SaveCategory(s.Mini,
                new(s.ShopId, 0, s.ParentId, 2, measure + s.ItemId, "U" + measure.ToUpperInvariant() + s.ItemId,
                    measure, "chilled", IsPrepared: prepared)));
            Assert.Equal(1, (await Route(s, r => r.CreateItem(s.Mini,
                new(s.ShopId, category.id, "错误单位", "出品态", "kg")))).code);
            var created = await Route(s, r => r.CreateItem(s.WeCom,
                new(s.ShopId, category.id, "按分类创建", "出品态", unit)));
            Assert.Equal(0, created.code); var item = Assert.IsType<FnbItem>(created.data);
            Assert.Equal(prepared ? "prepared" : "raw", item.item_type);
            await using var db = Open();
            var final = await db.fnbItemForm.SingleAsync(x => x.item_id == item.id);
            Assert.Equal(unit, final.unit_name); Assert.Equal(1m, final.per_base); Assert.Equal("chilled", final.storage_type);
            var listed = await new FnbCatalogController(db).ListUnits(s.Mini, s.ShopId, measure);
            Assert.Equal(0, listed.code);
            Assert.All(Assert.IsType<List<FnbUnit>>(listed.data), x => Assert.Equal(FnbV4Rules.Dimension(measure), x.dimension));
        }
    }
    [FnbSqlServerFact]
    public async Task SeasonalRulesAndDisabledFlagPersist()
    {
        var s = await SeedAsync();
        var c = await Catalog<FnbShelfLifeRule>(s, x => x.SaveShelfRule(s.Mini, new(s.ShopId, 0, s.CategoryId, null, "frozen", "warm", 20)));
        var i = await Catalog<FnbShelfLifeRule>(s, x => x.SaveShelfRule(s.WeCom, new(s.ShopId, 0, null, s.ItemId, "frozen", "all", 10)));
        await using var db = Open(); var rules = await db.fnbShelfLifeRule.ToListAsync();
        Assert.Equal(i.id, FnbV4Rules.ShelfRule(rules, s.ItemId, s.CategoryId, "frozen", 6)!.id);
        await Catalog<FnbShelfLifeRule>(s, x => x.SaveShelfRule(s.Mini, new(s.ShopId, i.id, null, s.ItemId, "frozen", "all", 10, false)));
        await using var fresh = Open(); rules = await fresh.fnbShelfLifeRule.ToListAsync();
        Assert.Equal(c.id, FnbV4Rules.ShelfRule(rules, s.ItemId, s.CategoryId, "frozen", 9)!.id);
        Assert.Null(FnbV4Rules.ShelfRule(rules, s.ItemId, s.CategoryId, "frozen", 10));
    }
    [FnbSqlServerFact]
    public async Task FormChainReordersWithUniqueIndicesAndServerCalculatedRatios()
    {
        var s = await SeedAsync();
        var a = await Route(s, r => r.AddUpstreamForm(s.Mini, new(s.ShopId, s.ItemId, "解冻态", "kg", 1000, "chilled", "T", "切片", 0.8m)));
        Assert.Equal(0, a.code); var first = Assert.IsType<FnbItemForm>(a.data);
        var b = await Route(s, r => r.AddUpstreamForm(s.WeCom, new(s.ShopId, s.ItemId, "采购态", "箱", 10000, "frozen", "B", "冷藏解冻", 1, 24)));
        Assert.Equal(0, b.code); var top = Assert.IsType<FnbItemForm>(b.data);
        await using var db = Open(); var forms = await db.fnbItemForm.Where(x => x.item_id == s.ItemId && x.valid).OrderBy(x => x.seq).ToListAsync();
        Assert.Equal(new[] { 0, 1, 2 }, forms.Select(x => x.seq)); Assert.Null(forms[0].in_op_name);
        Assert.Equal(10m, forms[1].in_op_ratio); Assert.Equal(1000m, forms[2].in_op_ratio); Assert.Equal(24m, forms[1].in_op_hours);
        Assert.Equal(1, (await Route(s, r => r.RemoveForm(s.Mini, new(s.ShopId, s.ItemId, first.id)))).code);
        Assert.Equal(0, (await Route(s, r => r.RemoveForm(s.Mini, new(s.ShopId, s.ItemId, top.id)))).code);
        await using var fresh = Open(); forms = await fresh.fnbItemForm.Where(x => x.item_id == s.ItemId && x.valid).OrderBy(x => x.seq).ToListAsync();
        Assert.Equal(new[] { 0, 1 }, forms.Select(x => x.seq)); Assert.Null(forms[0].in_op_name);
        Assert.Equal(0, (await Route(s, r => r.RemoveForm(s.Mini, new(s.ShopId, s.ItemId, first.id)))).code);
        Assert.Equal(1, (await Route(s, r => r.RemoveForm(s.Mini, new(s.ShopId, s.ItemId, s.FinalFormId)))).code);
    }
    [FnbSqlServerFact]
    public async Task FormUpdatePersistsAndCannotChangeReferencedUnitsOrFinalConversion()
    {
        var s = await SeedAsync();
        var added = await Route(s, r => r.AddUpstreamForm(s.Mini, new(s.ShopId, s.ItemId, "箱", "箱", 1000, "frozen", "B", "拆箱"))); Assert.Equal(0, added.code);
        var form = Assert.IsType<FnbItemForm>(added.data);
        Assert.Equal(0, (await Route(s, r => r.UpdateForm(s.Mini, new(s.ShopId, s.ItemId, form.id, "采购箱", "箱", 2000, "frozen", "B")))).code);
        await using var db = Open(); Assert.Equal(2000, (await db.fnbItemForm.SingleAsync(x => x.id == form.id)).per_base);
        Assert.Equal(2000, (await db.fnbItemForm.SingleAsync(x => x.id == s.FinalFormId)).in_op_ratio);
        Assert.Equal(0, (await Route(s, r => r.SaveSpec(s.Mini, new(s.ShopId, s.ItemId, 0, form.id, "被引用的箱")))).code);
        Assert.Equal(1, (await Route(s, r => r.UpdateForm(s.Mini, new(s.ShopId, s.ItemId, form.id, "采购箱", "箱", 3000, "frozen", "B")))).code);
        Assert.Equal(1, (await Route(s, r => r.RemoveForm(s.Mini, new(s.ShopId, s.ItemId, form.id)))).code);
        Assert.Equal(1, (await Route(s, r => r.UpdateForm(s.Mini, new(s.ShopId, s.ItemId, s.FinalFormId, "出品", "g", 2, "chilled", "F", OpName: "拆箱")))).code);
    }
    [FnbSqlServerFact]
    public async Task SpecsEnforceGlobalBarcodesAndProtectEntryFormAndReceiptReferences()
    {
        var s = await SeedAsync(); string barcode = "BC" + Guid.NewGuid().ToString("N");
        var saved = await Route(s, r => r.SaveSpec(s.Mini, new(s.ShopId, s.ItemId, 0, s.FinalFormId, "牛肉", Barcode: barcode))); Assert.Equal(0, saved.code);
        var spec = Assert.IsType<FnbPurchaseSpec>(saved.data);
        Assert.Equal(1, (await Route(s, r => r.SaveSpec(s.Mini, new(s.ShopId, s.ItemId, 0, s.FinalFormId, "重复条码", Barcode: barcode)))).code);
        var otherResult = await Route(s, r => r.CreateItem(s.Mini, new(s.ShopId, s.CategoryId, "猪肉", "猪肉片", "g")));
        Assert.Equal(0, otherResult.code); var other = Assert.IsType<FnbItem>(otherResult.data);
        await using var lookup = Open();
        int otherFormId = (await lookup.fnbItemForm.SingleAsync(x => x.item_id == other.id)).id;
        Assert.Equal(1, (await Route(s, r => r.SaveSpec(s.Mini, new(s.ShopId, other.id, 0, otherFormId, "跨食材重复条码", Barcode: barcode)))).code);
        Assert.Equal(0, (await Route(s, r => r.FindSpecByBarcode(s.WorkerMini, s.ShopId, barcode))).code);
        Assert.Equal(1, (await Route(s, r => r.SaveSpec(s.Mini, new(s.ShopId, s.ItemId, 0, s.FinalFormId + 100000, "跨食材")))).code);
        await using var db = Open(); db.fnbBatch.Add(new FnbBatch { shop_id = s.ShopId, item_id = s.ItemId, form_id = s.FinalFormId,
            spec_id = spec.id, batch_no = "TEST" + s.ItemId, state = "final", storage_type = "chilled", quantity = 100,
            amount = 1, expire_date = DateTime.Today.AddDays(10), received_at = DateTime.UtcNow }); await db.SaveChangesAsync();
        Assert.Equal(1, (await Route(s, r => r.SaveSpec(s.Mini, new(s.ShopId, s.ItemId, spec.id, s.FinalFormId, "改名", Barcode: barcode, Valid: false)))).code);
        Assert.Equal(0, (await Route(s, r => r.SaveSpec(s.WeCom, new(s.ShopId, s.ItemId, spec.id, s.FinalFormId, "牛肉", Barcode: barcode, Valid: false)))).code);
        await using var fresh = Open(); Assert.False((await fresh.fnbPurchaseSpec.SingleAsync(x => x.id == spec.id)).valid);
        Assert.Equal(1, (await Route(s, r => r.FindSpecByBarcode(s.Mini, s.ShopId, barcode))).code);
    }
    [FnbSqlServerFact]
    public async Task DatabaseEnforcesEffectiveExpiryAndRejectsNegativeStockAndCrossItemForm()
    {
        var s = await SeedAsync(); await using var db = Open();
        var batch = new FnbBatch { shop_id = s.ShopId, item_id = s.ItemId, form_id = s.FinalFormId, batch_no = "EXP" + s.ItemId,
            state = "final", storage_type = "chilled", quantity = 100, amount = 1, expire_date = new DateTime(2026, 10, 20),
            op_date = new DateTime(2026, 10, 6), op_expire_date = new DateTime(2026, 10, 8), received_at = DateTime.UtcNow };
        db.fnbBatch.Add(batch); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(new DateTime(2026, 10, 8), (await db.fnbBatch.SingleAsync(x => x.id == batch.id)).effective_expire);
        batch.quantity = -1; db.Entry(batch).State = EntityState.Modified;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var otherResult = await Route(s, r => r.CreateItem(s.Mini, new(s.ShopId, s.CategoryId, "跨食材检查", "出品态", "g")));
        Assert.Equal(0, otherResult.code); var other = Assert.IsType<FnbItem>(otherResult.data);
        int otherFormId = (await db.fnbItemForm.SingleAsync(x => x.item_id == other.id)).id;
        batch.id = 0; batch.quantity = 1; batch.form_id = otherFormId; batch.batch_no += "X";
        db.fnbBatch.Add(batch); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
