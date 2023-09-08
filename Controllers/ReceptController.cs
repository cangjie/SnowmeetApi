using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models.Rent;
using Org.BouncyCastle.Asn1.X509;
using System.Security.Cryptography;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ReceptController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        private IConfiguration _oriConfig;

        private readonly IHttpContextAccessor _httpContextAccessor;

        public ReceptController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
        }

        [HttpGet]
        public ActionResult<SerialTest> TestSerial()
        {
            string json = "{\"id\": 0, \"name\": \"cangjie\", \"cell\": \"13501177897\", \"joinDate\": \"2023-4-22\"}";
            object s = JsonConvert.DeserializeObject(json, typeof(SerialTest));
            return (SerialTest)s;
        }

        [HttpGet]
        public ActionResult<string> TestDeSerial()
        {
            SerialTest t = new SerialTest()
            {
                id = 1,
                name = "cj",
                cell = "18601197897",
                joinDate = DateTime.Now
            };
            //string json = "{\"id\": 0, \"name\": \"cangjie\", \"cell\": \"13501177897\", \"joinDate\": \"2023-4-22\"}";
            string s = JsonConvert.SerializeObject(t);
            return s;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Recept>> UpdateReceptOpenId(int id, string openId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            MiniAppUser adminUser = await GetUser(sessionKey);
            if (adminUser.is_admin != 1)
            {
                return BadRequest();
            }
            openId = Util.UrlDecode(openId);

            Recept recept = await _context.Recept.FindAsync(id);
            if (recept == null)
            {
                return NotFound();
            }

            recept.open_id = openId;

            _context.Entry(recept).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            int orderId = 0;
            switch (recept.recept_type)
            {
                case "租赁下单":
                    int rentId = recept.submit_return_id;
                    RentOrder rent = await _context.RentOrder.FindAsync(rentId);
                    rent.open_id = openId.Trim();
                    _context.Entry(rent).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    orderId = rent.order_id;
                    break;
                case "养护下单":
                    int maintainId = recept.submit_return_id;
                    MaintainLive mOrder = await _context.MaintainLives.FindAsync(maintainId);
                    mOrder.open_id = openId.Trim();
                    _context.Entry(mOrder).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    orderId = mOrder.order_id;
                    break;
                default:
                    break;
            }
            if (orderId>0)
            {
                OrderOnline order = await _context.OrderOnlines.FindAsync(orderId);
                order.open_id = openId;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            return Ok(recept);

        }




        [HttpGet]
        public async Task<ActionResult<Recept>> NewRecept(string openId, string scene, string shop, string sessionKey, string code = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            MiniAppUser adminUser = await GetUser(sessionKey);
            if (adminUser.is_admin != 1)
            {
                return BadRequest();
            }
            openId = Util.UrlDecode(openId);
            scene = Util.UrlDecode(scene);
            shop = Util.UrlDecode(shop);
            string realName = "";
            string gender = "";
            string cell = "";
            if (!openId.Trim().Equals(""))
            {
                MiniAppUser user = await _context.MiniAppUsers.FindAsync(openId);
                realName = user.real_name.Trim();
                switch (user.gender.Trim())
                {
                    case "男":
                        realName += " 先生";
                        break;
                    case "女":
                        realName += " 女士";
                        break;
                    default:
                        break;
                }
                cell = user.cell_number.Trim();
                gender = user.gender.Trim();
            }
            
            string entityJson = "";

            switch (scene)
            {
                case "租赁下单":
                    
                    RentOrder order = new RentOrder()
                    {
                        open_id = openId,
                        cell_number = cell,
                        real_name = realName,
                        shop = shop
                    };
                    entityJson = Newtonsoft.Json.JsonConvert.SerializeObject(order);
                    break;
                case "养护下单":
                    Models.Maintain.MaintainOrder mOrder = new Models.Maintain.MaintainOrder()
                    {
                        customerOpenId = openId,
                        cell = cell,
                        name = realName,
                        gender = gender,
                        ticketCode = code
                    };
                    entityJson = Newtonsoft.Json.JsonConvert.SerializeObject(mOrder);
                    break;
                default:
                    break;
            }
            Recept recept = new Recept()
            {
                shop = shop.Trim(),
                open_id = openId.Trim(),
                cell = cell,
                real_name = realName,
                current_step = 0,
                gender = gender,
                recept_type = scene.Trim(),
                submit_data = entityJson.Trim(),
                recept_staff = adminUser.open_id.Trim(),
                update_staff = "",
                submit_return_id = 0,
                create_date = DateTime.Now,
                update_date = DateTime.Now,
                code = code
            };
            await _context.Recept.AddAsync(recept);
            await _context.SaveChangesAsync();
            return Ok(recept);
        }

        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<Recept>> UpdateRecept(string sessionKey, Recept recept)
        {
            //Recept recept = JsonConvert.DeserializeObject(receptJson.ToString(), typeof(Recept));
            MiniAppUser adminUser = await GetUser(sessionKey);
            if (adminUser.is_admin == 0)
            {
                return BadRequest();
            }
            string entityJson = "";
            switch (recept.recept_type.Trim())
            {
                case "租赁下单":
                    //recept.rentOrder._details = null;
                    //recept.rentOrder.rentalDetails = null;
                    entityJson = Newtonsoft.Json.JsonConvert.SerializeObject(recept.rentOrder);
                    break;
                case "养护下单":
                    entityJson = Newtonsoft.Json.JsonConvert.SerializeObject(recept.maintainOrder);
                    break;
                default:
                    break;
            }
            recept.submit_data = entityJson;
            recept.update_staff = adminUser.open_id.Trim();
            recept.update_date = DateTime.Now;
            _context.Entry(recept).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(recept);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Recept>>> GetUnSubmitRecept(string shop, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            if (! await IsAdmin(sessionKey))
            {
                return BadRequest();
            }
            shop = Util.UrlDecode(shop).Trim();
            var list = await _context.Recept
                .Where(r => (r.submit_return_id == 0 && r.create_date.Date == DateTime.Now.Date && r.shop.Trim().Equals(shop)))
                .OrderBy(r => r.id).AsNoTracking().ToListAsync();
            if (list == null)
            {
                return BadRequest();
            }
            for (int i = 0; i < list.Count; i++)
            {
                Recept r = list[i];
                if (!r.recept_staff.Trim().Equals(""))
                {
                    MiniAppUser user = await _context.MiniAppUsers.FindAsync(r.recept_staff.Trim());
                    if (user != null)
                    {
                        r.recept_staff_name = user.real_name.Trim();
                    }
                    
                }
                if (!r.update_staff.Trim().Equals(""))
                {
                    MiniAppUser user = await _context.MiniAppUsers.FindAsync(r.update_staff.Trim());
                    if (user != null)
                    {
                        r.update_staff_name = user.real_name.Trim();
                    }
                }
            }
            return Ok(list);
            //return await _context.Recept.Where(r => )
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Recept>> PlaceOrder(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            if (!await IsAdmin(sessionKey))
            {
                return BadRequest();
            }
            Recept r = await _context.Recept.FindAsync(id);
            switch (r.recept_type)
            {
                case "租赁下单":
                    r = await CreateRentOrder(r);
                    break;
                case "养护下单":
                    r = await CreateMaintainOrder(r);
                    break;
                default:
                    break;
            }
            if (r == null) 
            {
                return NotFound();
            }
            return Ok(r);
        }

        [NonAction]
        public async Task<Recept> CreateMaintainOrder(Recept recept)
        {
            string jsonStr = recept.submit_data.Trim();
            Models.Maintain.MaintainOrder maintainOrder = JsonConvert.DeserializeObject<Models.Maintain.MaintainOrder>(jsonStr);

            int productId = 0;
            double totalAmount = 0;
            for (int i = 0; i < maintainOrder.items.Length; i++)
            {
                MaintainLive item = maintainOrder.items[i];
                if (item.confirmed_urgent == 1)
                {
                    if (item.confirmed_edge == 1 && item.confirmed_candle == 1)
                    {
                        productId = 137;
        }
                    else if (item.confirmed_edge == 1)
                    {
                        productId = 138;
                    }
                    else if (item.confirmed_candle == 1)
                    {
                        productId = 142;
                    }
                }
                else
                {
                    if (item.confirmed_edge == 1 && item.confirmed_candle == 1)
                    {
                        productId = 139;
        }
                    else if (item.confirmed_edge == 1)
                    {
                        productId = 140;
                    }
                    else if (item.confirmed_candle == 1)
                    {
                        productId = 143;
                    }
                }
                item.confirmed_product_id = productId;
                Models.Product.Product p = await _context.Product.FindAsync(productId);
                totalAmount = totalAmount + p.sale_price + item.confirmed_additional_fee;
            }


            double realPayAmount = totalAmount - maintainOrder.discount - maintainOrder.ticketDiscount;
            int orderId = 0;
            if (realPayAmount > 0 && (maintainOrder.payOption.Trim().Equals("现场支付") || maintainOrder.payOption.Trim().Equals("")))
            {
                OrderOnline order = new OrderOnline()
                {
                    id = 0,
                    type = "服务",
                    shop = recept.shop.Trim(),
                    open_id = recept.open_id,
                    name = recept.real_name.Trim(),
                    cell_number = recept.cell.Trim(),
                    pay_method = maintainOrder.payMethod.Trim().Equals("") ? "微信支付" : maintainOrder.payMethod.Trim(),
                    pay_memo = maintainOrder.payOption.Trim(),
                    pay_state = 0,
                    order_price = maintainOrder.summaryPrice,
                    order_real_pay_price = maintainOrder.summaryPrice,
                    ticket_amount = maintainOrder.ticketDiscount,
                    other_discount = maintainOrder.discount,
                    final_price = maintainOrder.summaryPrice - maintainOrder.ticketDiscount - maintainOrder.discount,
                    ticket_code = maintainOrder.ticketCode.Trim(),
                    staff_open_id = recept.update_staff,
                    score_rate = 0,
                    generate_score = 0

                };
                await _context.AddAsync(order);
                await _context.SaveChangesAsync();
                orderId = order.id;
            }




            for (int i = 0; i < maintainOrder.items.Length; i++)
            {
                MaintainLive m = maintainOrder.items[i];
                m.id = 0;
                m.shop = recept.shop.Trim();
                m.open_id = recept.open_id.Trim();
                m.service_open_id = recept.recept_staff.Trim();
                m.task_id = 0;
                m.confirmed_serial = m.confirmed_serial == null ? "" : m.confirmed_serial.Trim();
                if (m.confirmed_urgent == 1)
                {
                    m.confirmed_pick_date = DateTime.Now.Date;
                }
                else
                {
                    m.confirmed_pick_date = DateTime.Now.Date.AddDays(1);
                }
                m.order_id = orderId;
                m.pay_memo = maintainOrder.payOption.Trim();
                await _context.MaintainLives.AddAsync(m);
            }
            await _context.SaveChangesAsync();

            return recept;
        }

        [NonAction]
        public async Task<Recept> CreateRentOrder(Recept recept)
        {
            string jsonStr = recept.submit_data.Trim();
            RentOrder rentOrder = JsonConvert.DeserializeObject<RentOrder>(jsonStr);
            if (rentOrder.deposit_real == 0)
            {
                rentOrder.deposit_real = rentOrder.deposit;
            }
            rentOrder.deposit_final = rentOrder.deposit_real 
                - rentOrder.deposit_reduce - rentOrder.deposit_reduce_ticket;
            await _context.RentOrder.AddAsync(rentOrder);
            await _context.SaveChangesAsync();
            recept.rentOrder = rentOrder;
            if (rentOrder.id <= 0)
            {
                return recept;
            }
            recept.submit_return_id = rentOrder.id;
            _context.Entry(recept).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            for (int i = 0; i < rentOrder.details.Length; i++)
            {
                RentOrderDetail detail = rentOrder.details[i];
                detail.rent_list_id = rentOrder.id;
                await _context.RentOrderDetail.AddAsync(detail);
            }
            await _context.SaveChangesAsync();

            if (rentOrder.pay_option.Trim().Equals("现场支付") && rentOrder.deposit_final != 0)
            {

                OrderOnline order = new OrderOnline()
                {
                    id = 0,
                    type = "押金",
                    open_id = recept.open_id.Trim(),
                    cell_number = recept.cell.Trim(),
                    name = recept.real_name.Trim(),
                    pay_method = rentOrder.payMethod.Trim(),
                    order_price = rentOrder.deposit_final,
                    order_real_pay_price = rentOrder.deposit_final,
                    pay_state = 0,
                    pay_memo = rentOrder.pay_option.Trim(),
                    shop = recept.shop,
                    ticket_amount = 0,
                    have_score = 0,
                    score_rate = 0,
                    ticket_code = recept.code.Trim(),
                    other_discount = 0,
                    final_price = rentOrder.deposit_final,
                    staff_open_id = recept.update_staff.Trim().Equals("") ? recept.recept_staff.Trim() : recept.update_staff.Trim()
                };
                await _context.OrderOnlines.AddAsync(order);
                await _context.SaveChangesAsync();
                rentOrder.order_id = order.id;
                _context.Entry(rentOrder).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }

            
            return recept;
        }


      
        [HttpGet("{id}")]
        public async Task<ActionResult<Recept>> GetRecept(int id, string sessionKey)
        {
            if (!(await IsAdmin(sessionKey)))
            {
                return BadRequest();
            }
            Recept recept = await _context.Recept.FindAsync(id);
            return Ok(recept);
        }


        [NonAction]
        public async Task<bool> IsAdmin(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            return user.isAdmin;
        }

        [NonAction]
        public async Task<MiniAppUser> GetUser(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _context)).Value;
            return user.miniAppUser;
        }

     
        private bool ReceptExists(int id)
        {
            return _context.Recept.Any(e => e.id == id);
        }
    }
}
