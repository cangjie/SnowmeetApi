using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Pages.SchoolStaff
{
    public class IndexModel : PageModel
    {
        private readonly SnowmeetApi.Data.ApplicationDBContext _context;

        public IndexModel(SnowmeetApi.Data.ApplicationDBContext context)
        {
            _context = context;
        }

        public IList<SnowmeetApi.Models.SchoolStaff> SchoolStaff { get;set; }

        public async Task OnGetAsync()
        {

            SchoolStaff = await _context.SchoolStaffs.ToListAsync();
        }
    }
}
