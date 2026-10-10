using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MaterialsAlstar.Data;
using MaterialsAlstar.Models;
using MaterialsAlstar.Services;

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
    public string? StatusFilter { get; set; } = "active"; // "active" (landed/inspecting/pending approval), "all", "completed"

    // KPIs & Metrics
    public int TotalLandedCount { get; set; }
    public int TotalAdminPendingCount { get; set; }
    public int TotalAdminInspectionCount { get; set; }
    public int TotalInInspectionCount { get; set; }
    public int TotalPalletsCount { get; set; }
    public int TotalBoxesCount { get; set; }

    // Admin Verification Queues
    public List<ReceivingLogEntry> AdminPendingShipments { get; set; } = new();
    public List<ReceivingLogEntry> AdminInspectionApprovalQueue { get; set; } = new();

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

    public class InspectionLineDto
    {
        public int Id { get; set; }
        public int ReceivingLogEntryId { get; set; }
        public string? JpKey { get; set; }
        public string? PoNumber { get; set; }
        public string? PoLineNumber { get; set; }
        public string BomId { get; set; } = string.Empty;
        public string ItemDescription { get; set; } = string.Empty;
        public decimal QuantityOrdered { get; set; }
        public decimal QuantityPreviouslyReceived { get; set; }
        public decimal QuantityDue { get; set; }
        public decimal QuantityReceivedThisShipment { get; set; }
        public string UnitOfMeasure { get; set; } = "EA";
        public bool IsFullyReceived => QuantityPreviouslyReceived >= QuantityOrdered && QuantityOrdered > 0;
        public bool IsDescriptionMatched { get; set; }
        public string? HeatNumber { get; set; }
        public string? SerialNumber { get; set; }
        public string? StorageLocation { get; set; }
        public string? Notes { get; set; }
        public bool IsAdHoc { get; set; }
    }

    public class InspectionDraftRequest
    {
        public int EntryId { get; set; }
        public string? ReceiverName { get; set; }
        public string? InspectionNotes { get; set; }
        public List<InspectionLineDto> Lines { get; set; } = new();
    }

    public class InspectionSubmitRequest
    {
        public int EntryId { get; set; }
        public string? ReceiverName { get; set; }
        public string? InspectionNotes { get; set; }
        public List<InspectionLineDto> Lines { get; set; } = new();
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
        // 1. Load Admin Check-In Verification Queue (entries that need Materials Admin PO/Job approval)
        AdminPendingShipments = await _context.ReceivingLogs
            .Where(r => !r.IsAdminVerified)
            .OrderByDescending(r => r.ArrivalDateTime)
            .ToListAsync();

        TotalAdminPendingCount = AdminPendingShipments.Count;

        // 2. Load Admin Inspection Review Queue (inspected shipments awaiting admin audit)
        AdminInspectionApprovalQueue = await _context.ReceivingLogs
            .Where(r => r.Status == "Pending Admin Approval")
            .OrderByDescending(r => r.ReceiveEndTime ?? r.ArrivalDateTime)
            .ToListAsync();

        TotalAdminInspectionCount = AdminInspectionApprovalQueue.Count;

        // 3. Load verified landed entries
        var query = _context.ReceivingLogs
            .Where(r => r.IsAdminVerified)
            .AsQueryable();

        // Status Filter
        if (StatusFilter == "active")
        {
            query = query.Where(r => r.Status != "Complete" && r.Status != "Approved - Pending QA");
        }
        else if (StatusFilter == "completed")
        {
            query = query.Where(r => r.Status == "Complete" || r.Status == "Approved - Pending QA");
        }

        var allVerified = await query.ToListAsync();

        TotalLandedCount = allVerified.Count;
        TotalInInspectionCount = allVerified.Count(r => r.Status == "In Inspection" || r.Status == "Inspecting");
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

    // -------------------------------------------------------------
    // Inspection Modal API & Workflow Handlers
    // -------------------------------------------------------------

    public async Task<IActionResult> OnGetInspectionDataAsync(int entryId, string? receiverName)
    {
        var entry = await _context.ReceivingLogs.FindAsync(entryId);
        if (entry == null) return NotFound(new { error = "Shipment not found" });

        // If newly inspecting, transition status to Inspecting and stamp ReceiveStartTime
        if (entry.Status == "Landed - Pending Inspection" || entry.Status == "In Yard")
        {
            entry.Status = "Inspecting";
            entry.ReceiveStartTime ??= DateTime.Now;
        }

        if (!string.IsNullOrWhiteSpace(receiverName) && (string.IsNullOrWhiteSpace(entry.ReceiverName) || entry.ReceiverName == "Receiver"))
        {
            entry.ReceiverName = receiverName.Trim();
        }

        await _context.SaveChangesAsync();

        // 1. Resolve Lines: DB table -> Draft JSON -> VISTA BOM CSV
        List<InspectionLineDto> lines = new();

        var dbLines = await _context.InspectionLines
            .Where(l => l.ReceivingLogEntryId == entryId)
            .OrderBy(l => l.Id)
            .ToListAsync();

        if (dbLines.Count > 0)
        {
            lines = dbLines.Select(l => new InspectionLineDto
            {
                Id = l.Id,
                ReceivingLogEntryId = l.ReceivingLogEntryId,
                JpKey = l.JpKey,
                PoNumber = l.PoNumber,
                PoLineNumber = l.PoLineNumber,
                BomId = l.BomId,
                ItemDescription = l.ItemDescription,
                QuantityOrdered = l.QuantityOrdered,
                QuantityPreviouslyReceived = l.QuantityPreviouslyReceived,
                QuantityDue = l.QuantityDue,
                QuantityReceivedThisShipment = l.QuantityReceivedThisShipment,
                UnitOfMeasure = l.UnitOfMeasure,
                IsDescriptionMatched = l.IsDescriptionMatched,
                HeatNumber = l.HeatNumber,
                SerialNumber = l.SerialNumber,
                StorageLocation = l.StorageLocation,
                Notes = l.Notes,
                IsAdHoc = false
            }).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(entry.InspectionDraftJson))
        {
            try
            {
                var draft = JsonSerializer.Deserialize<List<InspectionLineDto>>(entry.InspectionDraftJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (draft != null && draft.Count > 0)
                {
                    lines = draft;
                }
            }
            catch { }
        }

        if (lines.Count == 0)
        {
            var importService = new ShipmentImportService();
            var bomLines = importService.LoadVistaBomForPo(entry.PoNumber, entry.JpKey, entry.Id);
            lines = bomLines.Select(l => new InspectionLineDto
            {
                Id = l.Id,
                ReceivingLogEntryId = l.ReceivingLogEntryId,
                JpKey = l.JpKey,
                PoNumber = l.PoNumber,
                PoLineNumber = l.PoLineNumber,
                BomId = l.BomId,
                ItemDescription = l.ItemDescription,
                QuantityOrdered = l.QuantityOrdered,
                QuantityPreviouslyReceived = l.QuantityPreviouslyReceived,
                QuantityDue = l.QuantityDue,
                QuantityReceivedThisShipment = l.QuantityReceivedThisShipment,
                UnitOfMeasure = l.UnitOfMeasure,
                IsDescriptionMatched = l.IsDescriptionMatched,
                HeatNumber = l.HeatNumber,
                SerialNumber = l.SerialNumber,
                StorageLocation = string.IsNullOrWhiteSpace(l.StorageLocation) ? entry.StagingLocation : l.StorageLocation,
                Notes = l.Notes,
                IsAdHoc = false
            }).ToList();
        }

        // 2. Load Autocomplete Suggestions from Job 251078 Inventory
        var materials = await _context.Materials
            .Where(m => m.JobNumber == "251078" || string.IsNullOrEmpty(m.JobNumber) || m.JobNumber == entry.JobNumber)
            .ToListAsync();

        var heatNumbers = materials
            .Select(m => m.HeatNumber)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .SelectMany(h => h.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(h => h)
            .ToList();

        var serialNumbers = materials
            .Select(m => m.SerialNumber)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => s.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        var binLocations = materials
            .Select(m => m.BinLocation)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b)
            .ToList();

        var standardYardLocations = new[] { "4 D 2 2", "4D22", "Walkway", "Bay 1", "Bay 2", "Yard Row A", "Yard Row B", "Quarantine" };
        foreach (var def in standardYardLocations)
        {
            if (!binLocations.Contains(def, StringComparer.OrdinalIgnoreCase))
                binLocations.Add(def);
        }

        // Lookup dictionary mapping BOM ID / Description to previously logged HT#, Serial#, Bin
        var catalog = materials
            .GroupBy(m => (m.BomId ?? "").Trim().ToLowerInvariant())
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    heatNumbers = g.Select(x => x.HeatNumber).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(),
                    serialNumbers = g.Select(x => x.SerialNumber).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList(),
                    binLocations = g.Select(x => x.BinLocation).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList()
                }
            );

        return new JsonResult(new
        {
            id = entry.Id,
            jpKey = entry.JpKey,
            jobNumber = entry.JobNumber ?? "251078",
            poNumber = entry.PoNumber,
            vendorName = entry.VendorName,
            packingSlipNumber = entry.PackingSlipNumber,
            bolNumber = entry.BolNumber,
            packageSummary = entry.PackageSummary,
            stagingLocation = entry.StagingLocation,
            loadType = entry.LoadType,
            arrivalDateTime = entry.ArrivalDateTime.ToString("MMM d, yyyy h:mm tt"),
            receiveStartTime = entry.ReceiveStartTime?.ToString("MMM d, yyyy h:mm tt") ?? DateTime.Now.ToString("MMM d, yyyy h:mm tt"),
            receiveEndTime = entry.ReceiveEndTime?.ToString("MMM d, yyyy h:mm tt") ?? "",
            receiverName = entry.ReceiverName ?? "Receiver",
            status = entry.Status,
            inspectionNotes = entry.InspectionNotes ?? "",
            lines,
            autocomplete = new
            {
                heatNumbers,
                serialNumbers,
                binLocations,
                catalog
            }
        });
    }

    public async Task<IActionResult> OnPostSaveInspectionDraftAsync([FromBody] InspectionDraftRequest request)
    {
        if (request == null || request.EntryId <= 0) return BadRequest();

        var entry = await _context.ReceivingLogs.FindAsync(request.EntryId);
        if (entry == null) return NotFound();

        if (entry.Status == "Landed - Pending Inspection" || entry.Status == "In Yard")
        {
            entry.Status = "Inspecting";
            entry.ReceiveStartTime ??= DateTime.Now;
        }

        if (!string.IsNullOrWhiteSpace(request.ReceiverName))
        {
            entry.ReceiverName = request.ReceiverName.Trim();
        }

        entry.InspectionNotes = request.InspectionNotes;
        entry.InspectionDraftJson = JsonSerializer.Serialize(request.Lines);

        await _context.SaveChangesAsync();

        return new JsonResult(new
        {
            success = true,
            savedAt = DateTime.Now.ToString("h:mm:ss tt")
        });
    }

    public async Task<IActionResult> OnPostSubmitInspectionAsync([FromBody] InspectionSubmitRequest request)
    {
        if (request == null || request.EntryId <= 0) return BadRequest();

        var entry = await _context.ReceivingLogs.FindAsync(request.EntryId);
        if (entry == null) return NotFound();

        entry.Status = "Pending Admin Approval";
        entry.ReceiveStartTime ??= DateTime.Now;
        entry.ReceiveEndTime = DateTime.Now;

        if (!string.IsNullOrWhiteSpace(request.ReceiverName))
        {
            entry.ReceiverName = request.ReceiverName.Trim();
        }

        entry.InspectionNotes = request.InspectionNotes;
        entry.InspectionDraftJson = JsonSerializer.Serialize(request.Lines);

        // Persist into InspectionLines table
        var existing = _context.InspectionLines.Where(l => l.ReceivingLogEntryId == entry.Id);
        _context.InspectionLines.RemoveRange(existing);

        int lineIdx = 1;
        foreach (var l in request.Lines)
        {
            _context.InspectionLines.Add(new InspectionLineItem
            {
                ReceivingLogEntryId = entry.Id,
                JpKey = entry.JpKey,
                PoNumber = entry.PoNumber,
                PoLineNumber = !string.IsNullOrWhiteSpace(l.PoLineNumber) ? l.PoLineNumber : lineIdx.ToString("0.00"),
                BomId = l.BomId ?? "",
                ItemDescription = l.ItemDescription ?? "",
                QuantityOrdered = l.QuantityOrdered,
                QuantityPreviouslyReceived = l.QuantityPreviouslyReceived,
                QuantityDue = l.QuantityDue,
                UnitOfMeasure = string.IsNullOrWhiteSpace(l.UnitOfMeasure) ? "EA" : l.UnitOfMeasure,
                IsDescriptionMatched = l.IsDescriptionMatched,
                QuantityReceivedThisShipment = l.QuantityReceivedThisShipment,
                HeatNumber = l.HeatNumber ?? "",
                SerialNumber = l.SerialNumber ?? "",
                StorageLocation = l.StorageLocation ?? "",
                Notes = l.Notes ?? ""
            });
            lineIdx++;
        }

        await _context.SaveChangesAsync();

        return new JsonResult(new
        {
            success = true,
            message = $"Shipment {entry.JpKey} submitted for Materials Admin approval.",
            redirectUrl = Url.Page("/ReceivingLog")
        });
    }

    public async Task<IActionResult> OnPostApproveInspectionAsync(int entryId, string? adminNotes)
    {
        var entry = await _context.ReceivingLogs.FindAsync(entryId);
        if (entry == null) return NotFound();

        entry.Status = "Approved - Pending QA";
        entry.ReceivingCompletedAt = DateTime.Now;

        if (!string.IsNullOrWhiteSpace(adminNotes))
        {
            entry.Notes = string.IsNullOrWhiteSpace(entry.Notes)
                ? $"[Admin Audit Approved]: {adminNotes}"
                : $"{entry.Notes} | [Admin Audit Approved]: {adminNotes}";
        }

        var lines = await _context.InspectionLines
            .Where(l => l.ReceivingLogEntryId == entryId)
            .ToListAsync();

        if (lines.Count == 0 && !string.IsNullOrWhiteSpace(entry.InspectionDraftJson))
        {
            try
            {
                var dtoList = JsonSerializer.Deserialize<List<InspectionLineDto>>(entry.InspectionDraftJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (dtoList != null)
                {
                    lines = dtoList.Select(d => new InspectionLineItem
                    {
                        ReceivingLogEntryId = entryId,
                        JpKey = entry.JpKey,
                        PoNumber = entry.PoNumber,
                        BomId = d.BomId,
                        ItemDescription = d.ItemDescription,
                        QuantityOrdered = d.QuantityOrdered,
                        QuantityPreviouslyReceived = d.QuantityPreviouslyReceived,
                        QuantityDue = d.QuantityDue,
                        UnitOfMeasure = d.UnitOfMeasure,
                        IsDescriptionMatched = d.IsDescriptionMatched,
                        QuantityReceivedThisShipment = d.QuantityReceivedThisShipment,
                        HeatNumber = d.HeatNumber ?? "",
                        SerialNumber = d.SerialNumber ?? "",
                        StorageLocation = d.StorageLocation ?? "",
                        Notes = d.Notes ?? ""
                    }).ToList();
                }
            }
            catch { }
        }

        int addedCount = 0;
        foreach (var line in lines.Where(l => l.QuantityReceivedThisShipment > 0))
        {
            _context.Materials.Add(new MaterialItem
            {
                JobNumber = !string.IsNullOrWhiteSpace(entry.JobNumber) ? entry.JobNumber : "251078",
                PoNumber = entry.PoNumber,
                ReceiverId = !string.IsNullOrWhiteSpace(entry.RecId) ? entry.RecId : $"REC-{entry.Id:D4}",
                VendorName = entry.VendorName,
                Description = line.ItemDescription,
                BomId = line.BomId,
                QuantityShipped = line.QuantityOrdered,
                QuantityReceived = line.QuantityReceivedThisShipment,
                Unit = string.IsNullOrWhiteSpace(line.UnitOfMeasure) ? "EA" : line.UnitOfMeasure,
                HeatNumber = line.HeatNumber ?? "",
                SerialNumber = line.SerialNumber ?? "",
                BinLocation = line.StorageLocation ?? "",
                ReceivedBy = entry.ReceiverName ?? "Receiver",
                ReceivedDate = DateTime.Now,
                Status = "Received - Pending QA"
            });
            addedCount++;

            line.QuantityPreviouslyReceived += line.QuantityReceivedThisShipment;
            line.QuantityDue = Math.Max(0, line.QuantityOrdered - line.QuantityPreviouslyReceived);
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Shipment {entry.JpKey} (PO {entry.PoNumber} - {entry.VendorName}) approved by Materials Admin! {addedCount} line items posted to Job 251078 Materials Inventory.";

        return RedirectToPage(new { GroupBy, StagingFilter, LoadTypeFilter, SearchTerm });
    }

    public async Task<IActionResult> OnPostReturnInspectionAsync(int entryId, string returnReason)
    {
        var entry = await _context.ReceivingLogs.FindAsync(entryId);
        if (entry == null) return NotFound();

        entry.Status = "Inspecting";
        entry.InspectionNotes = $"[Returned by Admin]: {returnReason} | Previous: {entry.InspectionNotes}";

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Shipment {entry.JpKey} returned to inspector ({entry.ReceiverName}) for revision: {returnReason}";

        return RedirectToPage(new { GroupBy, StagingFilter, LoadTypeFilter, SearchTerm });
    }
}
