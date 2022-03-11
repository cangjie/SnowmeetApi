using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Pages.SummerMaintain
{
    public class DeleteModel : PageModel
    {
        private readonly SnowmeetApi.Data.ApplicationDBContext _context;

        public DeleteModel(SnowmeetApi.Data.ApplicationDBContext context)
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

        public async Task<IActionResult> OnPostAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            SummerMaintain = await _context.SummerMaintain.FindAsync(id);

            if (SummerMaintain != null)
            {
                _context.SummerMaintain.Remove(SummerMaintain);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}
