namespace MaterialsAlstar.Models;

public class InspectionError
{
    public int Id { get; set; }
    public int ReceivingLogEntryId { get; set; }

    // Core Shipment Information
    public string JpKey { get; set; } = string.Empty;
    public string? JobNumber { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string PackingSlipNumber { get; set; } = string.Empty;
    public string? InspectorName { get; set; }
    public DateTime ArrivalDate { get; set; } = DateTime.Now;
    public DateTime? DateInspected { get; set; }

    // QC Check Status: If QC has checked the shipment
    public bool IsQcChecked { get; set; } = false;
    public DateTime? QcCheckedAt { get; set; }
    public string? QcNotes { get; set; }

    // SCM Submission Status: If/when the shipment has been submitted to SCM
    public bool IsSubmittedToScm { get; set; } = false;
    public DateTime? ScmSubmittedAt { get; set; }
    public string? ScmNotes { get; set; }

    // Error & Discrepancy Details
    public string ErrorType { get; set; } = "Discrepancy"; // e.g. "Quantity Shortage", "Quantity Overage", "Damaged Goods", "Description Mismatch", "Missing MTR / Paperwork", "Spec / Quality Issue"
    public string ErrorDescription { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium"; // "High", "Medium", "Low"
    public string? AffectedLineNumber { get; set; }
    public string? BomId { get; set; }
    public decimal? QuantityOrdered { get; set; }
    public decimal? QuantityDue { get; set; }
    public decimal? QuantityReceived { get; set; }

    // Resolution Details
    public string Status { get; set; } = "Open"; // "Open", "In Review", "Resolved"
    public bool IsResolved { get; set; } = false;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionAction { get; set; } // e.g. "Vendor Credit Requested", "Replacement PO Issued", "Accepted on Deviation Waiver", "Quarantined", "SCM Adjusted"
    public string? ResolutionNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

