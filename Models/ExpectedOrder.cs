namespace MaterialsAlstar.Models;

public class ExpectedOrder
{
    public int Id { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string PoNumber { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string PackingSlipNumber { get; set; } = string.Empty;
    public string LoadType { get; set; } = string.Empty;
    public DateTime? OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public bool HasLandedInYard { get; set; }
    public DateTime? ActualArrivalDate { get; set; }
    public string YardStatus { get; set; } = "In Transit"; // "In Transit", "Landed in Yard", "Past Due"
    public string ReceivingStatus { get; set; } = "Not Received"; // "Not Received", "Partially Received", "OS&D / Backorder", "Received"
    public string ClientName { get; set; } = string.Empty;
    public string ProjectManager { get; set; } = string.Empty;
    public string ShipmentNature { get; set; } = string.Empty;
    public bool IsLongLead { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string Unloader { get; set; } = string.Empty;
    public string Receiver { get; set; } = string.Empty;
    public string RecId { get; set; } = string.Empty;
    public string DriveLink { get; set; } = string.Empty;
    public bool IsStructural { get; set; }
    public string JpKey => string.IsNullOrEmpty(JobNumber) ? PoNumber : $"{JobNumber}{PoNumber}";
    public string? BolNumber { get; set; }
    public string? StagingLocation { get; set; }
    public int? PalletCount { get; set; }
    public int? CrateCount { get; set; }
    public int? BoxCount { get; set; }
    public string? PhotoPath { get; set; }
}


