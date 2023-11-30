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
using SnowmeetApi.Models.WanLong;
using Newtonsoft.Json;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class WanlongZiwoyouHelper:ControllerBase
	{
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public string custId = "6914348";

        public string apiKey = "3951EA4CA7BF7B679787F67E6262E1DD";

        public WanlongZiwoyouHelper(ApplicationDBContext context, IConfiguration config)
		{
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
        }

        [HttpGet]
        public ActionResult<ProductQueryResult> GetProductList()
        {
            string ret = Util.GetWebContent("https://task-api-stag.zowoyoo.com/api/thirdPaty/prod/list",
                "{\"apikey\": \"3951EA4CA7BF7B679787F67E6262E1DD\",\t\"catIds\": \"\",\t\"cityId\": \"\",\t\"cityName\": \"\",\t\"custId\": 6914348 ,\t\"isConfirm\": \"\",\t\"isExpress\": \"\",\t\"isMulti\": \"\",\t\"isPackage\": \"\",\t\"isPay\": \"\",\t\"keyWord\": \"\",\t\"orderBy\": \"\",\t\"page\": 0,\t\"productNos\": \"\",\t\"resultNum\": 0,\t\"tagIds\": \"\",\t\"treeId\": \"\",\t\"viewId\": \"\"}",
                "application/json");
            ProductQueryResult r = JsonConvert.DeserializeObject<ProductQueryResult>(ret);
            return Ok(r);
        }

	}
}

