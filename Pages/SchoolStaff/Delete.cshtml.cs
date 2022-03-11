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
    public class DeleteModel : PageModel
    {
        private readonly SnowmeetApi.Data.ApplicationDBContext _context;

        public DeleteModel(SnowmeetApi.Data.ApplicationDBContext context)
        {
            _context = context;
        }

        [BindProperty]
        public SnowmeetApi.Models.SchoolStaff SchoolStaff { get; set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            SchoolStaff = await _context.SchoolStaffs.FirstOrDefaultAsync(m => m.open_id == id);

            if (SchoolStaff == null)
            {
                return NotFound();
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            SchoolStaff = await _context.SchoolStaffs.FindAsync(id);

            if (SchoolStaff != null)
            {
                _context.SchoolStaffs.Remove(SchoolStaff);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}
