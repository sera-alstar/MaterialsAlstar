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
    public int TotalItems => Materials.Count;
    public decimal TotalQuantityReceived => Materials.Sum(m => m.QuantityReceived);
    public int UniqueJobsCount => Materials.Select(m => m.JobNumber).Distinct().Count();
    public int UniqueHeatNumbersCount => Materials.Select(m => m.HeatNumber).Where(h => !string.IsNullOrEmpty(h)).Distinct().Count();
    public int UniqueBinsCount => Materials.Select(m => m.BinLocation).Where(b => !string.IsNullOrEmpty(b)).Distinct().Count();
    public List<string> JobOptions => Materials.Select(m => m.JobNumber).Distinct().OrderBy(j => j).ToList();
    public List<string> BinOptions => Materials.Select(m => m.BinLocation).Where(b => !string.IsNullOrEmpty(b)).Distinct().OrderBy(b => b).ToList();

    public async Task OnGetAsync()
    {
        Materials = await _context.Materials
            .OrderBy(m => m.JobNumber)
            .ThenBy(m => m.PoNumber)
            .ThenBy(m => m.Id)
            .ToListAsync();
    }
}
