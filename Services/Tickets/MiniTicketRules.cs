using System;
using System.Data;
using System.Data.SqlTypes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.Tickets;

public static class MiniTicketRules
{
    public const string Personal = "mini_p", Group = "mini_g", QrCode = "mini_q";
    public const string ShareMarker = "小程序直接领取分享";
    public const string ShareReplacedMarker = "小程序分享已被后续分享替代";
    public const string ClaimPage = "pages/mine/ticket/ticket_claim_mini/ticket_claim_mini";
    public static bool IsMini(string type) => type == Personal || type == Group || type == QrCode;
    public static string DisplayType(string type) => type switch { Personal => "personal", Group => "group", QrCode => "qrcode", _ => type };
    // Both production columns are SQL datetime. Round once before assigning either value.
    public static DateTime ShareTime() => new SqlDateTime(DateTime.Now).Value;
    public static Task<bool> HasCurrentMarker(ApplicationDBContext db, Ticket ticket) =>
        ticket.shared != 1 || ticket.shared_time == null ? Task.FromResult(false) :
        db.ticketLog.AsNoTracking().AnyAsync(l => l.code == ticket.code && l.memo == ShareMarker
            && l.accepter_open_id == "" && l.transact_time == ticket.shared_time
            && !db.ticketLog.Any(replaced => replaced.code == l.code && replaced.memo == ShareReplacedMarker
                && replaced.accepter_open_id == "" && replaced.transact_time == l.transact_time));
}

public static class MiniTicketTransaction
{
    // SQL Server's transaction-owned application lock protects different workers/instances.
    // The process gate also gives SQLite fixtures the same ordering without pretending to test SQL locks.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<T> Run<T>(ApplicationDBContext db, Func<Task<T>> action)
    {
        await Gate.WaitAsync();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Lock(db, "snowmeet:mini-ticket:issuance");
            T result = await action();
            await tx.CommitAsync();
            return result;
        }
        finally { Gate.Release(); }
    }
    public static async Task Lock(ApplicationDBContext db, string resource)
    {
        if (db.Database.IsSqlServer())
            await db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @result int;
EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
IF @result < 0 THROW 51010, N'操作繁忙，请稍后重试', 1;");
    }
}
