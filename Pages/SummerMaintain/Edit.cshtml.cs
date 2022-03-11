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

namespace SnowmeetApi.Pages.SummerMaintain
{
    public class EditModel : PageModel
    {
        private readonly SnowmeetApi.Data.ApplicationDBContext _context;

        public EditModel(SnowmeetApi.Data.ApplicationDBContext context)
        {
            _context = context;
        }

        [BindProperty]
        public SnowmeetApi.Models.SummerMaintain SummerMaintain { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            SummerMaintain = await _context.SummerMaintain.FirstOrDefaultAsync(m => m.id == id);

            if (SummerMaintain == null)
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

            _context.Attach(SummerMaintain).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SummerMaintainExists(SummerMaintain.id))
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

        private bool SummerMaintainExists(int id)
        {
            return _context.SummerMaintain.Any(e => e.id == id);
        }
    }
}
