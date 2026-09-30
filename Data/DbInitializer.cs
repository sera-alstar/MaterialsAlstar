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
            new()
            {
                JobNumber = "241143",
                PoNumber = "241143C0446",
                ReceiverId = "C0446",
                VendorName = "Trans Am Piping Products Ltd",
                Description = "2\" BW Cap XXS A234-WPB",
                BomId = "CAP2-XX234",
                QuantityShipped = 4,
                QuantityReceived = 4,
                Unit = "EA",
                HeatNumber = "EV630",
                BinLocation = "4E21",
                ReceivedBy = "L. Walsh",
                ReceivedDate = new DateTime(2026, 3, 20),
                Status = "Complete"
            },
            new()
            {
                JobNumber = "241143",
                PoNumber = "241143C0472",
                ReceiverId = "C0472",
                VendorName = "DNOW Canada ULC",
                Description = "1 1/2\" 3,000# SW Coupling A350-LF2 CL.1",
                BomId = "CPLG1G3MSW350",
                QuantityShipped = 1,
                QuantityReceived = 1,
                Unit = "EA",
                HeatNumber = "D170",
                BinLocation = "4D22",
                ReceivedBy = "L. Walsh",
                ReceivedDate = new DateTime(2026, 4, 18),
                Status = "Complete"
            },
            new()
            {
                JobNumber = "241143",
                PoNumber = "241143C0472",
                ReceiverId = "C0472",
                VendorName = "DNOW Canada ULC",
                Description = "1 1/2\" x 1/2\" PBE ECC SWAGE XS A420-WPL6",
                BomId = "SWE1G-GXHPB420",
                QuantityShipped = 6,
                QuantityReceived = 6,
                Unit = "EA",
                HeatNumber = "K5292",
                BinLocation = "4C32",
                ReceivedBy = "L. Walsh",
                ReceivedDate = new DateTime(2026, 4, 18),
                Status = "Complete"
            },
            new()
            {
                JobNumber = "241143",
                PoNumber = "241143C0472",
                ReceiverId = "C0472",
                VendorName = "DNOW Canada ULC",
                Description = "1/2\" 900# RF SW Flange XS A350-LF2 CL.1",
                BomId = "FLG-G9RFSWXH350",
                QuantityShipped = 2,
                QuantityReceived = 2,
                Unit = "EA",
                HeatNumber = "TLC0039846",
                BinLocation = "4F21",
                ReceivedBy = "L. Walsh",
                ReceivedDate = new DateTime(2026, 4, 18),
                Status = "Complete"
            },
            new()
            {
                JobNumber = "241143",
                PoNumber = "241143C0479",
                ReceiverId = "C0479",
                VendorName = "Trans Am Piping Products Ltd",
                Description = "1-1/2\" x 2\" 3000# Sockolet A350-LF2 CL. 1",
                BomId = "SOL-1G01-2G-3M350",
                QuantityShipped = 4,
                QuantityReceived = 4,
                Unit = "EA",
                HeatNumber = "AGJI",
                BinLocation = "4D21",
                ReceivedBy = "BW / LW",
                ReceivedDate = new DateTime(2026, 4, 21),
                Status = "Complete"
            },
            new()
            {
                JobNumber = "241143",
                PoNumber = "241143C0450",
                ReceiverId = "C0450",
                VendorName = "Comco Pipe & Supply Company",
                Description = "1\" GATE VLV SB CL800 SW x TE BB A350-LF2 CL1 TR 12",
                BomId = "CNRL-028",
                QuantityShipped = 2,
                QuantityReceived = 2,
                Unit = "EA",
                HeatNumber = "B20",
                BinLocation = "3B32",
                ReceivedBy = "K.P.",
                ReceivedDate = new DateTime(2026, 3, 31),
                Status = "Complete"
            },
            new()
            {
                JobNumber = "251024",
                PoNumber = "251024C0146",
                ReceiverId = "C0146",
                VendorName = "Apex Distribution Inc",
                Description = "1\" GATE VALVE SW CL 800 A105N NACE TR. 12",
                BomId = "CONOCO-084",
                QuantityShipped = 8,
                QuantityReceived = 8,
                Unit = "EA",
                HeatNumber = "YBA",
                BinLocation = "1F21",
                ReceivedBy = "L. Walsh",
                ReceivedDate = new DateTime(2026, 1, 5),
                Status = "Complete"
            }
        };

        context.Materials.AddRange(materials);
        context.SaveChanges();
    }
}
