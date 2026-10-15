using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MaterialsAlstar.Data;
using MaterialsAlstar.Models;

namespace MaterialsAlstar.Pages;

[IgnoreAntiforgeryToken]
public class ReceivingLogModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;

    public ReceivingLogModel(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    // View & Filter Parameters
    [BindProperty(SupportsGet = true)]
    public string GroupBy { get; set; } = "loadtype"; // "loadtype", "staging", "job", "flat"

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StagingFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? LoadTypeFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; } = "active"; // "active" (landed/inspecting), "all", "completed"

    [BindProperty(SupportsGet = true)]
    public string? FilterJpKey { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterVendor { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterPackingSlip { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterDate { get; set; }

    // KPIs & Metrics
    public int TotalLandedCount { get; set; }
    public int TotalAdminPendingCount { get; set; }
    public int TotalInInspectionCount { get; set; }
    public int TotalPalletsCount { get; set; }
    public int TotalBoxesCount { get; set; }

    // Admin Verification Queue
    public List<ReceivingLogEntry> AdminPendingShipments { get; set; } = new();

    // Grouped Entries
    public List<ReceivingGroupViewModel> Groups { get; set; } = new();

    // Filter Options
    public List<string> StagingLocationOptions { get; set; } = new();
    public List<string> LoadTypeOptions { get; set; } = new();
    public List<JobLookupOption> AvailableJobs { get; set; } = new();

    public class JobLookupOption
    {
        public string JobNumber { get; set; } = string.Empty;
        public string? ClientName { get; set; }
    }

    public class ReceivingGroupViewModel
    {
        public string GroupTitle { get; set; } = string.Empty;
        public string GroupKey { get; set; } = string.Empty;
        public string IconClass { get; set; } = "bi-box-seam";
        public string BadgeClass { get; set; } = "bg-primary";
        public int TotalShipments => Items.Count;
        public int TotalPallets => Items.Sum(i => i.PalletCount);
        public int TotalCrates => Items.Sum(i => i.CrateCount);
        public int TotalBoxes => Items.Sum(i => i.BoxCount);
        public List<ReceivingLogEntry> Items { get; set; } = new();
    }

    public async Task OnGetAsync()
    {
        // 1. Load Admin Verification Queue (entries that need Materials Admin approval)
        AdminPendingShipments = await _context.ReceivingLogs
            .Where(r => !r.IsAdminVerified)
            .OrderByDescending(r => r.ArrivalDateTime)
            .ToListAsync();

        TotalAdminPendingCount = AdminPendingShipments.Count;

        // 2. Load verified landed entries
        var query = _context.ReceivingLogs
            .Where(r => r.IsAdminVerified)
            .AsQueryable();

        // Status Filter
        if (StatusFilter == "active")
        {
            query = query.Where(r => r.Status != "Complete");
        }
        else if (StatusFilter == "completed")
        {
            query = query.Where(r => r.Status == "Complete");
        }

        var allVerified = await query.ToListAsync();

        TotalLandedCount = allVerified.Count;
        TotalInInspectionCount = allVerified.Count(r => r.Status == "In Inspection");
        TotalPalletsCount = allVerified.Sum(r => r.PalletCount);
        TotalBoxesCount = allVerified.Sum(r => r.BoxCount);

        // Filter Options
        StagingLocationOptions = allVerified
            .Select(r => r.StagingLocation)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        LoadTypeOptions = allVerified
            .Select(r => r.LoadType)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        // Distinct jobs from Receiving Log entries for Admin Verification & Approval Modal
        var rawJobs = await _context.ReceivingLogs
            .Where(r => !string.IsNullOrWhiteSpace(r.JobNumber))
            .Select(r => new { Job = r.JobNumber!.Trim(), r.ClientName })
            .ToListAsync();

        AvailableJobs = rawJobs
            .GroupBy(r => r.Job, StringComparer.OrdinalIgnoreCase)
            .Select(g => new JobLookupOption
            {
                JobNumber = g.Key,
                ClientName = g.Select(x => x.ClientName).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
            })
            .OrderBy(j => j.JobNumber)
            .ToList();

        // 3. Search & Filters
        IEnumerable<ReceivingLogEntry> filtered = allVerified;

        // Apply specific column text filters first
        if (!string.IsNullOrWhiteSpace(FilterJpKey))
        {
            var cleanKey = FilterJpKey.Trim().ToLowerInvariant().Replace("-", "").Replace(" ", "");
            filtered = filtered.Where(r => r.JpKey.ToLowerInvariant().Replace("-", "").Replace(" ", "").Contains(cleanKey));
        }

        if (!string.IsNullOrWhiteSpace(FilterVendor))
        {
            var vTerm = FilterVendor.Trim().ToLowerInvariant();
            filtered = filtered.Where(r => r.VendorName != null && r.VendorName.ToLowerInvariant().Contains(vTerm));
        }

        if (!string.IsNullOrWhiteSpace(FilterPackingSlip))
        {
            var psTerm = FilterPackingSlip.Trim().ToLowerInvariant();
            filtered = filtered.Where(r => r.PackingSlipNumber != null && r.PackingSlipNumber.ToLowerInvariant().Contains(psTerm));
        }

        if (!string.IsNullOrWhiteSpace(FilterDate))
        {
            var dTerm = FilterDate.Trim().ToLowerInvariant();
            filtered = filtered.Where(r =>
                r.ArrivalDateTime.ToString("yyyy-MM-dd").Contains(dTerm) ||
                r.ArrivalDateTime.ToString("MMM d, yyyy").ToLowerInvariant().Contains(dTerm) ||
                r.ArrivalDateTime.ToString("MM/dd/yyyy").Contains(dTerm) ||
                r.ArrivalDateTime.ToString("MM-dd-yyyy").Contains(dTerm) ||
                r.ArrivalDateTime.ToString("MMMM d, yyyy").ToLowerInvariant().Contains(dTerm) ||
                r.ArrivalDateTime.ToString("ddd").ToLowerInvariant().Contains(dTerm) ||
                r.ArrivalDateTime.ToString("yyyy").Contains(dTerm));
        }

        // Keep the original search fields looking through the filtered results from the other fields
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim().ToLowerInvariant();
            var termClean = term.Replace("-", "").Replace(" ", "");

            filtered = filtered.Where(r =>
                r.JpKey.ToLowerInvariant().Contains(termClean) ||
                (r.JobNumber != null && r.JobNumber.ToLowerInvariant().Contains(term)) ||
                r.PoNumber.ToLowerInvariant().Contains(term) ||
                r.VendorName.ToLowerInvariant().Contains(term) ||
                r.PackingSlipNumber.ToLowerInvariant().Contains(term) ||
                r.BolNumber.ToLowerInvariant().Contains(term) ||
                r.StagingLocation.ToLowerInvariant().Contains(term) ||
                r.LoadType.ToLowerInvariant().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(StagingFilter))
        {
            filtered = filtered.Where(r => r.StagingLocation.Equals(StagingFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(LoadTypeFilter))
        {
            filtered = filtered.Where(r => r.LoadType.Equals(LoadTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        // 4. Grouping & Sorting Logic:
        // Group similar load types together, then order within group by JP Key -> Job # -> Staging Location
        Groups = BuildGroups(filtered, GroupBy);
    }

    private List<ReceivingGroupViewModel> BuildGroups(IEnumerable<ReceivingLogEntry> items, string groupBy)
    {
        var result = new List<ReceivingGroupViewModel>();

        switch (groupBy.ToLowerInvariant())
        {
            case "staging":
                var stagingGroups = items
                    .GroupBy(i => string.IsNullOrWhiteSpace(i.StagingLocation) ? "Unassigned Location" : i.StagingLocation)
                    .OrderBy(g => g.Key);

                foreach (var g in stagingGroups)
                {
                    result.Add(new ReceivingGroupViewModel
                    {
                        GroupTitle = g.Key,
                        GroupKey = g.Key,
                        IconClass = "bi-geo-alt-fill",
                        BadgeClass = "bg-success",
                        Items = g
                            .OrderBy(i => i.JpKey)
                            .ThenBy(i => i.JobNumber ?? "ZZZZZ")
                            .ThenBy(i => i.StagingLocation)
                            .ToList()
                    });
                }
                break;

            case "job":
                var jobGroups = items
                    .GroupBy(i => string.IsNullOrWhiteSpace(i.JobNumber) ? "Client Supplied (Job Pending)" : i.JobNumber)
                    .OrderBy(g => g.Key);

                foreach (var g in jobGroups)
                {
                    result.Add(new ReceivingGroupViewModel
                    {
                        GroupTitle = g.Key.StartsWith("Client") ? g.Key : $"Job {g.Key}",
                        GroupKey = g.Key,
                        IconClass = "bi-briefcase-fill",
                        BadgeClass = "bg-dark",
                        Items = g
                            .OrderBy(i => i.JpKey)
                            .ThenBy(i => i.LoadType)
                            .ThenBy(i => i.StagingLocation)
                            .ToList()
                    });
                }
                break;

            case "flat":
                result.Add(new ReceivingGroupViewModel
                {
                    GroupTitle = "All Landed Shipments (Sorted by JP Key)",
                    GroupKey = "all",
                    IconClass = "bi-list-ul",
                    BadgeClass = "bg-primary",
                    Items = items
                        .OrderBy(i => i.JpKey)
                        .ThenBy(i => i.JobNumber ?? "ZZZZZ")
                        .ThenBy(i => i.StagingLocation)
                        .ToList()
                });
                break;

            case "loadtype":
            default:
                // Primary Requirement: Group similar load types together (SNG, Pipe, Valves, Flanges & Fittings, Supports, etc.)
                // Then order by JP key, then job#, then staging location.
                var typeGroups = items
                    .GroupBy(i => NormalizeGroupCategory(i.LoadType))
                    .OrderBy(g => GetCategoryDisplayPriority(g.Key));

                foreach (var g in typeGroups)
                {
                    var catInfo = GetCategoryMeta(g.Key);
                    result.Add(new ReceivingGroupViewModel
                    {
                        GroupTitle = g.Key,
                        GroupKey = g.Key,
                        IconClass = catInfo.Icon,
                        BadgeClass = catInfo.Badge,
                        Items = g
                            .OrderBy(i => i.JpKey)
                            .ThenBy(i => i.JobNumber ?? "ZZZZZ")
                            .ThenBy(i => i.StagingLocation)
                            .ToList()
                    });
                }
                break;
        }

        return result;
    }

    private static string NormalizeGroupCategory(string loadType)
    {
        var lower = (loadType ?? "").ToLowerInvariant();

        if (lower.Contains("sng") || lower.Contains("bolt") || lower.Contains("stud") || lower.Contains("gasket") || lower.Contains("fastener"))
            return "Studs, Nuts, Gaskets (SNG / Bolt-Up)";

        if (lower.Contains("pipe") && !lower.Contains("fitting"))
            return "Pipe";

        if (lower.Contains("valve"))
            return "Valves";

        if (lower.Contains("flange") || lower.Contains("fitting"))
            return "Flanges & Fittings";

        if (lower.Contains("support"))
            return "Supports";

        if (lower.Contains("instrument") || lower.Contains("electrical"))
            return "Instrumentation & Electrical";

        if (lower.Contains("paint") || lower.Contains("consumable"))
            return "Paint & Consumables";

        return string.IsNullOrWhiteSpace(loadType) ? "General Materials / Misc" : loadType;
    }

    private static int GetCategoryDisplayPriority(string category)
    {
        return category switch
        {
            var c when c.Contains("SNG") => 1,
            var c when c.Equals("Pipe", StringComparison.OrdinalIgnoreCase) => 2,
            var c when c.Contains("Valve") => 3,
            var c when c.Contains("Flange") => 4,
            var c when c.Contains("Support") => 5,
            var c when c.Contains("Instrumentation") => 6,
            _ => 10
        };
    }

    private static (string Icon, string Badge) GetCategoryMeta(string category)
    {
        if (category.Contains("SNG")) return ("bi-gear-wide-connected", "bg-warning text-dark");
        if (category.Contains("Pipe")) return ("bi-symmetry-horizontal", "bg-primary text-white");
        if (category.Contains("Valve")) return ("bi-droplet-half", "bg-danger text-white");
        if (category.Contains("Flange")) return ("bi-record-circle", "bg-success text-white");
        if (category.Contains("Support")) return ("bi-bounding-box", "bg-info text-dark");
        if (category.Contains("Instrumentation")) return ("bi-cpu", "bg-secondary text-white");
        return ("bi-box-seam", "bg-dark text-white");
    }

    public async Task<IActionResult> OnPostApproveShipmentAsync(
        int entryId,
        string? jobNumber,
        string? assignedLoadType,
        string? stagingLocation,
        string? adminNotes)
    {
        var entry = await _context.ReceivingLogs.FindAsync(entryId);
        if (entry == null) return NotFound();

        entry.IsAdminVerified = true;
        entry.AdminVerifiedBy = "Materials Admin";
        entry.AdminVerifiedAt = DateTime.Now;
        entry.Status = "Landed - Pending Inspection";

        if (!string.IsNullOrWhiteSpace(jobNumber))
        {
            entry.JobNumber = jobNumber.Trim();
            entry.JpKey = $"{jobNumber.Trim()}{entry.PoNumber}";
        }

        if (!string.IsNullOrWhiteSpace(assignedLoadType))
        {
            entry.LoadType = assignedLoadType;
        }

        if (!string.IsNullOrWhiteSpace(stagingLocation))
        {
            entry.StagingLocation = stagingLocation;
        }

        if (!string.IsNullOrWhiteSpace(adminNotes))
        {
            entry.Notes = string.IsNullOrWhiteSpace(entry.Notes)
                ? $"[Admin Verified]: {adminNotes}"
                : $"{entry.Notes} | [Admin Verified]: {adminNotes}";
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Shipment {entry.JpKey} (PO {entry.PoNumber} - {entry.VendorName}) verified and approved into Receiving Queue at {entry.StagingLocation}.";

        return RedirectToPage(new { GroupBy, StagingFilter, LoadTypeFilter, SearchTerm });
    }

    public async Task<IActionResult> OnPostStartInspectionAsync(int entryId, string receiverName)
    {
        var entry = await _context.ReceivingLogs.FindAsync(entryId);
        if (entry == null) return NotFound();

        entry.Status = "In Inspection";
        entry.ReceiverName = string.IsNullOrWhiteSpace(receiverName) ? "Receiver" : receiverName.Trim();

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Physical receiving started for {entry.JpKey} by {entry.ReceiverName}.";

        return RedirectToPage(new { GroupBy, StagingFilter, LoadTypeFilter, SearchTerm });
    }

    public async Task<IActionResult> OnPostUpdateStagingAsync(int entryId, string newStagingLocation)
    {
        var entry = await _context.ReceivingLogs.FindAsync(entryId);
        if (entry == null) return NotFound();

        entry.StagingLocation = newStagingLocation;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Shipment {entry.JpKey} relocated to {newStagingLocation}.";

        return RedirectToPage(new { GroupBy, StagingFilter, LoadTypeFilter, SearchTerm });
    }
}
