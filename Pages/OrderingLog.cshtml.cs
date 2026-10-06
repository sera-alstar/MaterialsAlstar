using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MaterialsAlstar.Data;
using MaterialsAlstar.Models;

namespace MaterialsAlstar.Pages;

public class OrderingLogModel : PageModel
{
    private readonly AppDbContext _context;

    public OrderingLogModel(AppDbContext context)
    {
        _context = context;
    }

    // Filters
    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "3day"; // "3day", "pastdue", "future", "all"

    [BindProperty(SupportsGet = true)]
    public string? ReferenceDateStr { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? VendorFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? LoadTypeFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? YardStatusFilter { get; set; }

    // Date properties
    public DateTime ReferenceDate { get; set; }
    public DateTime YesterdayBusinessDay { get; set; }
    public DateTime TomorrowBusinessDay { get; set; }
    public bool IsThursdayOrFriday => ReferenceDate.DayOfWeek == DayOfWeek.Thursday || ReferenceDate.DayOfWeek == DayOfWeek.Friday;
    public DateTime CurrentWeekFriday { get; set; }
    public DateTime NextWeekMonday { get; set; }
    public DateTime NextWeekFriday { get; set; }

    // Orders and KPIs
    public List<ExpectedOrder> Orders { get; set; } = new();
    public int TotalWindowOrders { get; set; }
    public int LandedInYardCount { get; set; }
    public int PastDueCount { get; set; }
    public int NextWeekCount { get; set; }
    public int TotalOpenOrders { get; set; }

    // Filter Options
    public List<string> VendorOptions { get; set; } = new();
    public List<string> LoadTypeOptions { get; set; } = new();
    public List<string> JobOptions { get; set; } = new();

    public async Task OnGetAsync()
    {
        // 1. Determine reference date (defaults to today or system date)
        if (!string.IsNullOrEmpty(ReferenceDateStr) && DateTime.TryParse(ReferenceDateStr, out var parsedDate))
        {
            ReferenceDate = parsedDate.Date;
        }
        else
        {
            ReferenceDate = DateTime.Today;
        }

        ComputeBusinessDays();

        // 2. Base query: Exclude structural steel (handled by structural steel team)
        var baseQuery = _context.ExpectedOrders
            .Where(o => !o.IsStructural && o.ReceivingStatus != "Received");

        // Compute KPIs across all non-structural open orders
        var allOpenOrders = await baseQuery.ToListAsync();
        TotalOpenOrders = allOpenOrders.Count;

        TotalWindowOrders = allOpenOrders.Count(o =>
            o.ExpectedDate.HasValue &&
            o.ExpectedDate.Value.Date >= YesterdayBusinessDay.Date &&
            o.ExpectedDate.Value.Date <= TomorrowBusinessDay.Date);

        LandedInYardCount = allOpenOrders.Count(o => o.HasLandedInYard);

        PastDueCount = allOpenOrders.Count(o =>
            o.ExpectedDate.HasValue &&
            o.ExpectedDate.Value.Date < YesterdayBusinessDay.Date &&
            !o.HasLandedInYard);

        NextWeekCount = allOpenOrders.Count(o =>
            o.ExpectedDate.HasValue &&
            o.ExpectedDate.Value.Date >= NextWeekMonday.Date &&
            o.ExpectedDate.Value.Date <= NextWeekFriday.Date);

        // Populate dropdown options
        VendorOptions = allOpenOrders
            .Select(o => o.VendorName)
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        LoadTypeOptions = allOpenOrders
            .Select(o => o.LoadType)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        JobOptions = allOpenOrders
            .Select(o => o.JobNumber)
            .Where(j => !string.IsNullOrEmpty(j))
            .Distinct()
            .OrderBy(j => j)
            .ToList();

        // 3. Apply ViewMode date filtering
        IEnumerable<ExpectedOrder> filtered = allOpenOrders;

        switch (ViewMode.ToLowerInvariant())
        {
            case "pastdue":
                filtered = filtered.Where(o =>
                    o.ExpectedDate.HasValue &&
                    o.ExpectedDate.Value.Date < YesterdayBusinessDay.Date);
                break;

            case "future":
                if (IsThursdayOrFriday)
                {
                    // If Thursday or Friday, focus on Next Week
                    filtered = filtered.Where(o =>
                        o.ExpectedDate.HasValue &&
                        o.ExpectedDate.Value.Date >= NextWeekMonday.Date &&
                        o.ExpectedDate.Value.Date <= NextWeekFriday.Date);
                }
                else
                {
                    // Earlier in week: rest of week + next week
                    filtered = filtered.Where(o =>
                        o.ExpectedDate.HasValue &&
                        o.ExpectedDate.Value.Date > TomorrowBusinessDay.Date &&
                        o.ExpectedDate.Value.Date <= NextWeekFriday.Date);
                }
                break;

            case "all":
                // All open unreceived orders
                break;

            case "3day":
            default:
                filtered = filtered.Where(o =>
                    o.ExpectedDate.HasValue &&
                    o.ExpectedDate.Value.Date >= YesterdayBusinessDay.Date &&
                    o.ExpectedDate.Value.Date <= TomorrowBusinessDay.Date);
                break;
        }

        // 4. Apply additional interactive filters
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim().ToLowerInvariant();
            filtered = filtered.Where(o =>
                o.JobNumber.ToLowerInvariant().Contains(term) ||
                o.PoNumber.ToLowerInvariant().Contains(term) ||
                o.VendorName.ToLowerInvariant().Contains(term) ||
                o.PackingSlipNumber.ToLowerInvariant().Contains(term) ||
                o.ClientName.ToLowerInvariant().Contains(term) ||
                o.LoadType.ToLowerInvariant().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(VendorFilter))
        {
            filtered = filtered.Where(o => o.VendorName.Equals(VendorFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(LoadTypeFilter))
        {
            filtered = filtered.Where(o => o.LoadType.Equals(LoadTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(YardStatusFilter))
        {
            if (YardStatusFilter.Equals("Landed", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(o => o.HasLandedInYard);
            }
            else if (YardStatusFilter.Equals("InTransit", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(o => !o.HasLandedInYard);
            }
        }

        // 5. Order results: Landed in yard first, then by expected date, job, and PO
        Orders = filtered
            .OrderByDescending(o => o.HasLandedInYard)
            .ThenBy(o => o.ExpectedDate)
            .ThenBy(o => o.JobNumber)
            .ThenBy(o => o.PoNumber)
            .ToList();
    }

    private void ComputeBusinessDays()
    {
        // Business days: Monday to Friday
        // Yesterday
        YesterdayBusinessDay = ReferenceDate.DayOfWeek switch
        {
            DayOfWeek.Monday => ReferenceDate.AddDays(-3),
            DayOfWeek.Sunday => ReferenceDate.AddDays(-2),
            DayOfWeek.Saturday => ReferenceDate.AddDays(-1),
            _ => ReferenceDate.AddDays(-1)
        };

        // Tomorrow
        TomorrowBusinessDay = ReferenceDate.DayOfWeek switch
        {
            DayOfWeek.Friday => ReferenceDate.AddDays(3),
            DayOfWeek.Saturday => ReferenceDate.AddDays(2),
            DayOfWeek.Sunday => ReferenceDate.AddDays(1),
            _ => ReferenceDate.AddDays(1)
        };

        // Current week's Friday
        int daysUntilFriday = ((int)DayOfWeek.Friday - (int)ReferenceDate.DayOfWeek + 7) % 7;
        CurrentWeekFriday = ReferenceDate.AddDays(daysUntilFriday);

        // Next week Monday to Friday
        int daysUntilNextMonday = ((int)DayOfWeek.Monday - (int)ReferenceDate.DayOfWeek + 7) % 7;
        if (daysUntilNextMonday == 0) daysUntilNextMonday = 7;
        NextWeekMonday = ReferenceDate.AddDays(daysUntilNextMonday);
        NextWeekFriday = NextWeekMonday.AddDays(4);
    }

    public string GetRelativeDateBadge(DateTime? expectedDate)
    {
        if (!expectedDate.HasValue) return "Date Pending";

        var d = expectedDate.Value.Date;
        if (d == ReferenceDate.Date) return "Today";
        if (d == YesterdayBusinessDay.Date) return "Yesterday";
        if (d == TomorrowBusinessDay.Date) return "Tomorrow";

        if (d < YesterdayBusinessDay.Date)
        {
            int diff = (YesterdayBusinessDay.Date - d).Days;
            return $"{diff}d past window";
        }

        int futureDiff = (d - ReferenceDate.Date).Days;
        return $"In {futureDiff}d";
    }

    public string GetLoadTypeBadgeClass(string loadType)
    {
        var lower = loadType.ToLowerInvariant();
        if (lower.Contains("valve")) return "badge-valves";
        if (lower.Contains("flange") || lower.Contains("fitting")) return "badge-flanges";
        if (lower.Contains("pipe")) return "badge-pipe";
        if (lower.Contains("sng") || lower.Contains("bolt") || lower.Contains("fastener")) return "badge-sng";
        if (lower.Contains("support")) return "badge-supports";
        if (lower.Contains("electrical")) return "badge-electrical";
        if (lower.Contains("instrument")) return "badge-instrumentation";
        return "badge-default";
    }
}
