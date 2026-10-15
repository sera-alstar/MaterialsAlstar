using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MaterialsAlstar.Data;
using MaterialsAlstar.Models;

namespace MaterialsAlstar.Pages;

[IgnoreAntiforgeryToken]
public class OrderingLogModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;

    public OrderingLogModel(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
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

    [BindProperty(SupportsGet = true)]
    public string? FilterJpKey { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterVendor { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterPackingSlip { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterDate { get; set; }

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

    // Search state
    public bool IsSearchNotFound { get; set; }
    public bool IsJpSearchActive { get; set; }

    // Filter Options
    public List<string> VendorOptions { get; set; } = new();
    public List<string> LoadTypeOptions { get; set; } = new();
    public List<string> JobOptions { get; set; } = new();

    public async Task OnGetAsync()
    {
        // 1. Determine reference date (defaults to Oct 5, 2026 or Today)
        if (!string.IsNullOrEmpty(ReferenceDateStr) && DateTime.TryParse(ReferenceDateStr, out var parsedRef))
        {
            ReferenceDate = parsedRef.Date;
        }
        else
        {
            ReferenceDate = new DateTime(2026, 10, 5); // Base reference aligned with sample data
        }

        ComputeBusinessDays();

        // 2. Fetch all open material orders (exclude structural steel)
        var allOpenOrders = await _context.ExpectedOrders
            .Where(o => !o.IsStructural && o.ReceivingStatus != "Received")
            .ToListAsync();

        TotalOpenOrders = allOpenOrders.Count;
        LandedInYardCount = allOpenOrders.Count(o => o.HasLandedInYard);

        // Calculate KPI counts
        TotalWindowOrders = allOpenOrders.Count(o =>
            o.ExpectedDate.HasValue &&
            o.ExpectedDate.Value.Date >= YesterdayBusinessDay.Date &&
            o.ExpectedDate.Value.Date <= TomorrowBusinessDay.Date);

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

        // 3. ViewMode & Text Filter Logic
        IEnumerable<ExpectedOrder> filtered;

        // If a SearchTerm or FilterJpKey is provided, search across ALL open orders (even outside the 3-day window)
        // because an arriving shipment might have been expected anytime.
        if (!string.IsNullOrWhiteSpace(SearchTerm) || !string.IsNullOrWhiteSpace(FilterJpKey))
        {
            IsJpSearchActive = true;
            filtered = allOpenOrders;
        }
        else
        {
            // Apply ViewMode date filtering
            filtered = allOpenOrders;

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
                        filtered = filtered.Where(o =>
                            o.ExpectedDate.HasValue &&
                            o.ExpectedDate.Value.Date >= NextWeekMonday.Date &&
                            o.ExpectedDate.Value.Date <= NextWeekFriday.Date);
                    }
                    else
                    {
                        filtered = filtered.Where(o =>
                            o.ExpectedDate.HasValue &&
                            o.ExpectedDate.Value.Date > TomorrowBusinessDay.Date &&
                            o.ExpectedDate.Value.Date <= NextWeekFriday.Date);
                    }
                    break;

                case "all":
                    break;

                case "3day":
                default:
                    filtered = filtered.Where(o =>
                        o.ExpectedDate.HasValue &&
                        o.ExpectedDate.Value.Date >= YesterdayBusinessDay.Date &&
                        o.ExpectedDate.Value.Date <= TomorrowBusinessDay.Date);
                    break;
            }
        }

        // Apply specific column text filters
        if (!string.IsNullOrWhiteSpace(FilterJpKey))
        {
            var cleanKey = FilterJpKey.Trim().ToLowerInvariant().Replace("-", "").Replace(" ", "");
            filtered = filtered.Where(o =>
                o.JpKey.ToLowerInvariant().Replace("-", "").Replace(" ", "").Contains(cleanKey) ||
                $"{o.JobNumber}{o.PoNumber}".ToLowerInvariant().Replace("-", "").Replace(" ", "").Contains(cleanKey) ||
                o.JobNumber.ToLowerInvariant().Contains(cleanKey));
        }

        if (!string.IsNullOrWhiteSpace(FilterVendor))
        {
            var vTerm = FilterVendor.Trim().ToLowerInvariant();
            filtered = filtered.Where(o => o.VendorName != null && o.VendorName.ToLowerInvariant().Contains(vTerm));
        }

        if (!string.IsNullOrWhiteSpace(FilterPackingSlip))
        {
            var psTerm = FilterPackingSlip.Trim().ToLowerInvariant();
            filtered = filtered.Where(o => o.PackingSlipNumber != null && o.PackingSlipNumber.ToLowerInvariant().Contains(psTerm));
        }

        if (!string.IsNullOrWhiteSpace(FilterDate))
        {
            var dTerm = FilterDate.Trim().ToLowerInvariant();
            filtered = filtered.Where(o =>
                (o.ExpectedDate.HasValue && (
                    o.ExpectedDate.Value.ToString("yyyy-MM-dd").Contains(dTerm) ||
                    o.ExpectedDate.Value.ToString("MMM d, yyyy").ToLowerInvariant().Contains(dTerm) ||
                    o.ExpectedDate.Value.ToString("MM/dd/yyyy").Contains(dTerm) ||
                    o.ExpectedDate.Value.ToString("MM-dd-yyyy").Contains(dTerm) ||
                    o.ExpectedDate.Value.ToString("MMMM d, yyyy").ToLowerInvariant().Contains(dTerm) ||
                    o.ExpectedDate.Value.ToString("ddd").ToLowerInvariant().Contains(dTerm) ||
                    o.ExpectedDate.Value.ToString("yyyy").Contains(dTerm)
                )) ||
                (o.ActualArrivalDate.HasValue && (
                    o.ActualArrivalDate.Value.ToString("yyyy-MM-dd").Contains(dTerm) ||
                    o.ActualArrivalDate.Value.ToString("MMM d, yyyy").ToLowerInvariant().Contains(dTerm) ||
                    o.ActualArrivalDate.Value.ToString("MM/dd/yyyy").Contains(dTerm)
                ))
            );
        }

        // Keep the original search fields looking through the filtered results from the other fields
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim().ToLowerInvariant();
            var termClean = term.Replace("-", "").Replace(" ", "");

            filtered = filtered.Where(o =>
                o.JpKey.ToLowerInvariant().Contains(termClean) ||
                $"{o.JobNumber}{o.PoNumber}".ToLowerInvariant().Contains(termClean) ||
                o.JobNumber.ToLowerInvariant().Contains(term) ||
                o.PoNumber.ToLowerInvariant().Contains(term) ||
                o.VendorName.ToLowerInvariant().Contains(term) ||
                o.PackingSlipNumber.ToLowerInvariant().Contains(term) ||
                o.ClientName.ToLowerInvariant().Contains(term) ||
                o.LoadType.ToLowerInvariant().Contains(term));

            if (!filtered.Any())
            {
                IsSearchNotFound = true;
            }
        }

        // 4. Apply additional interactive filters
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

    public async Task<IActionResult> OnPostAcceptShipmentAsync(
        int expectedOrderId,
        string packingSlipNumber,
        string bolNumber,
        int palletCount,
        int crateCount,
        int boxCount,
        string selectedLoadType,
        string stagingLocation,
        IFormFile? shipmentPhoto,
        string? checkInNotes)
    {
        var order = await _context.ExpectedOrders.FindAsync(expectedOrderId);
        if (order == null)
        {
            return NotFound();
        }

        // Save optional photo if uploaded
        string? photoPath = null;
        if (shipmentPhoto != null && shipmentPhoto.Length > 0)
        {
            photoPath = await SaveUploadedPhotoAsync(shipmentPhoto, order.JpKey);
        }

        // Update ExpectedOrder status
        order.HasLandedInYard = true;
        order.ActualArrivalDate = DateTime.Now;
        order.YardStatus = "Landed in Yard";
        order.ReceivingStatus = "Pending Inspection";
        order.PackingSlipNumber = string.IsNullOrWhiteSpace(order.PackingSlipNumber)
            ? packingSlipNumber
            : (order.PackingSlipNumber.Contains(packingSlipNumber) ? order.PackingSlipNumber : $"{order.PackingSlipNumber}, {packingSlipNumber}");
        order.BolNumber = bolNumber;
        order.StagingLocation = stagingLocation;
        order.PalletCount = palletCount;
        order.CrateCount = crateCount;
        order.BoxCount = boxCount;
        if (!string.IsNullOrEmpty(photoPath))
        {
            order.PhotoPath = photoPath;
        }

        // Create new entry in Receiving Log
        var receivingEntry = new ReceivingLogEntry
        {
            JpKey = order.JpKey,
            JobNumber = order.JobNumber,
            PoNumber = order.PoNumber,
            VendorName = order.VendorName,
            PackingSlipNumber = packingSlipNumber,
            BolNumber = bolNumber,
            PalletCount = palletCount,
            CrateCount = crateCount,
            BoxCount = boxCount,
            LoadType = string.IsNullOrWhiteSpace(selectedLoadType) ? order.LoadType : selectedLoadType,
            StagingLocation = stagingLocation,
            ArrivalDateTime = DateTime.Now,
            PhotoPath = photoPath,
            Status = "Landed - Pending Inspection",
            IsAdminVerified = true, // Expected from SCM, automatically verified
            ExpectedOrderId = order.Id,
            ClientName = order.ClientName,
            ProjectManager = order.ProjectManager,
            Notes = checkInNotes
        };

        _context.ReceivingLogs.Add(receivingEntry);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Shipment for {order.JpKey} ({order.VendorName}) successfully accepted and checked into yard at {stagingLocation}! Logged into Receiving Log.";
        TempData["NewReceivingId"] = receivingEntry.Id;

        return RedirectToPage(new { ViewMode, ReferenceDateStr });
    }

    public async Task<IActionResult> OnPostCreateNewShipmentAsync(
        string vendorName,
        string poNumber,
        string? jobNumber,
        string packingSlipNumber,
        string bolNumber,
        int palletCount,
        int crateCount,
        int boxCount,
        string loadType,
        string stagingLocation,
        IFormFile? shipmentPhoto,
        string? notes)
    {
        string jpKey = string.IsNullOrWhiteSpace(jobNumber)
            ? $"UNKNOWN-{poNumber.Trim()}"
            : $"{jobNumber.Trim()}{poNumber.Trim()}";

        string? photoPath = null;
        if (shipmentPhoto != null && shipmentPhoto.Length > 0)
        {
            photoPath = await SaveUploadedPhotoAsync(shipmentPhoto, jpKey);
        }

        var entry = new ReceivingLogEntry
        {
            JpKey = jpKey,
            JobNumber = string.IsNullOrWhiteSpace(jobNumber) ? null : jobNumber.Trim(),
            PoNumber = poNumber.Trim(),
            VendorName = vendorName.Trim(),
            PackingSlipNumber = packingSlipNumber.Trim(),
            BolNumber = bolNumber.Trim(),
            PalletCount = palletCount,
            CrateCount = crateCount,
            BoxCount = boxCount,
            LoadType = loadType,
            StagingLocation = stagingLocation,
            ArrivalDateTime = DateTime.Now,
            PhotoPath = photoPath,
            Status = "Pending Admin Verification",
            IsAdminVerified = false, // Must be verified by materials admin
            Notes = notes
        };

        _context.ReceivingLogs.Add(entry);
        await _context.SaveChangesAsync();

        TempData["WarningMessage"] = $"Unscheduled shipment for PO {poNumber} ({vendorName}) accepted at yard ({stagingLocation}) and submitted for Materials Admin verification before appearing in receiving queue.";
        TempData["NewReceivingId"] = entry.Id;

        return RedirectToPage(new { ViewMode, ReferenceDateStr });
    }

    private async Task<string> SaveUploadedPhotoAsync(IFormFile photo, string identifier)
    {
        try
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadsDir = Path.Combine(webRoot, "uploads", "shipments");
            if (!Directory.Exists(uploadsDir))
            {
                Directory.CreateDirectory(uploadsDir);
            }

            var ext = Path.GetExtension(photo.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var safeId = string.Join("_", identifier.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"shipment_{safeId}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString()[..6]}{ext}";
            var fullPath = Path.Combine(uploadsDir, fileName);

            using var stream = new FileStream(fullPath, FileMode.Create);
            await photo.CopyToAsync(stream);

            return $"/uploads/shipments/{fileName}";
        }
        catch
        {
            return string.Empty;
        }
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
