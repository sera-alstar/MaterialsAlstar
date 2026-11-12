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
            var importService = new MaterialsAlstar.Services.ShipmentImportService();

            var pendingApprovalEntry = context.ReceivingLogs.FirstOrDefault(r => r.JpKey == "251078C0270" || r.PoNumber == "0270");
            if (pendingApprovalEntry != null)
            {
                var lines = importService.LoadVistaBomForPo("0270", "251078C0270", pendingApprovalEntry.Id);
                if (lines.Count > 0)
                {
                    foreach (var l in lines) l.Id = 0;
                    lines[0].QuantityReceivedThisShipment = 80;
                    lines[0].HasError = true;
                    lines[0].Notes = "Short 20 EA on shipment (received 80 of 100 EA)";
                    context.InspectionLines.AddRange(lines);
                    pendingApprovalEntry.InspectionDraftJson = System.Text.Json.JsonSerializer.Serialize(lines);
                }
            }

            var inspectingEntry = context.ReceivingLogs.FirstOrDefault(r => r.JpKey == "251078C0438" || r.PoNumber == "0438");
            if (inspectingEntry != null)
            {
                var lines = importService.LoadVistaBomForPo("0438", "251078C0438", inspectingEntry.Id);
                if (lines.Count > 0)
                {
                    foreach (var l in lines) l.Id = 0;
                    lines[0].QuantityReceivedThisShipment = 1;
                    lines[0].HasError = true;
                    lines[0].Notes = "Short 1 EA on shipment (received 1 of 2 EA)";
                    context.InspectionLines.AddRange(lines);
                    inspectingEntry.InspectionDraftJson = System.Text.Json.JsonSerializer.Serialize(lines);
                }
            }

            context.SaveChanges();
        }

        if (!context.InspectionErrors.Any())
        {
            var errorLines = context.InspectionLines.Where(l => l.HasError).ToList();
            var recLogsById = context.ReceivingLogs.ToDictionary(r => r.Id);

            foreach (var line in errorLines)
            {
                if (!recLogsById.TryGetValue(line.ReceivingLogEntryId, out var entry))
                    continue;

                bool hasQtyShortage = line.QuantityReceivedThisShipment < line.QuantityDue && line.QuantityDue > 0;
                bool hasQtyOverage = line.QuantityReceivedThisShipment > line.QuantityDue;
                bool hasDescMismatch = !line.IsDescriptionMatched && line.QuantityReceivedThisShipment > 0;

                string errType = hasDescMismatch ? "Description Mismatch" :
                                 hasQtyShortage ? "Quantity Shortage" :
                                 hasQtyOverage ? "Quantity Overage" : "Inspection Error";

                string desc = hasQtyShortage
                    ? $"Quantity shortage on Line {line.PoLineNumber} ({line.ItemDescription}): expected {line.QuantityDue} {line.UnitOfMeasure}, received {line.QuantityReceivedThisShipment} {line.UnitOfMeasure} ({line.QuantityDue - line.QuantityReceivedThisShipment} short)."
                    : (!string.IsNullOrWhiteSpace(line.Notes)
                        ? $"Error on Line {line.PoLineNumber} ({line.ItemDescription}): {line.Notes}"
                        : $"Error flagged on Line {line.PoLineNumber} ({line.ItemDescription}) during receiving inspection.");

                context.InspectionErrors.Add(new InspectionError
                {
                    ReceivingLogEntryId = entry.Id,
                    JpKey = entry.JpKey,
                    JobNumber = entry.JobNumber ?? "251078",
                    PoNumber = entry.PoNumber,
                    VendorName = entry.VendorName,
                    PackingSlipNumber = entry.PackingSlipNumber,
                    InspectorName = entry.ReceiverName ?? "Receiver",
                    ArrivalDate = entry.ArrivalDateTime,
                    DateInspected = entry.ReceiveEndTime ?? entry.ReceiveStartTime ?? entry.ArrivalDateTime.AddHours(1),
                    IsQcChecked = entry.QcMtrCheck == "Complete",
                    QcNotes = entry.QcMtrComments,
                    IsSubmittedToScm = entry.IsSubmittedToScm,
                    ScmSubmittedAt = entry.ScmSubmittedAt,
                    ErrorType = errType,
                    ErrorDescription = desc,
                    Severity = hasQtyShortage || hasDescMismatch ? "High" : "Medium",
                    AffectedLineNumber = line.PoLineNumber,
                    BomId = line.BomId,
                    QuantityOrdered = line.QuantityOrdered,
                    QuantityDue = line.QuantityDue,
                    QuantityReceived = line.QuantityReceivedThisShipment,
                    Status = "Open",
                    IsResolved = false,
                    CreatedAt = DateTime.Now.AddHours(-2)
                });
            }

            context.SaveChanges();
        }

        if (!context.BillsOfLading.Any())
        {
            var persistedBols = BillOfLading.LoadPersistedBols();
            if (persistedBols != null && persistedBols.Count > 0)
            {
                foreach (var b in persistedBols) b.Id = 0;
                context.BillsOfLading.AddRange(persistedBols);
                context.SaveChanges();
            }
            else
            {
                string yy = (DateTime.Now.Year % 100).ToString("00");
                var seedBols = new List<BillOfLading>
                {
                    new BillOfLading
                    {
                        BolNumber = $"AO{yy}.BOL001",
                        JobNumber = "261073C",
                        ShipFrom = "Alstar Group - Calgary Fab Shop\n8700 Venture Ave\nCalgary, AB T3S 0A2",
                        ShipTo = "Cenovus",
                        BatchId = 101,
                        Priorities = "11, 12, 13, 14",
                        Type = "Shiploose",
                        Kitter = "Kris",
                        PalletCount = 1,
                        CrateCount = 1,
                        DateCreated = DateTime.Now.AddDays(-5),
                        Notes = "Initial shiploose valves, gaskets, and directional anchors kitted for Area 100."
                    },
                    new BillOfLading
                    {
                        BolNumber = $"AO{yy}.BOL002",
                        JobNumber = "251078C",
                        ShipFrom = "Alstar Group - Calgary Fab Shop\n8700 Venture Ave\nCalgary, AB T3S 0A2",
                        ShipTo = "Cenovus",
                        BatchId = 102,
                        Priorities = "3, 7-10",
                        Type = "Assembly",
                        Kitter = "Kris",
                        PalletCount = 2,
                        CrateCount = 2,
                        DateCreated = DateTime.Now.AddDays(-3),
                        Notes = "Prefabricated pipe spool assemblies and pipe shoes secured on flatbed skids."
                    },
                    new BillOfLading
                    {
                        BolNumber = $"AO{yy}.BOL003",
                        JobNumber = "251078",
                        ShipFrom = "Alstar Group - Calgary Fab Shop\n8700 Venture Ave\nCalgary, AB T3S 0A2",
                        ShipTo = "Cenovus",
                        BatchId = 104,
                        Priorities = "11-14, 18",
                        Type = "Shiploose",
                        Kitter = "Aaron Victoria",
                        PalletCount = 4,
                        CrateCount = 0,
                        DateCreated = DateTime.Now.AddDays(-1),
                        Notes = "Flange kits and spiral wound gaskets shrink-wrapped and tagged by priority."
                    }
                };

                context.BillsOfLading.AddRange(seedBols);
                context.SaveChanges();
                BillOfLading.SavePersistedBols(context.BillsOfLading.ToList());
            }
        }
    }
}
