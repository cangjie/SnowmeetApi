using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Rent;

// 防止自动保存晚于确认追加或取消追加，把已确认状态写回草稿。
public static class RentAppendOrderLock
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    public static async Task<T> Run<T>(ApplicationDBContext db, int orderId, Func<Task<T>> action)
    {
        var gate = Gates[(int)((uint)orderId % (uint)Gates.Length)];
        await gate.WaitAsync();
        bool opened = false, locked = false;
        string resource = "snowmeet:rent-append:" + orderId;
        try
        {
            await db.Database.OpenConnectionAsync(); opened = true;
            if (db.Database.IsSqlServer())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @result int;
EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=15000;
IF @result < 0 THROW 51010, N'追加操作繁忙，请稍后重试', 1;");
                locked = true;
            }
            return await action();
        }
        finally
        {
            try { if (locked) await db.Database.ExecuteSqlInterpolatedAsync($@"EXEC sys.sp_releaseapplock @Resource={resource}, @LockOwner='Session';"); }
            finally { if (opened) await db.Database.CloseConnectionAsync(); gate.Release(); }
        }
    }
}
