using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.UTV;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class UTVController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly Order.OrderPaymentController payCtrl;

        public UTVController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            payCtrl = new Order.OrderPaymentController(context, config, httpContextAccessor);
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

        [HttpGet("{date}")]
        public async Task<ActionResult<IEnumerable<UTVTrip>>> GetTripsDetail(DateTime date, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (!(await IsAdmin(sessionKey)))
            {
                return BadRequest();
            }
            var tripList = await _db.utvTrip.Where(t => t.trip_date.Date == date.Date).ToListAsync();
            for (int i = 0; i < tripList.Count; i++)
            {
                UTVTrip trip = tripList[i];
                trip.vehicleSchedule = await _db.utvVehicleSchedule
                    .Where(s => (!s.status.Trim().Equals("取消") && s.trip_id == trip.id)).ToListAsync();

            }
            return Ok(tripList);
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
        public async Task<ActionResult<UTVTrip>> GetTrip(int tripId)
        {
            return await _db.utvTrip.FindAsync(tripId);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Vehicle>>> GetAvailiableVehicles()
        {
            return await _db.vehicle.Where(v => v.valid == 1).ToListAsync();
        }

        [HttpGet("{tripId}")]
        public async Task<ActionResult<IEnumerable<UTVVehicleSchedule>>> GetSchedulesForTrip(int tripId, string sort, string sessionKey)
        {
           bool isAdmin = await IsAdmin(sessionKey);
            sort = Util.UrlDecode(sort);
           var sList = await _db.utvVehicleSchedule
                 .Where(s => s.trip_id == tripId).OrderBy(s => s.reserve_id) .ToListAsync();
            if (sort.Trim().Equals("car_no"))
            {
                sList = await _db.utvVehicleSchedule
                 .Where(s => s.trip_id == tripId).OrderBy(s => s.car_no).ToListAsync();
            }

            for(int i = 0; i < sList.Count; i++) 
            {
                if (!isAdmin)
                {
                    sList[i].driver_user_id = 0;
                    sList[i].driver_insurance = "";
                    sList[i].passenger_user_id = 0;
                    sList[i].passenger_insurance = "";
                }
                if (sList[i].driver_user_id > 0)
                {
                    sList[i].driver = await _db.utvUser.FindAsync(sList[i].driver_user_id);
                    if (sList[i].driver == null)
                    {
                        sList[i].haveDriverLicense = false;
                    }
                    else
                    {
                        if (sList[i].driver.driver_license.Trim().Equals(""))
                        {
                            sList[i].haveDriverLicense = false;
                        }
                        else
                        {
                            sList[i].haveDriverLicense = true;
                        }
                    }
                    if (sList[i].driver_insurance.Trim().Equals(""))
                    {
                        sList[i].haveDriverInsurance = false;
                    }
                    else
                    {
                        sList[i].haveDriverInsurance = true;
                    }
                }
                else
                {
                    sList[i].haveDriverInsurance = true;
                    sList[i].haveDriverLicense = true;
                }
                if (sList[i].passenger_user_id > 0)
                {
                    sList[i].passenger = await _db.utvUser.FindAsync(sList[i].passenger_user_id);
                    if (sList[i].passenger.driver_license.Trim().Equals(""))
                    {
                        sList[i].havePassengerLicense = false;
                    }
                    else
                    {
                        sList[i].havePassengerLicense = true;
                    }
                    if (sList[i].passenger_insurance.Trim().Equals(""))
                    {
                        sList[i].havePassengerInsurance = false;
                    }
                    else
                    {
                        sList[i].havePassengerInsurance = true;
                    }
                }
                else
                {
                    sList[i].havePassengerInsurance = true;
                    sList[i].havePassengerLicense = true;
                }

                if (sList[i].haveDriverLicense && sList[i].haveDriverInsurance
                    && sList[i].havePassengerInsurance && sList[i].driver != null
                    && !sList[i].driver.real_name.Trim().Equals("") && !sList[i].driver.cell.Trim().Equals("")
                    && (sList[i].passenger == null || (!sList[i].passenger.real_name.Trim().Equals("") && !sList[i].passenger.cell.Trim().Equals("")))
                )
                {
                    sList[i].canGo = true;
                }
            }

            return Ok(sList);
        }

        [HttpGet("{reserveId}")]
        public async Task<ActionResult<IEnumerable<UTVVehicleSchedule>>> GetScheduleForReserve(int reserveId, string sessionKey, int getAll = 0)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            bool isAdmin = await IsAdmin(sessionKey);
            if (!isAdmin)
            {
                UTVUsers user = await GetUTVUser(sessionKey);
                UTVReserve reserve = (UTVReserve)Util.GetValueFromResult((await GetReserve(reserveId, sessionKey)).Result);
                if (reserve.utv_user_id == user.id)
                {
                    isAdmin = true;
                }
            }
            


            var sList = await _db.utvVehicleSchedule
                 .Where(s => (s.reserve_id == reserveId && (getAll == 1 || (getAll == 0 && !s.status.Trim().Equals("取消"))) )).ToListAsync();
            for (int i = 0; i < sList.Count; i++)
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
                = (IEnumerable<UTVVehicleSchedule>)Util.GetValueFromResult((await GetSchedulesForTrip(tripId, "", sessionKey)).Result);
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
        public async Task<ActionResult<IEnumerable<UTVReserve>>> GetReserveList(string sessionKey, DateTime startDate, DateTime endDate, string status = "", int onlyMine = 0)
        {
            sessionKey = Util.UrlDecode(sessionKey.Trim());
            status = Util.UrlDecode(status.Trim()).Trim();
            bool isAdmin = await IsAdmin(sessionKey);
            string openId = await GetOpenId(sessionKey);
            if (openId == null || openId.Trim().Equals(""))
            {
                return NoContent();
            }
            UTVUsers user = (await GetUser(openId));
            int userId = 0;
            if (user == null)
            {
                return NoContent();
            }
            userId = user.id;
            if (userId == 0)
            {
                return NoContent();
            }

            if (!isAdmin)
            {
                onlyMine = 1;
            }
            
            var rList = await _db.utvReserve.Join(_db.utvTrip, r => r.trip_id, t => t.id,
                (r, t) => new { r.id, r.trip_id, r.cell, r.real_name, r.line_type, r.vehicle_num, r.status, t.trip_name, t.trip_date, r.utv_user_id })
                .Where(r => ((status.Equals("") || r.status.Trim().Equals(status))
                    && r.trip_date.Date >= startDate.Date
                    && r.trip_date.Date <= endDate.Date
                    && (onlyMine == 0 || r.utv_user_id == userId) ))
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
            string openId = await GetOpenId(sessionKey);
            if (openId == null || openId.Trim().Equals(""))
            {
                return NoContent();
            }
            UTVUsers user = await GetUTVUser(sessionKey);
            UTVReserve reserve = await _db.utvReserve.FindAsync(id);
            if (reserve == null) 
            {
                return NotFound();
            }
            if (!isAdmin &&  reserve.utv_user_id != user.id)
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
            bool needChangeScheduleTripId = false;
            var oriReserveList = await _db.utvReserve.Where(r => r.id == reserve.id).AsNoTracking().ToListAsync();
            if (oriReserveList == null || oriReserveList.Count == 0)
            {
                return NotFound();
            }
            if (oriReserveList[0].trip_id != reserve.trip_id)
            {
                needChangeScheduleTripId = true;
            }
            _db.Entry(reserve).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            if (needChangeScheduleTripId)
            {
                var scheduleList = await _db.utvVehicleSchedule.Where(s => s.reserve_id == reserve.id).ToListAsync();
                for (int i = 0; i < scheduleList.Count; i++)
                {
                    scheduleList[i].trip_id = reserve.trip_id;
                    _db.Entry(scheduleList[i]).State = EntityState.Modified;
                }
                await _db.SaveChangesAsync();
            }

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
                    status = "待支付",
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

        [HttpGet("{id}")]
        public async Task<ActionResult<UTVVehicleSchedule>> GetSchedule(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            bool isAdmin = await IsAdmin(sessionKey);
            
            UTVVehicleSchedule s = await _db.utvVehicleSchedule.FindAsync(id);

            if (!isAdmin)
            {
                UTVReserve reserve = await _db.utvReserve.FindAsync(s.reserve_id);
                UTVUsers user = await GetUTVUser(sessionKey);
                if (reserve.utv_user_id == user.id)
                {
                    isAdmin = true;
                }
            }

            if (!isAdmin)
            {
                s.driver_user_id = 0;
                s.driver_insurance = "";
                s.passenger_user_id = 0;
                s.passenger_insurance = "";
            }
            return Ok(s);
        }

        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<UTVVehicleSchedule>> UpdateSchedule(string sessionKey, UTVVehicleSchedule schedule)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            bool isAdmin = await IsAdmin(sessionKey);
            if (!isAdmin)
            {
                string openId = await GetOpenId(sessionKey);
                UTVUsers user = await GetUser(openId);
                UTVReserve reserve = await _db.utvReserve.FindAsync(schedule.reserve_id);
                if (reserve != null && reserve.utv_user_id == user.id)
                {
                    isAdmin = true;
                }
                
                
            }
            if (!isAdmin)
            {
                return BadRequest();
            }
            _db.Entry(schedule).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(schedule);
        }

        [HttpGet("{reserveId}")]
        public async Task<ActionResult<Models.Order.TenpaySet>> PayDepositByTencent(int reserveId, string sessionKey)
        {
            string openId = await GetOpenId(sessionKey);
            UTVUsers user = await GetUser(openId.Trim());
            UTVReserve reserve = await _db.utvReserve.FindAsync(reserveId);
            if (reserve.status != "待付押金" || reserve.utv_user_id != user.id)
            {
                return BadRequest();
            }


            var scheduleList = await _db.utvVehicleSchedule.Where(s => (s.status == "待支付" && s.reserve_id == reserveId)).ToListAsync();
            double depositTotal = 0;
            for (int i = 0; i < scheduleList.Count; i++)
            {
                depositTotal += (scheduleList[i].deposit - scheduleList[i].deposit_discount);
            }

            int orderId = reserve.order_id;
            int paymentId = 0;
            OrderPayment paymentFinal = new OrderPayment();
            bool needCreateNew = false;

            if (orderId == 0)
            {
                needCreateNew = true;
                

            }

            OrderOnline orderOri = await _db.OrderOnlines.FindAsync(orderId);

            if (orderOri == null)
            {
                needCreateNew = true;
            }
            else
            {
                if (orderOri.pay_state != 0)
                {
                    return BadRequest();
                }
            }

            var payList = await _db.OrderPayment.Where(p => (p.order_id == orderId)).ToListAsync();
            if (payList == null || payList.Count == 0)
            {
                needCreateNew = true;
            }
            else
            {
                
                for (int i = 0; i < payList.Count; i++)
                {
                    if (payList[i].status.Trim().Equals("待支付")
                        && Math.Round(payList[i].amount, 2) == Math.Round(depositTotal, 2))
                    {
                        paymentId = payList[i].id;
                        paymentFinal = payList[i];
                        break;
                    }
                }
                if (paymentId == 0)
                {
                    needCreateNew = true;
                }
            }

            if (needCreateNew)
            {
                if (orderId > 0)
                {
                    await payCtrl.CancelOrder(orderId, sessionKey);
                }
                OrderOnline order = new OrderOnline()
                {
                    cell_number = reserve.cell.Trim(),
                    name = reserve.real_name.Trim(),
                    type = "UTV押金",
                    shop = "万龙体验中心",
                    order_price = depositTotal,
                    order_real_pay_price = depositTotal,
                    final_price = depositTotal,
                    open_id = openId.Trim(),
                    staff_open_id = "",
                    memo = "UTV"
                };
                await _db.AddAsync(order);
                await _db.SaveChangesAsync();
                OrderPayment payment = new OrderPayment()
                {
                    order_id = order.id,
                    pay_method = order.pay_method.Trim(),
                    amount = order.final_price,
                    status = "待支付",
                    staff_open_id = ""
                };
                reserve.order_id = order.id;
                await _db.OrderPayment.AddAsync(payment);
                _db.Entry(reserve).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                paymentId = payment.id;
                var set = await payCtrl.TenpayRequest(payment.id, sessionKey);
                return Ok(set.Value);
            }
            else
            {
                TenpaySet set = new TenpaySet()
                {
                    nonce = paymentFinal.nonce,
                    prepay_id = paymentFinal.prepay_id,
                    timeStamp = paymentFinal.timestamp,
                    sign = paymentFinal.sign
                };
                return Ok(set);
            }

            
            
                
        }

        [HttpGet]
        public async Task<bool> SetReservePaySuccess(int reserveId)
        {
            UTVReserve reserve = await _db.utvReserve.FindAsync(reserveId);
            if (reserve == null)
            {
                return false;
            }
            reserve.status = "已付押金";
            _db.Entry(reserve).State = EntityState.Modified;

            int lockNum = 0;

            var vScheduleList = await _db.utvVehicleSchedule
                .Where(s => ((s.status.Equals("锁定") || s.status.Equals("候补"))
                && s.trip_id == reserve.trip_id)).ToListAsync();
            lockNum = vScheduleList.Count;

            int totalNum = (int)Util.GetValueFromResult((await GetAvailableVehicleNum(reserve.trip_id)).Result);

            vScheduleList = await _db.utvVehicleSchedule
                .Where(v => (v.status.Trim().Equals("待支付") && v.reserve_id == reserveId))
                .ToListAsync();
            if (vScheduleList == null)
            {
                return false;
            }

            for (int i = 0; i < vScheduleList.Count; i++)
            {
                string status = "锁定";
                if ((i + 1) > (totalNum - lockNum))
                {
                    status = "候补";
                }
                vScheduleList[i].status = status.Trim();
                _db.Entry(vScheduleList[i]).State = EntityState.Modified;
            }

            await _db.SaveChangesAsync();

            return true;
        }

        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<UTVUsers>> RefreshUTVUser(string sessionKey, UTVUsers user)
        {
            UTVUsers oriUser = await GetUser(sessionKey);
            if (oriUser == null)
            {
                string openId = await GetOpenId(sessionKey);
                if (openId == null || openId.Trim().Equals(""))
                {
                    return BadRequest();
                }
            }
            
            var uList = await _db.utvUser.Where(u => (u.cell.Trim().Equals(user.cell.Trim())
                && u.real_name.Trim().Equals(user.real_name.Trim()) 
                && u.gender.Trim().Equals(user.gender.Trim()))).AsNoTracking().ToListAsync();
            if (uList == null || uList.Count == 0)
            {
                await _db.utvUser.AddAsync(user);
                await _db.SaveChangesAsync();
                if (oriUser != null && oriUser.id > 0 && user.id > 0)
                {
                    UTVUserGroup grp = new UTVUserGroup()
                    {
                        host_id = oriUser.id,
                        guest_id = user.id
                    };
                    await _db.uTVUserGroups.AddAsync(grp);
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                if (user.id == 0 || uList.Count == 1)
                {
                    user.id = uList[0].id;

                }
                _db.Entry(user).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            
            return Ok(user);
        }


        [HttpGet("{cell}")]
        public async Task<ActionResult<UTVUsers>> GetUTVUser(string cell, string name)
        {
            name = Util.UrlDecode(name.Trim()).Trim();
            var uList = await _db.utvUser.Where(u => (u.cell.Trim().Equals(cell) && u.real_name.Trim().Equals(name))).ToListAsync();
            if (uList == null || uList.Count == 0)
            {
                UTVUsers u = new UTVUsers()
                {
                    user_id = 0,
                    wechat_open_id = "",
                    tiktok_open_id = "",
                    real_name = name.Trim(),
                    cell = cell,
                    driver_license = ""
                };
                await _db.utvUser.AddAsync(u);
                await _db.SaveChangesAsync();
                return Ok(u);
            }
            return Ok(uList[0]);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<UTVUsers>> GetUTVUserById(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            string openId = await GetOpenId(sessionKey);
            bool isAdmin = await IsAdmin(sessionKey);

            isAdmin = true;

            if (!isAdmin)
            {
                return NotFound();
            }

            

            return await _db.utvUser.FindAsync(id);

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
            var userList = await _db.utvUser.Where(u => (( (source.Equals("") || source.Equals("wechat")) && u.wechat_open_id.Trim().Equals(openId))
                || (source.Equals("tiktok") && u.tiktok_open_id.Trim().Equals(openId)))).ToListAsync();
            if (userList == null || userList.Count == 0)
            {
                return null;
            }
            return userList[0];
        }

        /*

        [NonAction]
        public async Task<UTVReserve> SetDepositPaySuccess(int reserveId)
        {

        }

        [NonAction]
        public async Task<UTVVehicleSchedule> LockTripSchedule(int scheduleId, string sessionKey)
        {
            string status = "已锁定";
            UTVVehicleSchedule s = await _db.utvVehicleSchedule.FindAsync(scheduleId);
            if (s == null)
            {
                return null;
            }
            int lockNum = (int)Util.GetValueFromResult((await GetLockedNumForTrip(s.trip_id, sessionKey)).Result);
            int totalNum = (int)Util.GetValueFromResult((await GetAvailableVehicleNum(s.trip_id)).Result);
            if (lockNum >= totalNum)
            {
                status = "候补";
            }
            s.status = status;
            _db.Entry(s).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return s;
        }
        */
            
    }
}
