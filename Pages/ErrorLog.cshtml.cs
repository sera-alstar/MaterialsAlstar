using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MaterialsAlstar.Data;
using MaterialsAlstar.Models;

namespace MaterialsAlstar.Pages;

public class ErrorLogModel : PageModel
{
    private readonly AppDbContext _context;

    public ErrorLogModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string StatusFilter { get; set; } = "all"; // "all", "open", "in_review", "resolved"

    [BindProperty(SupportsGet = true)]
    public string QcFilter { get; set; } = "all"; // "all", "checked", "pending"

    [BindProperty(SupportsGet = true)]
    public string ScmFilter { get; set; } = "all"; // "all", "submitted", "pending"

    [BindProperty(SupportsGet = true)]
    public string ErrorTypeFilter { get; set; } = "all";

    // KPI Metrics
    public int TotalErrorsCount { get; set; }
    public int OpenErrorsCount { get; set; }
    public int OpenStatusCount { get; set; }
    public int InReviewCount { get; set; }
    public int PendingQcCount { get; set; }
    public int AwaitingScmCount { get; set; }
    public int ResolvedErrorsCount { get; set; }

    // List of Errors
    public List<InspectionError> Errors { get; set; } = new();
    public List<string> ErrorTypeOptions { get; set; } = new();

