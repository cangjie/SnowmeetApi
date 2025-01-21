using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections;
using System.Collections.Generic;
using Aop.Api.Domain;
using SnowmeetApi.Models.Users;

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

            public int length { get; set; }
            public string description { get; set; }
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

        [HttpGet("{tableName}")]
        public async Task<ActionResult<table>> GetTableDetail(string tableName)
        {
            var tableList = await _context.sysObject
                .Where(s => s.type.Trim().Equals("U") && s.name.Equals(tableName.Trim()))
                .ToListAsync();
            if (tableList == null || tableList.Count == 0)
            {
                return NotFound();
            }
            var t = tableList[0];
            var extList = await _context.extendedProperties
                .Where(e => e.major_id == t.id).OrderBy(e => e.minor_id).ToListAsync();
            string tableDesc = "";
            var columnList = await _context.sysColumn.Where(c => c.table_id == t.id)
                .OrderBy(c => c.colid).ToListAsync();
            fields[] fArr = new fields[columnList.Count];
            for (int j = 0; j < fArr.Length; j++)
            {
                fArr[j] = new fields();
                fArr[j].name = columnList[j].column_name;
                fArr[j].type = columnList[j].data_type;
                fArr[j].length = columnList[j].type_length;
                string cDesc = "";
                for (int k = 0; k < extList.Count; k++)
                {
                    if (extList[k].minor_id == j + 1)
                    {
                        cDesc = extList[k].value.Trim();
                        break;
                    }
                    else if (extList[k].minor_id == 0)
                    {
                        tableDesc = extList[k].value.Trim();
                    }
                }
                fArr[j].description = cDesc;
            }
            table tRet = new table();
            tRet.tableName = tableName.Trim();
            tRet.description = tableDesc.Trim();
            tRet.fields = fArr;
            return Ok(tRet);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<table>>> GetTables()
        {
            var tableList = await _context.sysObject.Where(s => s.type.Trim().Equals("U"))
                    .OrderBy(t => t.name).ToListAsync();
            table[] tArr = new table[tableList.Count];
            for (int i = 0; i < tableList.Count; i++)
            {
                var t = tableList[i];
                string tableName = t.name;
                var extList = await _context.extendedProperties
                    .Where(e => e.major_id == t.id)
                    .OrderBy(e => e.minor_id).ToListAsync();
                string desc = "";
                if (extList.Count > 0 && extList[0].minor_id == 0)
                {
                    desc = extList[0].value.Trim();
                }
                table currentTable = new table();
                currentTable.tableName = t.name;
                currentTable.description = desc;
                //currentTable.fields = fArr;
                tArr[i] = currentTable;
            }
            return Ok(tArr);
        }

        [HttpGet]
        public async Task<ActionResult<table[]>> TestSerial()
        {

            var tableList = await _context.sysObject.Where(s => s.type.Trim().Equals("U"))
                .OrderBy(t => t.name).ToListAsync();
            table[] tArr = new table[tableList.Count];

            for (int i = 0; i < tableList.Count; i++)
            {
                var t = tableList[i];
                string tableName = t.name;
                var extList = await _context.extendedProperties
                    .Where(e => e.major_id == t.id)
                    .OrderBy(e => e.minor_id).ToListAsync();
                string desc = "";
                if (extList.Count > 0 && extList[0].minor_id == 0)
                {
                    desc = extList[0].value.Trim();
                }
                var columnList = await _context.sysColumn//.Where(c => c.table_id == t.id)
                    .OrderBy(c => c.colid).ToListAsync();
                fields[] fArr = new fields[columnList.Count];
                for (int j = 0; j < fArr.Length; j++)
                {
                    fArr[j] = new fields();
                    fArr[j].name = columnList[j].column_name;
                    fArr[j].type = columnList[j].data_type;
                    fArr[j].length = columnList[j].type_length;
                    string cDesc = "";
                    for (int k = 0; k < extList.Count; k++)
                    {
                        if (extList[k].minor_id == j)
                        { 
                            cDesc = extList[k].value.Trim();
                            break;
                        }
                    }
                    fArr[j].description = cDesc;
                }

                table currentTable = new table();
                currentTable.tableName = t.name;
                currentTable.description = desc;
                currentTable.fields = fArr;
                tArr[i] = currentTable;

            }
            return Ok(tArr);
        }

        
        [HttpGet]
        public async Task<ActionResult<Models.Users.Member>> GetMember()
        {
            Models.Users.Member  member = await _context.member.Where(m => m.id == 15506)
                .Include(m => m.memberSocialAccounts)
                .FirstAsync();
            return Ok(member);
        }

        [HttpGet]
        public async Task<ActionResult<Models.Users.Member>> GetMemberByCell()
        {
            MemberSocialAccount msa = await _context.memberSocialAccount
                .Where(m => m.num.Trim().Equals("18601197897") && m.type.Trim().Equals("cell"))
                .Include(m => m.member)
                .FirstAsync();
            return Ok(msa);
        }
        
    }
}

