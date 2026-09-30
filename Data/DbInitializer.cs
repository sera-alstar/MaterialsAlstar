using MaterialsAlstar.Models;

namespace MaterialsAlstar.Data;

public static class DbInitializer
{
    public static void Initialize(AppDbContext context)
    {
        context.Database.EnsureCreated();

        if (context.Materials.Any())
        {
            return; // Database already seeded
        }

        var materials = new MaterialItem[]
        {
            new() { Name = "Aluminum Sheet 6061-T6", Sku = "AL-6061-001", Category = "Metals", Quantity = 150, Unit = "sheets", UnitCost = 45.50m },
            new() { Name = "Stainless Steel Rod 304", Sku = "SS-304-002", Category = "Metals", Quantity = 85, Unit = "bars", UnitCost = 28.75m },
            new() { Name = "Carbon Fiber Fabric 3K", Sku = "CF-3K-003", Category = "Composites", Quantity = 40, Unit = "rolls", UnitCost = 110.00m },
            new() { Name = "Polycarbonate Panel 1/4 inch", Sku = "PC-CLR-004", Category = "Plastics", Quantity = 60, Unit = "panels", UnitCost = 35.20m },
            new() { Name = "Titanium Hex Bolts M8x30", Sku = "TI-BOLT-005", Category = "Hardware", Quantity = 500, Unit = "pcs", UnitCost = 4.25m }
        };

        context.Materials.AddRange(materials);
        context.SaveChanges();
    }
}
