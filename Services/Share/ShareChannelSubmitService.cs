using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Services.Share;

/// <summary>管理员手动提交支付渠道；先原子认领付款分账，再调用渠道，避免并发重复提交。</summary>
public sealed class ShareChannelSubmitService(ApplicationDBContext db, Func<PaymentShare, Task<PaymentShare>> dispatch)
{
    public async Task SubmitManualAsync(Staff op, int orderShareId)
    {
        if (!StaffAccountRules.IsAdmin(op)) throw new ShareException("只有管理员可以手动分账", 3);
        List<int> ids;
        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            var share = await db.orderShare.AsNoTracking().FirstOrDefaultAsync(s => s.id == orderShareId && s.valid)
                ?? throw new ShareException("分账申请不存在");
            if (share.share_method != ShareWorkflowRules.Manual || !await ShareQueries.IsIntroducerRelationAsync(db, share.relation_id))
                throw new ShareException("这条申请不是手动渠道分账");
            var records = await db.paymentShare.AsNoTracking().Where(p => p.valid && p.share_id == share.id).ToListAsync();
            await ShareChannelValidation.CheckAsync(db, share, records);
            ids = records.Select(p => p.id).ToList();
            DateTime now = DateTime.Now;
            int claimed = await db.paymentShare.Where(p => ids.Contains(p.id) && p.valid && p.submit_time == null)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.submit_time, now).SetProperty(p => p.update_date, now));
            if (claimed != ids.Count) throw new ShareException("分账状态已变化，请刷新后核对");
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order_share", "dealed", share.id, "手动提交渠道分账", null, op.id,
                null, "提交中", "支付分账记录 " + string.Join(",", ids)));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        db.ChangeTracker.Clear();
        try
        {
            foreach (int id in ids)
            {
                var row = await db.paymentShare.AsNoTracking().Include(p => p.payment)
                    .Include(p => p.orderShare).ThenInclude(s => s.relation)
                    .FirstAsync(p => p.id == id);
                await dispatch(row);
                db.ChangeTracker.Clear();
            }
        }
        catch (Exception)
        {
            db.ChangeTracker.Clear();
            throw new ShareException("提交结果待核对，请刷新列表查看状态，勿重复操作");
        }
    }
}
