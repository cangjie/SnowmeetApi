using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class DDController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        private IConfiguration _oriConfig;

        private readonly IHttpContextAccessor _httpContextAccessor;

        public struct fields
        {
            public string name { get; set; }
            public string type { get; set; }
            public string desccription { get; set; }
        }

        public struct table
        {
            public string tableName { get; set; }
            public string description { get; set; }
            public fields[] fields { get; set; }
        }

        public struct sysobject
        {
            public int id { get; set; }
            public string name { get; set; }
        }

        public DDController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
		{
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
        }

        [HttpGet]
        public async  Task<ActionResult<SerialTest>> TestSerial()
        {
            //var tableList = _context.Database.SqlQuery("select * from sysobjects where type = 'U'");//   .ExecuteSqlRaw(" select * from sysobjects where type = 'U' ");
            //_context.oAReceive.FromSqlRaw<sysobject>(" select * from sysobjects where type = 'U' ");
            var tableList = await _context.sysObject.Where(s => s.type.Trim().Equals("U")).ToListAsync();
            var extList = await _context.extendedProperties.ToListAsync();

            return BadRequest();
        }

    }
}

