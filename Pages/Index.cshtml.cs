using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MaterialsAlstar.Data;
using MaterialsAlstar.Models;

namespace MaterialsAlstar.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;

    public IndexModel(AppDbContext context)
    {
        _context = context;
    }

    public IList<MaterialItem> Materials { get; set; } = new List<MaterialItem>();

    public async Task OnGetAsync()
    {
        Materials = await _context.Materials.OrderBy(m => m.Name).ToListAsync();
    }
}
