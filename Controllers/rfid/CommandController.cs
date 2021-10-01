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
    public class CommandController : ControllerBase
    {
        private readonly ApplicationDBContext _context;


        public CommandController(ApplicationDBContext context)
        {
            _context = context;
        }

        // GET: api/Command
        [HttpGet]
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET: api/Command/5
        [HttpGet("{id}", Name = "Get")]
        public string Get(int id)
        {
            return "value";
        }

        // POST: api/Command
        [HttpPost]
        public void Post([FromBody] string value)
        {
        }

        // PUT: api/Command/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE: api/Command/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
