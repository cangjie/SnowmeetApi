using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.CoachPlatform;

internal static class DistributorAccess
{
    // 老版仅有 coach 时维持兼容；已有分销商资料时，必须有相匹配的启用身份。
    internal static async Task<bool> CanIssueAsync(ApplicationDBContext db, Coach coach)
    {
        var records = await db.introducer.AsNoTracking().Where(i => i.member_id == coach.member_id).ToListAsync();
        if (records.Count == 0) return true;
        var active = records.Where(i => i.valid == 1).ToList();
        return active.Count == 1 && (active[0].coach_id == null || active[0].coach_id == coach.id);
    }
}
