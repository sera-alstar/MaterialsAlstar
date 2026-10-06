namespace MaterialsAlstar.Models;

public class ReceivingLogEntry
{
    public int Id { get; set; }
    public string JpKey { get; set; } = string.Empty; // e.g. 251078C0123
    public string? JobNumber { get; set; } // Optional: client-supplied shipments may not have job identified yet
    public string PoNumber { get; set; } = string.Empty; // Required
    public string VendorName { get; set; } = string.Empty; // Required
    public string PackingSlipNumber { get; set; } = string.Empty; // Required
    public string BolNumber { get; set; } = string.Empty; // Required
    public int PalletCount { get; set; }
    public int CrateCount { get; set; }
    public int BoxCount { get; set; }
    public string PackageSummary => $"{PalletCount} Pallet{(PalletCount == 1 ? "" : "s")}, {CrateCount} Crate{(CrateCount == 1 ? "" : "s")}, {BoxCount} Box{(BoxCount == 1 ? "" : "es")}";
    public string LoadType { get; set; } = string.Empty; // e.g. SNG, Pipe, Valves, Flanges & Fittings
    public string StagingLocation { get; set; } = "Walkway"; // e.g. Walkway, Bay 1, Bay 2, Yard Row A
    public DateTime ArrivalDateTime { get; set; } = DateTime.Now;
    public string? PhotoPath { get; set; } // Relative URL to uploaded photo / camera image
    public string Status { get; set; } = "Landed - Pending Inspection"; // "Pending Admin Verification", "Landed - Pending Inspection", "In Inspection", "Complete"
    public bool IsAdminVerified { get; set; } = true; // false for ad-hoc entries until admin reviews
    public string? AdminVerifiedBy { get; set; }
    public DateTime? AdminVerifiedAt { get; set; }
    public int? ExpectedOrderId { get; set; } // Link to source ExpectedOrder if accepted from ordering log
    public string? ClientName { get; set; }
    public string? ProjectManager { get; set; }
    public string? Notes { get; set; }
    public string? ReceiverName { get; set; }
    public string? Unloader { get; set; }
    public string? MrrNumber { get; set; } // Material Receiving Report #
    public string? ClientPo { get; set; }
    public string? FreeIssue { get; set; } // Y / N
    public string? MaterialsMtrCheck { get; set; } // Complete, N/A, Pending
    public string? MaterialsMtrComments { get; set; }
    public string? QcMtrCheck { get; set; } // Complete, N/A, Pending
    public string? QcMtrComments { get; set; }
    public string? DocumentLink { get; set; } // Link to Drive folder / documents
    public DateTime? ReceivingCompletedAt { get; set; }
    public string? RecId { get; set; } // e.g. REC-0001
}
