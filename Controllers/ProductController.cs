using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using Newtonsoft.Json;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ProductController : ControllerBase
    {



        private readonly ApplicationDBContext _context;

        private IConfiguration _config;
        private IConfiguration _oriConfig;

        public string _appId = "";

        public ProductController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();

        }


        /// <summary>
        /// 雪场名 → 雪票分类 id（category.biz_type = "雪票"：南山雪票 0301 / 万龙雪票 0302）。
        /// 原来是拿 product.shop 比 resort —— 门店比雪场，崇礼旗舰店卖的是万龙雪场的票，
        /// resort="万龙" 一条也查不出来。改按分类筛后 南山 80 条 / 万龙 74 条，与按 resort 筛等价。
        /// </summary>
        [NonAction]
        private async Task<int?> ResolveSkiPassCategoryId(string resort)
        {
            string name = (resort ?? "").Trim() + "雪票";
            return await _context.category.AsNoTracking()
                .Where(c => c.biz_type == "雪票" && c.name == name && c.valid == 1)
                .Select(c => (int?)c.id).FirstOrDefaultAsync();
        }

        [HttpGet]
        [ActionName("GetNanshanTodaySkipass")]
        public async Task<ActionResult<IEnumerable<Product>>> GetNanshanTodaySkipass()
        {
            int? nanshanCategoryId = await ResolveSkiPassCategoryId("南山");
            return await _context.product
                .Where(p => (p.name.Trim().IndexOf("当日票") >= 0 && p.category_id == nanshanCategoryId && p.end_date > DateTime.Now))
                .ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> Get(int id)
        {
            return await _context.product.FindAsync(id);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetMaintainProduct(string shop)
        {
            return await _context.product
                .Where(p => (p.id == 137 || p.id == 138 || p.id == 139 || p.id == 140 || p.id == 142 || p.id == 143 || p.id == 202))
                .ToListAsync();
        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<Product>> SetHidden(int productId, int hidden, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_context, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return BadRequest();
            }
            Product product = await _context.product.FindAsync(productId);
            if (product == null)
            {
                return NotFound();
            }
            product.hidden = hidden;
            _context.product.Entry(product).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(product);
        }
        /*
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetSkiPassProduct(string resort, DateTime date, string tags)
        {
            string[] tagArr = tags == null? new string[] { } : Util.UrlDecode(tags.Trim()).Split(',');
            
            int? skiPassCategoryId = await ResolveSkiPassCategoryId(resort);
            var skiPassProdustList = await _context.Product.Where(p => (p.category_id == skiPassCategoryId && p.hidden == 0))
                .Join(_context.skiPass, p => p.id, s => s.product_id,
                (p, s) => new { p.id, p.name, p.sale_price, p.deposit, s.product_id,
                    s.resort, s.end_sale_time, s.rules,
                    s.available_days, s.unavailable_days,
                    s.tags })

                .OrderBy(p => p.sale_price).ToListAsync();


            for (int i = 0; i < skiPassProdustList.Count; i++)
            {
                var r = skiPassProdustList[i];
                SkiPass skiPass = new SkiPass()
                {
                    product_id = r.product_id,
                    resort = r.resort.Trim(),
                    end_sale_time = r.end_sale_time,
                    rules = r.rules,
                    available_days = r.available_days,
                    unavailable_days = r.unavailable_days,
                    tags = r.tags

                };

                if (!skiPass.DateMatch(date) || !skiPass.TagMatch(tagArr))
                {
                    skiPassProdustList.RemoveAt(i);
                    i--;
                }


            }


            return skiPassProdustList;
        }
        */
        /*

        // GET: api/Product
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProduct()
        {
            return await _context.Product.ToListAsync();
        }

        // GET: api/Product/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProduct(int id)
        {
            var product = await _context.Product.FindAsync(id);

            if (product == null)
            {
                return NotFound();
            }

            return product;
        }

        // PUT: api/Product/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutProduct(int id, Product product)
        {
            if (id != product.id)
            {
                return BadRequest();
            }

            _context.Entry(product).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductExists(id))
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

        // POST: api/Product
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<Product>> PostProduct(Product product)
        {
            _context.Product.Add(product);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetProduct", new { id = product.id }, product);
        }

        // DELETE: api/Product/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Product.FindAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            _context.Product.Remove(product);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ProductExists(int id)
        {
            return _context.Product.Any(e => e.id == id);
        }
        */
    }
}
