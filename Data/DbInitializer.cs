using MaterialsAlstar.Models;

namespace MaterialsAlstar.Data;

public static class DbInitializer
{
    public static void Initialize(AppDbContext context)
    {
        context.Database.EnsureCreated();

        if (!context.Materials.Any())
        {
            var importService = new MaterialsAlstar.Services.ShipmentImportService();
            var materials = importService.LoadMaterialItems();
            if (materials.Count > 0)
            {
                context.Materials.AddRange(materials);
                context.SaveChanges();
            }
        }

        if (!context.ExpectedOrders.Any())
        {
            var importService = new MaterialsAlstar.Services.ShipmentImportService();
            var expectedOrders = importService.LoadExpectedOrders();
            if (expectedOrders.Count > 0)
            {
                context.ExpectedOrders.AddRange(expectedOrders);
                context.SaveChanges();
            }
        }

        if (!context.ReceivingLogs.Any())
        {
            var importService = new MaterialsAlstar.Services.ShipmentImportService();
            var recLogs = importService.LoadReceivingLogs();
            if (recLogs.Count > 0)
            {
                context.ReceivingLogs.AddRange(recLogs);
                context.SaveChanges();
            }
        }

        if (!context.InspectionLines.Any())
        {
            var pendingApprovalEntry = context.ReceivingLogs.FirstOrDefault(r => r.JpKey == "251078C0270" || r.PoNumber == "0270");
            if (pendingApprovalEntry != null)
            {
                var importService = new MaterialsAlstar.Services.ShipmentImportService();
                var lines = importService.LoadVistaBomForPo("0270", "251078C0270", pendingApprovalEntry.Id);
                if (lines.Count > 0)
                {
                    context.InspectionLines.AddRange(lines);
                    pendingApprovalEntry.InspectionDraftJson = System.Text.Json.JsonSerializer.Serialize(lines);
                    context.SaveChanges();
                }
            }
        }
    }
}


