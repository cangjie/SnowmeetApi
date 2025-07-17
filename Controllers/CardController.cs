using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class CardController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        
        public string _appId = "";

        public CardController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config;//.GetSection("Settings");
            _appId = _config.GetSection("Settings").GetSection("AppId").Value.Trim();
            
        }

        

        [NonAction]
        public  string CreateCard(string type)
        {
            string code = Util.GetRandomCode(9);
            int retryTimes = 0;
            bool isDuplicate = true;
            for (; isDuplicate && retryTimes < 1000;)
            {
                isDuplicate = _context.card.Any(e => e.card_no == code);
            }
            if (isDuplicate)
            {
                return "";
            }
            Card card = new Card()
            {
                card_no = code,
                is_ticket = 0,
                type = type
            };
            try
            {
                _context.card.Add(card);
                _context.SaveChanges();
                return code.Trim();
            }
            catch
            {

            }
            return "";
        }
        private bool CardExists(string id)
        {
            return _context.card.Any(e => e.card_no == id);
        }
    }
}
