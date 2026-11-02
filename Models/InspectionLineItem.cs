namespace MaterialsAlstar.Models;

public class InspectionLineItem
{
    public int Id { get; set; }
    public int ReceivingLogEntryId { get; set; }
    public string JpKey { get; set; } = string.Empty;
    public string PoNumber { get; set; } = string.Empty;
    public string PoLineNumber { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public string BomId { get; set; } = string.Empty;
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityPreviouslyReceived { get; set; }
    public decimal QuantityDue { get; set; }
    public string UnitOfMeasure { get; set; } = "EA";

    // True if fully received previously (Qty Ordered <= Qty Previously Received)
    public bool IsFullyReceived => QuantityPreviouslyReceived >= QuantityOrdered && QuantityOrdered > 0;

    // Inspection fields
    public bool IsDescriptionMatched { get; set; }
    public decimal QuantityReceivedThisShipment { get; set; }
    public string HeatNumber { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string StorageLocation { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

