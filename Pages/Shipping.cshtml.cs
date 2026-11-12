using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MaterialsAlstar.Data;
using MaterialsAlstar.Models;

namespace MaterialsAlstar.Pages;

[IgnoreAntiforgeryToken]
public class ShippingModel : PageModel
{
    private readonly AppDbContext _context;

    public ShippingModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string TypeFilter { get; set; } = "all"; // "all", "Shiploose", "Assembly"

    [BindProperty(SupportsGet = true)]
    public string JobFilter { get; set; } = "all";

    public List<BillOfLading> BillsOfLading { get; set; } = new();
    public List<string> JobOptions { get; set; } = new();
    public List<string> KitterOptions { get; set; } = new();

    public string NextBolNumber { get; set; } = string.Empty;

    // KPI Metrics
    public int TotalBolsCount { get; set; }
    public int ShiplooseCount { get; set; }
    public int AssemblyCount { get; set; }
    public int TotalPalletsCount { get; set; }
    public int TotalCratesCount { get; set; }

    public async Task OnGetAsync()
    {
        var persistedBols = BillOfLading.LoadPersistedBols();
        if (persistedBols != null && persistedBols.Count > 0)
        {
            var existingNumbers = new HashSet<string>(
                await _context.BillsOfLading.Select(b => b.BolNumber).ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            bool addedMissing = false;
            foreach (var pb in persistedBols)
            {
                if (!string.IsNullOrWhiteSpace(pb.BolNumber) && !existingNumbers.Contains(pb.BolNumber))
                {
                    pb.Id = 0;
                    _context.BillsOfLading.Add(pb);
                    existingNumbers.Add(pb.BolNumber);
                    addedMissing = true;
                }
            }

            if (addedMissing)
            {
                await _context.SaveChangesAsync();
            }
        }

        var allBols = await _context.BillsOfLading
            .OrderByDescending(b => b.DateCreated)
            .ThenByDescending(b => b.Id)
            .ToListAsync();

        NextBolNumber = BillOfLading.GenerateNextBolNumber(allBols.Select(b => b.BolNumber), DateTime.Now);

        TotalBolsCount = allBols.Count;
        ShiplooseCount = allBols.Count(b => string.Equals(b.Type, "Shiploose", StringComparison.OrdinalIgnoreCase));
        AssemblyCount = allBols.Count(b => string.Equals(b.Type, "Assembly", StringComparison.OrdinalIgnoreCase));
        TotalPalletsCount = allBols.Sum(b => b.PalletCount);
        TotalCratesCount = allBols.Sum(b => b.CrateCount);

        // Gather linked Job #s from across the entire application
        var materialJobs = await _context.Materials
            .Where(m => !string.IsNullOrWhiteSpace(m.JobNumber))
            .Select(m => m.JobNumber!.Trim())
            .Distinct()
            .ToListAsync();

        var expectedJobs = await _context.ExpectedOrders
            .Where(o => !string.IsNullOrWhiteSpace(o.JobNumber))
            .Select(o => o.JobNumber!.Trim())
            .Distinct()
            .ToListAsync();

        var receivingJobs = await _context.ReceivingLogs
            .Where(r => !string.IsNullOrWhiteSpace(r.JobNumber))
            .Select(r => r.JobNumber!.Trim())
            .Distinct()
            .ToListAsync();

        var bolJobs = allBols
            .Where(b => !string.IsNullOrWhiteSpace(b.JobNumber))
            .Select(b => b.JobNumber.Trim())
            .Distinct()
            .ToList();

        JobOptions = materialJobs
            .Concat(expectedJobs)
            .Concat(receivingJobs)
            .Concat(bolJobs)
            .Where(j => !string.IsNullOrWhiteSpace(j))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(j => j)
            .ToList();

        if (JobOptions.Count == 0)
        {
            JobOptions.Add("251078");
        }

        // Gather known kitters/receivers for suggestions
        var receivers = await _context.ReceivingLogs
            .Where(r => !string.IsNullOrWhiteSpace(r.ReceiverName))
            .Select(r => r.ReceiverName!.Trim())
            .Distinct()
            .ToListAsync();

        KitterOptions = allBols
            .Select(b => b.Kitter)
            .Concat(receivers)
            .Concat(new[] { "Brandon", "Kris", "Aaron Victoria" })
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k)
            .ToList();

        // Filter query
        IEnumerable<BillOfLading> query = allBols;

        if (!string.IsNullOrWhiteSpace(TypeFilter) && TypeFilter != "all")
        {
            query = query.Where(b => string.Equals(b.Type, TypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(JobFilter) && JobFilter != "all")
        {
            query = query.Where(b => string.Equals(b.JobNumber, JobFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();
            query = query.Where(b =>
                b.BolNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.JobNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.ShipFrom.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.ShipTo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.BatchId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Priorities.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Type.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                b.Kitter.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (b.Notes != null && b.Notes.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        BillsOfLading = query.ToList();
    }

    public async Task<IActionResult> OnPostCreateBolAsync(
        string jobNumber,
        string? shipFrom,
        string? shipTo,
        int batchId,
        string priorities,
        string type,
        string kitter,
        int palletCount,
        int crateCount,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(jobNumber))
        {
            TempData["WarningMessage"] = "Job # is required when creating a Bill of Lading.";
            return RedirectToPage();
        }

        var existingBolNumbers = await _context.BillsOfLading
            .Select(b => b.BolNumber)
            .ToListAsync();

        var now = DateTime.Now;
        var autoBolNumber = BillOfLading.GenerateNextBolNumber(existingBolNumbers, now);

        string normalizedType = string.Equals(type?.Trim(), "Assembly", StringComparison.OrdinalIgnoreCase)
            ? "Assembly"
            : "Shiploose";

        string normalizedPriorities = NormalizePriorities(priorities);

        var newBol = new BillOfLading
        {
            BolNumber = autoBolNumber,
            JobNumber = jobNumber.Trim(),
            ShipFrom = string.IsNullOrWhiteSpace(shipFrom)
                ? "Alstar Group - Calgary Fab Shop\n8700 Venture Ave\nCalgary, AB T3S 0A2"
                : shipFrom.Trim(),
            ShipTo = string.IsNullOrWhiteSpace(shipTo)
                ? "Cenovus"
                : shipTo.Trim(),
            BatchId = Math.Max(0, batchId),
            Priorities = string.IsNullOrWhiteSpace(normalizedPriorities) ? "1" : normalizedPriorities,
            Type = normalizedType,
            Kitter = string.IsNullOrWhiteSpace(kitter) ? "Unassigned" : kitter.Trim(),
            PalletCount = Math.Max(0, palletCount),
            CrateCount = Math.Max(0, crateCount),
            DateCreated = now,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        _context.BillsOfLading.Add(newBol);
        await _context.SaveChangesAsync();

        var allSaved = await _context.BillsOfLading.ToListAsync();
        BillOfLading.SavePersistedBols(allSaved);

        TempData["SuccessMessage"] = $"Bill of Lading {newBol.BolNumber} created for Job {newBol.JobNumber}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditBolAsync(
        int id,
        string? bolNumber,
        string jobNumber,
        string? shipFrom,
        string? shipTo,
        int batchId,
        string priorities,
        string type,
        string kitter,
        int palletCount,
        int crateCount,
        string? notes)
    {
        var bol = await _context.BillsOfLading.FirstOrDefaultAsync(b => b.Id == id);
        if (bol == null && !string.IsNullOrWhiteSpace(bolNumber))
        {
            bol = await _context.BillsOfLading.FirstOrDefaultAsync(b => b.BolNumber == bolNumber.Trim());
        }

        if (bol == null)
        {
            TempData["WarningMessage"] = "Could not find the requested Bill of Lading to update.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(jobNumber))
        {
            TempData["WarningMessage"] = "Job # is required when updating a Bill of Lading.";
            return RedirectToPage();
        }

        string normalizedType = string.Equals(type?.Trim(), "Assembly", StringComparison.OrdinalIgnoreCase)
            ? "Assembly"
            : "Shiploose";

        string normalizedPriorities = NormalizePriorities(priorities);

        // BOL # and DateCreated remain unchanged
        bol.JobNumber = jobNumber.Trim();
        bol.ShipFrom = string.IsNullOrWhiteSpace(shipFrom)
            ? "Alstar Group - Calgary Fab Shop\n8700 Venture Ave\nCalgary, AB T3S 0A2"
            : shipFrom.Trim();
        bol.ShipTo = string.IsNullOrWhiteSpace(shipTo)
            ? "Cenovus"
            : shipTo.Trim();
        bol.BatchId = Math.Max(0, batchId);
        bol.Priorities = string.IsNullOrWhiteSpace(normalizedPriorities) ? "1" : normalizedPriorities;
        bol.Type = normalizedType;
        bol.Kitter = string.IsNullOrWhiteSpace(kitter) ? "Unassigned" : kitter.Trim();
        bol.PalletCount = Math.Max(0, palletCount);
        bol.CrateCount = Math.Max(0, crateCount);
        bol.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        await _context.SaveChangesAsync();

        var allSaved = await _context.BillsOfLading.ToListAsync();
        BillOfLading.SavePersistedBols(allSaved);

        TempData["SuccessMessage"] = $"Bill of Lading {bol.BolNumber} updated.";
        return RedirectToPage();
    }

    private static string NormalizePriorities(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var tokens = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var validTokens = new List<string>();

        foreach (var token in tokens)
        {
            var trimmed = token.Trim();
            if (Regex.IsMatch(trimmed, @"^\d+$"))
            {
                validTokens.Add(trimmed);
            }
            else
            {
                var rangeMatch = Regex.Match(trimmed, @"^(\d+)\s*-\s*(\d+)$");
                if (rangeMatch.Success)
                {
                    int start = int.Parse(rangeMatch.Groups[1].Value);
                    int end = int.Parse(rangeMatch.Groups[2].Value);
                    if (start > end)
                    {
                        (start, end) = (end, start);
                    }
                    validTokens.Add(start == end ? $"{start}" : $"{start}-{end}");
                }
                else if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    validTokens.Add(trimmed);
                }
            }
        }

        return string.Join(", ", validTokens);
    }
}
