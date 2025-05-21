using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    public class LogController
    {
        private readonly ApplicationDBContext _db;
        public LogController(ApplicationDBContext context)
        {
            _db = context;
        }
        public async Task<List<CoreDataModLog>> GetSimpleLogs(string tableName, int key)
        {
            return await _db.coreDataModLog.Include(l => l.staff)
                .Where(l => l.table_name.Trim().Equals(tableName.Trim()) && l.key_value == key)
                .OrderByDescending(l => l.trace_id).AsNoTracking().ToListAsync();
        }
    }
}