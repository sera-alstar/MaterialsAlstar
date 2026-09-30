namespace MaterialsAlstar.Models;

public class MaterialItem
{
    public int Id { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string PoNumber { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BomId { get; set; } = string.Empty;
    public decimal QuantityShipped { get; set; }
    public decimal QuantityReceived { get; set; }
    public string Unit { get; set; } = "EA";
    public string HeatNumber { get; set; } = string.Empty;
    public string BinLocation { get; set; } = string.Empty;
    public string ReceivedBy { get; set; } = string.Empty;
    public DateTime? ReceivedDate { get; set; }
    public string Status { get; set; } = "Received";
}
