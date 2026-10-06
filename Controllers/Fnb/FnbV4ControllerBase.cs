using System;
using System.Data;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Controllers.Fnb;

public abstract class FnbV4ControllerBase(ApplicationDBContext db) : ControllerBase
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    protected ApplicationDBContext Db => db;
    protected static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };
    public static bool HasSqlError(Exception? error, int number)
    {
        for (var ex = error; ex != null; ex = ex.InnerException)
            if (ex is SqlException sql) foreach (SqlError item in sql.Errors) if (item.Number == number) return true;
        return false;
    }
    protected async Task<ApiResult<object>> Execute(string? key, int shopId, bool write, Func<Task<object>> action)
        => await Run(key, shopId, write, write, _ => action());

    protected Task<ApiResult<object>> StockWrite(string? key, int shopId, bool manager, Guid requestId, string actionName, object payload, Func<FnbAccess.Actor, Task<object>> action)
        => Run(key, shopId, manager, true, async actor =>
        {
            if (requestId == Guid.Empty) throw new ArgumentException("requestId 必须是非空 UUID");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, WireJson))));
            var prior = await db.fnbRequest.SingleOrDefaultAsync(x => x.shop_id == shopId && x.action == actionName && x.request_id == requestId);
            if (prior != null)
            {
                if (prior.payload_hash != hash) throw new FnbConflictException("同一 requestId 不能更换请求内容");
                return JsonSerializer.Deserialize<JsonElement>(prior.response_json);
            }
            var data = await action(actor);
            db.fnbRequest.Add(new FnbRequest { shop_id = shopId, action = actionName, request_id = requestId, payload_hash = hash,
                response_json = JsonSerializer.Serialize(data, WireJson), staff_id = actor.Staff.id, created_at = DateTime.UtcNow });
            await db.SaveChangesAsync(); return data;
        });

    protected async Task<ApiResult<object>> Run(string? key, int shopId, bool manager, bool transaction, Func<FnbAccess.Actor, Task<object>> action)
    {
        try
        {
            var actor = await new FnbAccess(db).ResolveActorAsync(key);
            if (actor == null) return Result(2, "会话失效或员工已离职");
            if (shopId <= 0 || !FnbAccess.CanAccess(actor.Staff, shopId, manager)) return Result(3, manager ? "需要本店店长权限" : "无本店权限");
            if (!await db.shop.AsNoTracking().AnyAsync(x => x.id == shopId)) return Result(3, "门店不存在");
            if (!transaction) return Result(0, "", await action(actor));
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            object data = await action(actor);
            await tx.CommitAsync(); return Result(0, "", data);
        }
        catch (Exception ex) when (HasSqlError(ex, 1205) || HasSqlError(ex, 2601) || HasSqlError(ex, 2627) || ex is DbUpdateConcurrencyException || ex is FnbConflictException)
        { db.ChangeTracker.Clear(); return Result(4, "数据发生冲突，请刷新后重试"); }
        catch (ArgumentException ex) { db.ChangeTracker.Clear(); return Result(1, ex.Message); }
    }
}
