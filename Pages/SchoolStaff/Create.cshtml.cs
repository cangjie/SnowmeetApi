using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Pages.SchoolStaff
{
    public class CreateModel : PageModel
    {
        private readonly SnowmeetApi.Data.ApplicationDBContext _context;

        public CreateModel(SnowmeetApi.Data.ApplicationDBContext context)
        {
            _context = context;
        }

        public IActionResult OnGet()
        {
            return Page();
        }

        [BindProperty]
        public SnowmeetApi.Models.SchoolStaff SchoolStaff { get; set; }

        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            _context.SchoolStaffs.Add(SchoolStaff);
            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}
