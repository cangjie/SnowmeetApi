using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Pages.SchoolStaff
{
    public class EditModel : PageModel
    {
        private readonly SnowmeetApi.Data.ApplicationDBContext _context;

        public EditModel(SnowmeetApi.Data.ApplicationDBContext context)
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

        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see https://aka.ms/RazorPagesCRUD.
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            _context.Attach(SchoolStaff).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SchoolStaffExists(SchoolStaff.open_id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return RedirectToPage("./Index");
        }

        private bool SchoolStaffExists(string id)
        {
            return _context.SchoolStaffs.Any(e => e.open_id == id);
        }
    }
}
