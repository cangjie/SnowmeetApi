using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;
using Microsoft.Extensions.Configuration;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class PointController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public PointController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        protected List<Point> GetUserPointBalance(string openId, string openIdType)
        {
            
            UnicUser user = UnicUser.GetUnicUser(openId, openIdType);
            return _context.Point.Where(p => (p.user_open_id.Trim().Equals(user.miniAppOpenId)
            || p.user_open_id.Trim().Equals(user.officialAccountOpenId) || p.user_open_id.Trim().Equals(user.officialAccountOpenIdOld)))
                .OrderByDescending(p => p.id).ToList();
        }

        [HttpGet]
        public  ActionResult<int> GetUserPointsSummary(string openId, string openIdType)
        {
            List<Point> pointList = GetUserPointBalance(openId, openIdType);
            int sum = 0;

            for (int i = 0; i < pointList.Count; i++)
            {
                sum = sum + pointList[i].points;
            }
            return sum;
        }

        [HttpGet]
        public ActionResult<int> GetUserPointsTotalEarned(string openId, string openIdType)
        {
            List<Point> pointList = GetUserPointBalance(openId, openIdType);
            int sum = 0;

            for (int i = 0; i < pointList.Count; i++)
            {
                if (pointList[i].points > 0)
                {
                    sum = sum + pointList[i].points;
                }
            }
            return sum;
        }

        [HttpGet]
        public async Task<ActionResult<int>> GetMyPointsSummary(string sessionKey)
        {
            
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            return GetUserPointsSummary(user.miniAppOpenId, "snowmeet_mini");
        }

        [HttpGet]
        public async Task<ActionResult<Point>> SetPoint(int points, string sessionKey, string memo)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            memo = Util.UrlDecode(memo);
            
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            Point point = new Point()
            {
                points = points,
                user_open_id = user.miniAppOpenId.Trim(),
                memo = memo,
                transact_date = DateTime.Now
            };
            _context.Point.Add(point);
            await _context.SaveChangesAsync();
            //return CreatedAtAction("SetPoint", new { id = point.id }, point);
            return point;
        }


        [HttpGet]
        public async Task<ActionResult<List<Point>>> GetMyPointsBalance(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            List<Point> pointsList = GetUserPointBalance(user.miniAppOpenId, "snowmeet_mini");
            for (int i = 0; i < pointsList.Count; i++)
            {
                pointsList[i].user_open_id = "";
            }
            return pointsList;
        }



        /*
        // GET: api/Point
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Point>>> GetPoint()
        {
            return await _context.Point.ToListAsync();
        }

        // GET: api/Point/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Point>> GetPoint(int id)
        {
            var point = await _context.Point.FindAsync(id);

            if (point == null)
            {
                return NotFound();
            }

            return point;
        }

        // PUT: api/Point/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutPoint(int id, Point point)
        {
            if (id != point.id)
            {
                return BadRequest();
            }

            _context.Entry(point).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PointExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/Point
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Point>> PostPoint(Point point)
        {
            _context.Point.Add(point);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetPoint", new { id = point.id }, point);
        }

        // DELETE: api/Point/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePoint(int id)
        {
            var point = await _context.Point.FindAsync(id);
            if (point == null)
            {
                return NotFound();
            }

            _context.Point.Remove(point);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        */


        private bool PointExists(int id)
        {
            return _context.Point.Any(e => e.id == id);
        }
    }
}