    public async Task OnGetAsync()
    {
        await ReconcileErrorsWithInspectionLinesAsync();

        var allErrors = await _context.InspectionErrors
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        TotalErrorsCount = allErrors.Count;
        OpenErrorsCount = allErrors.Count(e => !e.IsResolved);
        OpenStatusCount = allErrors.Count(e => !e.IsResolved && e.Status != "In Review");
        InReviewCount = allErrors.Count(e => !e.IsResolved && e.Status == "In Review");
        PendingQcCount = allErrors.Count(e => !e.IsQcChecked);
        AwaitingScmCount = allErrors.Count(e => !e.IsSubmittedToScm);
        ResolvedErrorsCount = allErrors.Count(e => e.IsResolved);

        ErrorTypeOptions = allErrors
            .Select(e => e.ErrorType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var query = allErrors.AsQueryable();

        // 1. Status Filter
        if (StatusFilter == "open")
        {
            query = query.Where(e => !e.IsResolved && e.Status != "In Review");
        }
        else if (StatusFilter == "in_review")
        {
            query = query.Where(e => e.Status == "In Review");
        }
        else if (StatusFilter == "resolved")
        {
            query = query.Where(e => e.IsResolved);
        }

        // 2. QC Filter
        if (QcFilter == "checked")
        {
            query = query.Where(e => e.IsQcChecked);
        }
        else if (QcFilter == "pending")
        {
            query = query.Where(e => !e.IsQcChecked);
        }

        // 3. SCM Filter
        if (ScmFilter == "submitted")
        {
            query = query.Where(e => e.IsSubmittedToScm);
        }
        else if (ScmFilter == "pending")
        {
            query = query.Where(e => !e.IsSubmittedToScm);
        }

        // 4. Error Type Filter
        if (!string.IsNullOrWhiteSpace(ErrorTypeFilter) && ErrorTypeFilter != "all")
        {
            query = query.Where(e => e.ErrorType.Equals(ErrorTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        // 5. Search Term Filter
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.JpKey.ToLowerInvariant().Contains(term) ||
                e.VendorName.ToLowerInvariant().Contains(term) ||
                e.PackingSlipNumber.ToLowerInvariant().Contains(term) ||
                e.PoNumber.ToLowerInvariant().Contains(term) ||
                (e.JobNumber ?? "").ToLowerInvariant().Contains(term) ||
                (e.InspectorName ?? "").ToLowerInvariant().Contains(term) ||
                (e.BomId ?? "").ToLowerInvariant().Contains(term) ||
                e.ErrorDescription.ToLowerInvariant().Contains(term) ||
                e.ErrorType.ToLowerInvariant().Contains(term) ||
                (e.ResolutionAction ?? "").ToLowerInvariant().Contains(term) ||
                (e.ResolutionNotes ?? "").ToLowerInvariant().Contains(term)
            );
        }

        Errors = query.ToList();
    }

    public async Task<IActionResult> OnPostResolveErrorAsync(
        int errorId,
        string resolutionAction,
        string resolutionNotes,
        string resolvedBy,
        bool markQcChecked,
        string? qcNotes,
        bool markScmSubmitted,
        DateTime? scmSubmittedAt,
        string? scmNotes)
    {
        var error = await _context.InspectionErrors.FindAsync(errorId);
        if (error == null) return NotFound();

        error.IsResolved = true;
        error.Status = "Resolved";
        error.ResolvedAt = DateTime.Now;
        error.ResolvedBy = string.IsNullOrWhiteSpace(resolvedBy) ? "Aaron Victoria" : resolvedBy.Trim();
        error.ResolutionAction = string.IsNullOrWhiteSpace(resolutionAction) ? "Resolved" : resolutionAction.Trim();
        error.ResolutionNotes = resolutionNotes?.Trim();

        if (markQcChecked)
        {
            error.IsQcChecked = true;
            error.QcCheckedAt ??= DateTime.Now;
            if (!string.IsNullOrWhiteSpace(qcNotes))
            {
                error.QcNotes = qcNotes.Trim();
            }
        }

        if (markScmSubmitted)
        {
            error.IsSubmittedToScm = true;
            error.ScmSubmittedAt = scmSubmittedAt ?? DateTime.Now;
            if (!string.IsNullOrWhiteSpace(scmNotes))
            {
                error.ScmNotes = scmNotes.Trim();
            }
        }

        // Sync with linked ReceivingLogEntry if present
        if (error.ReceivingLogEntryId > 0)
        {
            var entry = await _context.ReceivingLogs.FindAsync(error.ReceivingLogEntryId);
            if (entry != null)
            {
                if (markQcChecked)
                {
                    entry.QcMtrCheck = "Complete";
                    if (!string.IsNullOrWhiteSpace(qcNotes))
                    {
                        entry.QcMtrComments = qcNotes.Trim();
                    }
                }

                if (markScmSubmitted)
                {
                    entry.IsSubmittedToScm = true;
                    entry.ScmSubmittedAt = error.ScmSubmittedAt;
                    if (!string.IsNullOrWhiteSpace(scmNotes))
                    {
                        entry.ScmNotes = scmNotes.Trim();
                    }
                }

                entry.Notes = string.IsNullOrWhiteSpace(entry.Notes)
                    ? $"[Inspection Error Resolved]: {error.ResolutionAction} - {error.ResolutionNotes}"
                    : $"{entry.Notes} | [Inspection Error Resolved]: {error.ResolutionAction}";
            }
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Error for shipment {error.JpKey} (PO {error.PoNumber}) successfully resolved by {error.ResolvedBy}!";

        return RedirectToPage(new { StatusFilter, QcFilter, ScmFilter, ErrorTypeFilter, SearchTerm });
    }

    public async Task<IActionResult> OnPostReopenErrorAsync(int errorId, string? reopenReason)
    {
        var error = await _context.InspectionErrors.FindAsync(errorId);
        if (error == null) return NotFound();

        error.IsResolved = false;
        error.Status = "Open";
        error.ResolvedAt = null;
        error.ResolvedBy = null;

        var note = !string.IsNullOrWhiteSpace(reopenReason) ? reopenReason.Trim() : "Reopened for re-inspection";
        error.ResolutionNotes = string.IsNullOrWhiteSpace(error.ResolutionNotes)
            ? $"[Reopened]: {note}"
            : $"{error.ResolutionNotes} | [Reopened]: {note}";

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Inspection error for shipment {error.JpKey} has been reopened.";

        return RedirectToPage(new { StatusFilter, QcFilter, ScmFilter, ErrorTypeFilter, SearchTerm });
    }

    public async Task<IActionResult> OnPostUpdateScmStatusAsync(int errorId, bool isSubmitted, DateTime? submittedAt, string? notes)
    {
        var error = await _context.InspectionErrors.FindAsync(errorId);
        if (error == null) return NotFound();

        error.IsSubmittedToScm = isSubmitted;
        error.ScmSubmittedAt = isSubmitted ? (submittedAt ?? DateTime.Now) : null;
        error.ScmNotes = notes?.Trim();

        if (error.ReceivingLogEntryId > 0)
        {
            var entry = await _context.ReceivingLogs.FindAsync(error.ReceivingLogEntryId);
            if (entry != null)
            {
                entry.IsSubmittedToScm = isSubmitted;
                entry.ScmSubmittedAt = error.ScmSubmittedAt;
                entry.ScmNotes = error.ScmNotes;
            }
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = isSubmitted
            ? $"SCM submission logged for {error.JpKey} on {error.ScmSubmittedAt:MMM d, yyyy h:mm tt}."
            : $"SCM submission status reset for {error.JpKey}.";

        return RedirectToPage(new { StatusFilter, QcFilter, ScmFilter, ErrorTypeFilter, SearchTerm });
    }

    public async Task<IActionResult> OnPostCreateManualErrorAsync(
        string jpKey,
        string poNumber,
        string? jobNumber,
        string vendorName,
        string packingSlipNumber,
        string? inspectorName,
        string errorType,
        string errorDescription,
        string severity,
        string? affectedLineNumber,
        string? bomId,
        decimal? quantityOrdered,
        decimal? quantityDue,
        decimal? quantityReceived,
        bool isQcChecked,
        bool isSubmittedToScm)
    {
        if (string.IsNullOrWhiteSpace(jpKey) || string.IsNullOrWhiteSpace(poNumber) || string.IsNullOrWhiteSpace(vendorName))
        {
            TempData["WarningMessage"] = "Please fill in all required shipment fields (JP Key, PO#, Vendor).";
            return RedirectToPage();
        }

        var entry = await _context.ReceivingLogs
            .FirstOrDefaultAsync(r => r.JpKey == jpKey.Trim() || r.PoNumber == poNumber.Trim());

        var newErr = new InspectionError
        {
            ReceivingLogEntryId = entry?.Id ?? 0,
            JpKey = jpKey.Trim(),
            JobNumber = string.IsNullOrWhiteSpace(jobNumber) ? (entry?.JobNumber ?? "251078") : jobNumber.Trim(),
            PoNumber = poNumber.Trim(),
            VendorName = vendorName.Trim(),
            PackingSlipNumber = string.IsNullOrWhiteSpace(packingSlipNumber) ? (entry?.PackingSlipNumber ?? "TBD") : packingSlipNumber.Trim(),
            InspectorName = string.IsNullOrWhiteSpace(inspectorName) ? (entry?.ReceiverName ?? "Receiver") : inspectorName.Trim(),
            ArrivalDate = entry?.ArrivalDateTime ?? DateTime.Now,
            DateInspected = entry?.ReceiveEndTime ?? DateTime.Now,
            IsQcChecked = isQcChecked || (entry?.QcMtrCheck == "Complete"),
            QcCheckedAt = isQcChecked ? DateTime.Now : null,
            IsSubmittedToScm = isSubmittedToScm,
            ScmSubmittedAt = isSubmittedToScm ? DateTime.Now : null,
            ErrorType = string.IsNullOrWhiteSpace(errorType) ? "Discrepancy" : errorType.Trim(),
            ErrorDescription = string.IsNullOrWhiteSpace(errorDescription) ? "Inspection discrepancy flagged" : errorDescription.Trim(),
            Severity = string.IsNullOrWhiteSpace(severity) ? "Medium" : severity.Trim(),
            AffectedLineNumber = affectedLineNumber?.Trim(),
            BomId = bomId?.Trim(),
            QuantityOrdered = quantityOrdered,
            QuantityDue = quantityDue,
            QuantityReceived = quantityReceived,
            Status = "Open",
            IsResolved = false,
            CreatedAt = DateTime.Now
        };

        _context.InspectionErrors.Add(newErr);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"New inspection error logged for {newErr.JpKey} ({newErr.ErrorType}).";

        return RedirectToPage(new { StatusFilter = "open" });
    }

    private async Task ReconcileErrorsWithInspectionLinesAsync()
    {
        var allLines = await _context.InspectionLines.ToListAsync();
        var allErrors = await _context.InspectionErrors.ToListAsync();
        var recLogs = await _context.ReceivingLogs.ToDictionaryAsync(r => r.Id);

        bool changed = false;

        // 1. Remove any error tied to a ReceivingLogEntry where no corresponding InspectionLineItem has HasError == true
        foreach (var err in allErrors.Where(e => e.ReceivingLogEntryId > 0).ToList())
        {
            var matchingErrorLine = allLines.FirstOrDefault(l =>
                l.ReceivingLogEntryId == err.ReceivingLogEntryId &&
                l.HasError &&
                (string.IsNullOrWhiteSpace(err.AffectedLineNumber) ||
                 string.Equals(l.PoLineNumber?.Trim(), err.AffectedLineNumber.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (matchingErrorLine == null)
            {
                _context.InspectionErrors.Remove(err);
                changed = true;
            }
        }

        // 2. Ensure every InspectionLineItem with HasError == true has an entry in InspectionErrors
        foreach (var l in allLines.Where(x => x.HasError))
        {
            string lineNum = !string.IsNullOrWhiteSpace(l.PoLineNumber) ? l.PoLineNumber.Trim() : "1.00";
            var existingErr = allErrors.FirstOrDefault(e =>
                e.ReceivingLogEntryId == l.ReceivingLogEntryId &&
                string.Equals(e.AffectedLineNumber?.Trim(), lineNum, StringComparison.OrdinalIgnoreCase));

            if (existingErr == null && recLogs.TryGetValue(l.ReceivingLogEntryId, out var entry))
            {
                bool hasQtyShortage = l.QuantityReceivedThisShipment < l.QuantityDue && l.QuantityDue > 0;
                bool hasQtyOverage = l.QuantityReceivedThisShipment > l.QuantityDue;
                bool hasDescMismatch = !l.IsDescriptionMatched && l.QuantityReceivedThisShipment > 0;

                string errType = hasDescMismatch ? "Description Mismatch" :
                                 hasQtyShortage ? "Quantity Shortage" :
                                 hasQtyOverage ? "Quantity Overage" : "Inspection Error";

                string desc = hasQtyShortage
                    ? $"Quantity shortage on Line {lineNum} ({l.ItemDescription}): expected {l.QuantityDue} {l.UnitOfMeasure}, received {l.QuantityReceivedThisShipment} {l.UnitOfMeasure} ({l.QuantityDue - l.QuantityReceivedThisShipment} short)."
                    : (!string.IsNullOrWhiteSpace(l.Notes)
                        ? $"Error on Line {lineNum} ({l.ItemDescription}): {l.Notes}"
                        : $"Error flagged on Line {lineNum} ({l.ItemDescription}) during receiving inspection.");

                _context.InspectionErrors.Add(new InspectionError
                {
                    ReceivingLogEntryId = entry.Id,
                    JpKey = entry.JpKey,
                    JobNumber = entry.JobNumber ?? "251078",
                    PoNumber = entry.PoNumber,
                    VendorName = entry.VendorName,
                    PackingSlipNumber = entry.PackingSlipNumber,
                    InspectorName = entry.ReceiverName ?? "Receiver",
                    ArrivalDate = entry.ArrivalDateTime,
                    DateInspected = entry.ReceiveEndTime ?? entry.ReceiveStartTime ?? DateTime.Now,
                    IsQcChecked = entry.QcMtrCheck == "Complete",
                    QcNotes = entry.QcMtrComments,
                    IsSubmittedToScm = entry.IsSubmittedToScm,
                    ScmSubmittedAt = entry.ScmSubmittedAt,
                    ErrorType = errType,
                    ErrorDescription = desc,
                    Severity = hasQtyShortage || hasDescMismatch ? "High" : "Medium",
                    AffectedLineNumber = lineNum,
                    BomId = l.BomId,
                    QuantityOrdered = l.QuantityOrdered,
                    QuantityDue = l.QuantityDue,
                    QuantityReceived = l.QuantityReceivedThisShipment,
                    Status = "Open",
                    IsResolved = false,
                    CreatedAt = DateTime.Now
                });
                changed = true;
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync();
        }
    }
}

