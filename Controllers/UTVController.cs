using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.UTV;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class UTVController : ControllerBase
    {
        private readonly ApplicationDBContext _db;

        public UTVController(ApplicationDBContext context)
        {
            _db = context;
        }

        [HttpGet]
        public ActionResult<double> GetUnitDeposit()
        {
            return Ok((double)3000);
        }

        [HttpGet]
        public ActionResult<double> GetUnitLongCharge()
        {
            return Ok((double)1280);
        }

        [HttpGet]
        public ActionResult<double> GetUnitShortCharge()
        {
            return Ok((double)680);
        }

        [HttpGet("{reserveAble}")]
        public async Task<ActionResult<IEnumerable<UTVTrip>>> GetTrips(int reserveAble, DateTime date)
        {
            if (reserveAble == 1)
            {
                return Ok(await _db.utvTrip.Where(t => t.reserve_able == 1 && t.trip_date.Date == date.Date).ToListAsync());
            }
            else
            {
                return Ok(await _db.utvTrip.Where(t => t.trip_date.Date == date.Date).ToListAsync());
            }
        }

        [HttpGet("{sessionKey}")]
        public async Task<ActionResult<UTVUsers>> GetUserBySessionKey(string sessionKey)
        {
            string openId = await GetOpenId(sessionKey);
            if (openId == null)
            {
                return NotFound();
            }
            UTVUsers user = await GetUser(openId);
            if (user == null)
            {
                var userList = await _db.MiniAppUsers.Where(u => u.open_id.Trim().Equals(openId.Trim())).ToListAsync();
                if (userList == null || userList.Count == 0)
                {
                    return NotFound();
                }
                MiniAppUser miniAppUser = userList[0];
                UTVUsers utvUser = new UTVUsers()
                {
                    id = 0,
                    wechat_open_id = openId.Trim(),
                    user_id = miniAppUser.member_id,
                    tiktok_open_id = "",
                    real_name = miniAppUser.real_name,
                    cell = miniAppUser.cell_number.Trim(),
                    driver_license = ""
                };
                await _db.utvUser.AddAsync(utvUser);
                await _db.SaveChangesAsync();
                return Ok(utvUser);
            }
            return Ok(user);
        }

        [NonAction]
        public async Task<string> GetOpenId(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            var sessionList = await _db.MiniSessons.Where(m => m.session_key.Trim().Equals(sessionKey)).OrderByDescending(s => s.create_date).ToListAsync();
            if (sessionList == null || sessionList.Count == 0)
            {
                return null;
            }
            return sessionList[0].open_id.Trim();
        }
        [NonAction]
        public async Task<UTVUsers> GetUser(string openId)
        {
            var userList = await _db.utvUser.Where(u => (u.tiktok_open_id.Trim().Equals(openId.Trim()) || u.wechat_open_id.Trim().Equals(openId))).ToListAsync();
            if (userList == null || userList.Count == 0)
            {
                return null;
            }
            else
            {
                return userList[0];
            }

        }
        [HttpGet("{tripId}")]
        public async Task<ActionResult<int>> GetAvailableVehicleNum(int tripId)
        {
            int totalNum = 0;
            var trip = await _db.utvTrip.FindAsync(tripId);
            var vehicleList = await _db.vehicle.ToListAsync();
            for (int i = 0; i < vehicleList.Count; i++)
            {
                var vehicle = vehicleList[i];
                if (vehicle.valid == 1)
                {
                    totalNum++;
                }
                else if (vehicle.update_date.AddDays(5).Date < trip.trip_date.Date)
                {
                    totalNum++;
                }
            }   
            return Ok(totalNum);
        }

        [HttpGet("{tripId}")]
        public async Task<ActionResult<IEnumerable<UTVVehicleSchedule>>> GetSchedulesForTrip(int tripId, string sessionKey)
        {
           bool isAdmin = await IsAdmin(sessionKey);
            var sList = await _db.utvVehicleSchedule
                 .Where(s => s.trip_id == tripId).ToListAsync();
            for(int i = 0; i < sList.Count; i++) 
            {
                if (!isAdmin)
                {
                    sList[i].driver_user_id = 0;
                    sList[i].driver_insurance = "";
                    sList[i].passenger_user_id = 0;
                    sList[i].passenger_insurance = "";
                }
            }
            return Ok(sList);
        }

        [HttpGet("{tripId}")]
        public async Task<ActionResult<int>> GetLockedNumForTrip(int tripId, string sessionKey)
        {
            IEnumerable<UTVVehicleSchedule> sList 
                = (IEnumerable<UTVVehicleSchedule>)Util.GetValueFromResult((await GetSchedulesForTrip(tripId, sessionKey)).Result);
            if (sList == null)
            {
                return 0;
            }
            
            int num = 0;
            for (int i = 0; i < sList.Count(); i++)
            {
                UTVVehicleSchedule s = sList.ElementAt(i);
                if (s.status.Trim().Equals("锁定") || s.status.Trim().Equals("候补"))
                {
                    num++;
                }
            }
            return num;
        }




        [HttpGet("{tripId}")]
        public async Task<ActionResult<int>> Reserve(int tripId, string lineType, int vehicleNum, string cell, string name, string source, string sessionKey)
        {
            UTVTrip trip = await _db.utvTrip.FindAsync(tripId);

            if (trip == null || trip.reserve_able == 0 || trip.trip_date.Date < DateTime.Now.Date)
            {
                return NotFound();
            }

            sessionKey = Util.UrlDecode(sessionKey.Trim());
            lineType = Util.UrlDecode(lineType);
            name = Util.UrlDecode(name);
            UTVUsers user = (UTVUsers)Util.GetValueFromResult((await GetUserBySessionKey(sessionKey)).Result);
            if (user == null || user.id == 0)
            {
                return NotFound();
            }

            bool userInfoModified = false;

            if (user.cell.Trim().Equals(""))
            {
                user.cell = cell.Trim();
                userInfoModified = true;
            }

            if (user.real_name.Trim().Equals(""))
            {
                user.real_name = name.Trim();
                userInfoModified = true;
            }

            if (userInfoModified)
            {
                await _db.SaveChangesAsync();
            }

            if (!user.wechat_open_id.Trim().Equals(""))
            {
                userInfoModified = false;
                MiniAppUser mUser = await _db.MiniAppUsers.FindAsync(user.wechat_open_id.Trim());
                if (mUser != null)
                {
                    if (mUser.cell_number.Trim().Equals(""))
                    {
                        mUser.cell_number = cell.Trim();
                        userInfoModified = true;
                    }

                    if (mUser.real_name.Trim().Equals(""))
                    {
                        mUser.real_name = name.Trim();
                        userInfoModified = true;
                    }
                    if (userInfoModified)
                    {
                        _db.Entry(mUser).State = EntityState.Modified;
                        await _db.SaveChangesAsync();
                    }
                }
            }


            UTVReserve r = new UTVReserve()
            {
                utv_user_id = user.id,
                trip_id = tripId,
                vehicle_num = vehicleNum,
                line_type = lineType.Trim(),
                cell = cell.Trim(),
                real_name = name.Trim(),
                status = "待确认",
                source = source.Trim()
            };
            await _db.AddAsync(r);
            await _db.SaveChangesAsync();
            return Ok(r.id);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UTVReserve>>> GetReserveList(string sessionKey, DateTime startDate, DateTime endDate, string status = "")
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            status = Util.UrlDecode(status.Trim()).Trim();
            if (!(await IsAdmin(sessionKey)))
            {
                return BadRequest();
            }
            var rList = await _db.utvReserve.Join(_db.utvTrip, r => r.trip_id, t => t.id,
                (r, t) => new { r.id, r.trip_id, r.cell, r.real_name, r.line_type, r.vehicle_num, r.status, t.trip_name, t.trip_date })
                .Where(r => ((status.Equals("") || r.status.Trim().Equals(status)) && r.trip_date.Date >= startDate.Date && r.trip_date.Date <= endDate.Date))
                .ToListAsync();
            if (rList == null)
            { 
                return NotFound();
            }
            
            return Ok(rList);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UTVReserve>> GetReserve(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            bool isAdmin = await IsAdmin(sessionKey);
            UTVUsers user = await GetUTVUser(sessionKey);
            UTVReserve reserve = await _db.utvReserve.FindAsync(id);
            if (reserve == null) 
            {
                return NotFound();
            }
            if (!isAdmin && reserve.utv_user_id != user.id)
            {
                return NoContent();
            }
            UTVTrip trip = await _db.utvTrip.FindAsync(reserve.trip_id);
            reserve.trip_name = trip.trip_name;
            reserve.trip_date = trip.trip_date;
            return Ok(reserve);
        }

        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<UTVReserve>> UpdateReserve(string sessionKey, UTVReserve reserve)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
           
            if (!(await IsAdmin(sessionKey)))
            {
                return BadRequest();
            }

            _db.Entry(reserve).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(reserve);
        }

        [HttpGet("{reserveId}")]
        public async Task<ActionResult<IEnumerable<UTVVehicleSchedule>>> ApplyReserve(int reserveId, string sessionKey)
        {
            UTVReserve reserve = await _db.utvReserve.FindAsync(reserveId);
            if (reserve == null || !reserve.status.Trim().Equals("待确认")) 
            {
                return NotFound();
            }
            reserve.status = "待付押金";
            reserve = (UTVReserve)Util.GetValueFromResult((await UpdateReserve(sessionKey, reserve)).Result);
            if (reserve == null)
            {
                return BadRequest();
            }
            double deposit = (double)Util.GetValueFromResult(GetUnitDeposit().Result);
            double charge = 0;
            switch (reserve.line_type.Trim())
            {
                case "长线":
                    charge = (double)Util.GetValueFromResult(GetUnitLongCharge().Result);
                    break;
                case "短线":
                    charge = (double)Util.GetValueFromResult(GetUnitShortCharge().Result);
                    break;
                default:
                    break;
            }
            for (int i = 0; i < reserve.vehicle_num; i++)
            {
               
                UTVVehicleSchedule s = new UTVVehicleSchedule()
                {
                    trip_id = reserve.trip_id,
                    reserve_id = reserve.id,
                    car_no = "",
                    status = "",
                    start_mile = "",
                    end_mile = "",
                    line_type = reserve.line_type.Trim(),
                    charge = charge,
                    charge_discount = 0,
                    deposit = deposit,
                    deposit_discount = 0,
                    ticket_code = "",
                    ticket_discount = 0,
                    driver_user_id = 0,
                    driver_insurance = "",
                    passenger_user_id = 0,
                    passenger_insurance = "",
                    memo = ""
                };
                await _db.utvVehicleSchedule.AddAsync(s);
            }
            await _db.SaveChangesAsync();
            return await _db.utvVehicleSchedule.Where(s => s.reserve_id == reserve.id).ToListAsync();
        }

       

        /*
        [NonAction]
        public async Task<IEnumerable<UTVVehicleSchedule>> AllocateVehicleForReserve(int reserveId)
        {
            UTVReserve reserve = await _db.utvReserve.FindAsync(reserveId);
            if (reserve == null || !reserve.status.Trim().Equals("待付押金"))
            {
                return null;
            }

            var vList = await _db.utvVehicleSchedule.Where(s => (s.reserve_id == reserveId && s.status.Trim().Equals("待支付"))).ToListAsync();
            if (vList == null || vList.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < vList.Count; i++)
            {
                UTVVehicleSchedule s = vList[i];
                s.memo = "重新支付取消原有的。";
                _db.Entry(s).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();

            for (int i = 0; i < reserve.vehicle_num; i++)
            {
                UTVVehicleSchedule s = new UTVVehicleSchedule()
                {
                    trip_id = reserve.trip_id,
                    reserve_id = reserve.id,
                    car_no = "",
                    status = "",
                    start_mile = "",
                    end_mile = "",
                    line_type = reserve.line_type.Trim(),
                    charge = 0,
                    deposit = 0,
                    discount = 0,
                    ticket_code = "",
                    ticket_discount = 0,
                    driver_user_id = 0,
                    driver_insurance = "",
                    passenger_user_id = 0,
                    passenger_insurance = "",
                    memo = ""
                };
                await _db.utvVehicleSchedule.AddAsync(s);
            }
            await _db.SaveChangesAsync();
            return await _db.utvVehicleSchedule.Where(s => s.reserve_id == reserve.id).ToListAsync();
        }
        */
        [NonAction]
        public async Task<bool> IsAdmin(string sessionKey)
        {
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
            return user.isAdmin;
        }

        [NonAction]
        public async Task<UTVUsers> GetUTVUser(string sessionKey)
        {
            var sessionList = await _db.MiniSessons
                .Where(s => s.session_key.Trim().Equals(sessionKey))
                .OrderByDescending(s => s.create_date).ToListAsync();
            if (sessionList == null || sessionList.Count == 0)
            {
                return null;
            }
            string source = sessionList[0].session_type.Trim();
            string openId = sessionList[0].open_id.Trim();
            var userList = await _db.utvUser.Where(u => ((source.Equals("wechat") && u.wechat_open_id.Trim().Equals(openId))
                || (source.Equals("tiktok") && u.tiktok_open_id.Trim().Equals(openId)))).ToListAsync();
            if (userList == null || userList.Count == 0)
            {
                return null;
            }
            return userList[0];
        }
            
    }
}
