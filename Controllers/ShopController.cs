using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class ShopController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        public ShopController(ApplicationDBContext context)
        {
            _context = context;
        }

        // GET: api/Shop
        [HttpGet]
        public async Task<ActionResult<ActionResult<List<Shop>>>> GetShop()
        {
            return Ok(new ApiResult<List<Shop>>()
            {
                code = 0,
                message = "",
                data = await _context.shop.OrderBy(s => s.sort).AsNoTracking().ToListAsync()
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<Shop>>> GetShopByName(string shopName)
        {
            shopName = Util.UrlDecode(shopName);
            Shop shop = await _context.shop.Where(s => s.name.Trim().Equals(shopName)).AsNoTracking().FirstOrDefaultAsync();
            return Ok(new ApiResult<Shop>()
            {
                code = 0,
                message = "",
                data = shop
            });
        }
    }
}
