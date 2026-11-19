using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Rent;

/// <summary>Serializes the final refund check and creation of pending refund rows across workers.</summary>
public static class RentalBenefitRefundLock
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const string Resource = "snowmeet:rental-benefit:commit";

    public static async Task<T> Run<T>(ApplicationDBContext db, Func<Task<T>> action)
    {
        await Gate.WaitAsync();
        bool opened = false, locked = false;
        try
        {
            await db.Database.OpenConnectionAsync();
            opened = true;
            if (db.Database.IsSqlServer())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @result int;
EXEC @result = sys.sp_getapplock @Resource={Resource}, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=15000;
IF @result < 0 THROW 51010, N'操作繁忙，请稍后重试', 1;");
                locked = true;
            }
            return await action();
        }
        finally
        {
            try
            {
                if (locked) await db.Database.ExecuteSqlInterpolatedAsync($@"EXEC sys.sp_releaseapplock @Resource={Resource}, @LockOwner='Session';");
            }
            finally
            {
                if (opened) await db.Database.CloseConnectionAsync();
                Gate.Release();
            }
        }
    }
}
