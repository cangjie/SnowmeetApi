using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
namespace SnowmeetApi.Controllers.rfid
{
    [Route("rfid/[controller]")]
    [ApiController]
    public class ScanController : ControllerBase
    {

        private readonly ApplicationDBContext _context;

        public ScanController(ApplicationDBContext context)
        {
            _context = context;
        }

        // GET: api/Scan
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET: api/Scan/5
        [HttpGet("{id}", Name = "Get")]
        public string Get(int id)
        {
            return "value";
        }

        // POST: api/Scan
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT: api/Scan/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE: api/Scan/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
