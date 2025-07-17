/*
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
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class BltDeviceController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        public BltDeviceController(ApplicationDBContext context)
        {
            _context = context;
        }
        // GET: api/BltDevice
        [HttpGet]
        public async Task<ActionResult<IEnumerable<BltDevice>>> GetBltDevice()
        {
            return await _context.BltDevice.ToListAsync();
        }
        // GET: api/BltDevice/5
        [HttpGet("{id}")]
        public async Task<ActionResult<BltDevice>> GetBltDevice(int id)
        {
            var bltDevice = await _context.BltDevice.FindAsync(id);

            if (bltDevice == null)
            {
                return NotFound();
            }

            return bltDevice;
        }
        [HttpGet]
        public ActionResult<string> Test()
        {
            string ret = DateTime.Now.ToLongTimeString();
            return Ok(ret);
        }
        private bool BltDeviceExists(int id)
        {
            return _context.BltDevice.Any(e => e.id == id);
        }
    }
}
*/