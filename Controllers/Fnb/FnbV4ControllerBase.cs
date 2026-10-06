using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Controllers.Fnb;

public abstract class FnbV4ControllerBase(ApplicationDBContext db) : ControllerBase
{
    protected ApplicationDBContext Db => db;
    protected static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };
    public static bool HasSqlError(Exception? error, int number)
    {
        for (var ex = error; ex != null; ex = ex.InnerException)
            if (ex is SqlException sql) foreach (SqlError item in sql.Errors) if (item.Number == number) return true;
        return false;
    }
    protected async Task<ApiResult<object>> Execute(string? key, int shopId, bool write, Func<Task<object>> action)
    {
        try
        {
            var actor = await new FnbAccess(db).ResolveActorAsync(key);
            if (actor == null) return Result(2, "会话失效或员工已离职");
            if (shopId <= 0 || !FnbAccess.CanAccess(actor.Staff, shopId, write)) return Result(3, write ? "需要本店店长权限" : "无本店权限");
            if (!await db.shop.AsNoTracking().AnyAsync(x => x.id == shopId)) return Result(3, "门店不存在");
            if (!write) return Result(0, "", await action());
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            object data = await action();
            await tx.CommitAsync(); return Result(0, "", data);
        }
        catch (Exception ex) when (HasSqlError(ex, 1205) || HasSqlError(ex, 2601) || HasSqlError(ex, 2627) || ex is DbUpdateConcurrencyException)
        { db.ChangeTracker.Clear(); return Result(4, "数据发生冲突，请刷新后重试"); }
        catch (ArgumentException ex) { db.ChangeTracker.Clear(); return Result(1, ex.Message); }
    }
}
