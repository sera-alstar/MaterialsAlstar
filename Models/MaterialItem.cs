namespace MaterialsAlstar.Models;

public class MaterialItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal UnitCost { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
