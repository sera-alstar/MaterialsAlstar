using System.Globalization;
using System.Text;
using MaterialsAlstar.Models;

namespace MaterialsAlstar.Services;

public class ShipmentImportService
{
    private readonly string _importsDirectory;

    public ShipmentImportService(string? importsDirectory = null)
    {
        if (!string.IsNullOrEmpty(importsDirectory) && Directory.Exists(importsDirectory))
        {
            _importsDirectory = importsDirectory;
        }
        else
        {
            var current = Directory.GetCurrentDirectory();
            var path1 = Path.Combine(current, "Data", "Imports");
            var path2 = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data", "Imports");
            var path3 = Path.Combine(AppContext.BaseDirectory, "Data", "Imports");

            if (Directory.Exists(path1)) _importsDirectory = Path.GetFullPath(path1);
            else if (Directory.Exists(path2)) _importsDirectory = Path.GetFullPath(path2);
            else if (Directory.Exists(path3)) _importsDirectory = Path.GetFullPath(path3);
            else _importsDirectory = path1;
        }
    }

    public List<ExpectedOrder> LoadExpectedOrders()
    {
        var orders = new List<ExpectedOrder>();
        var expFile = Path.Combine(_importsDirectory, "MML - Expected Receiving.csv");
        var recLogFile = Path.Combine(_importsDirectory, "MML - RecLog2.csv");
        var activeJobsFile = Path.Combine(_importsDirectory, "MTS - Active Jobs.csv");
        var unrecFile = Path.Combine(_importsDirectory, "251078 MMT - Unreceived Report.csv");

        var clientLookup = LoadActiveJobs(activeJobsFile);
        var recLogLookup = LoadRecLogLookup(recLogFile);

        if (File.Exists(expFile))
        {
            orders = ParseExpectedReceivingCsv(expFile, recLogLookup, clientLookup);
        }

        // Merge unreceived report items specifically for Job 251078
        MergeUnreceivedReport(orders, unrecFile, clientLookup);

        if (orders.Count == 0)
        {
            orders = GetFallbackSeedOrders();
        }

        return orders;
    }

    public List<ReceivingLogEntry> LoadReceivingLogs()
    {
        var entries = new List<ReceivingLogEntry>();
        var recLogFile = Path.Combine(_importsDirectory, "MML - RecLog2.csv");
        var activeJobsFile = Path.Combine(_importsDirectory, "MTS - Active Jobs.csv");

        var clientLookup = LoadActiveJobs(activeJobsFile);
        int idCounter = 1;

        if (File.Exists(recLogFile))
        {
            try
            {
                using var reader = new StreamReader(recLogFile, Encoding.UTF8);
                var recordsEnum = ReadCsvRecords(reader).GetEnumerator();
                if (recordsEnum.MoveNext())
                {
                    var headers = recordsEnum.Current;
                    int jobIdx = headers.FindIndex(h => CleanHeader(h).Equals("Job", StringComparison.OrdinalIgnoreCase));
                    int poIdx = headers.FindIndex(h => CleanHeader(h).Equals("PO", StringComparison.OrdinalIgnoreCase));
                    int vendorIdx = headers.FindIndex(h => CleanHeader(h).Equals("Vendor", StringComparison.OrdinalIgnoreCase));
                    int plIdx = headers.FindIndex(h => CleanHeader(h).Equals("Packing List", StringComparison.OrdinalIgnoreCase));
                    int loadTypeIdx = headers.FindIndex(h => CleanHeader(h).Equals("Load Type", StringComparison.OrdinalIgnoreCase));
                    int actualIdx = headers.FindIndex(h => CleanHeader(h).Equals("Actual Arrival", StringComparison.OrdinalIgnoreCase));
                    int recIdIdx = headers.FindIndex(h => CleanHeader(h).Equals("Rec ID", StringComparison.OrdinalIgnoreCase));
                    int receiverIdx = headers.FindIndex(h => CleanHeader(h).Equals("Receiver", StringComparison.OrdinalIgnoreCase));
                    int clientIdx = headers.FindIndex(h => CleanHeader(h).Contains("Client") && !CleanHeader(h).Contains("PO"));
                    int pmIdx = headers.FindIndex(h => CleanHeader(h).Contains("Project Manager"));
                    int unloaderIdx = headers.FindIndex(h => CleanHeader(h).Equals("Unloader", StringComparison.OrdinalIgnoreCase));
                    int mrrIdx = headers.FindIndex(h => CleanHeader(h).Equals("MRR #", StringComparison.OrdinalIgnoreCase));
                    int clientPoIdx = headers.FindIndex(h => CleanHeader(h).Equals("Client PO", StringComparison.OrdinalIgnoreCase));
                    int freeIssueIdx = headers.FindIndex(h => CleanHeader(h).Equals("Free Issue", StringComparison.OrdinalIgnoreCase));
                    int linkIdx = headers.FindIndex(h => CleanHeader(h).Equals("Link", StringComparison.OrdinalIgnoreCase));
                    int matMtrCheckIdx = headers.FindIndex(h => CleanHeader(h).Equals("Materials MTR Check", StringComparison.OrdinalIgnoreCase));
                    int matMtrCommIdx = headers.FindIndex(h => CleanHeader(h).Equals("Materials MTR Comments", StringComparison.OrdinalIgnoreCase));
                    int qcMtrCheckIdx = headers.FindIndex(h => CleanHeader(h).Equals("QC MTR Check", StringComparison.OrdinalIgnoreCase));
                    int qcMtrCommIdx = headers.FindIndex(h => CleanHeader(h).Equals("QC MTR Comments", StringComparison.OrdinalIgnoreCase));

                    while (recordsEnum.MoveNext())
                    {
                        var cols = recordsEnum.Current;
                        if (jobIdx < 0 || poIdx < 0 || cols.Count <= Math.Max(jobIdx, poIdx)) continue;

                        var job = cols[jobIdx].Trim();
                        var po = cols[poIdx].Trim();
                        if (string.IsNullOrEmpty(job) || string.IsNullOrEmpty(po)) continue;

                        // ONLY reflect 251078 job data
                        if (!job.StartsWith("251078", StringComparison.OrdinalIgnoreCase)) continue;

                        var vendor = vendorIdx >= 0 && cols.Count > vendorIdx ? cols[vendorIdx].Trim() : "";
                        var pl = plIdx >= 0 && cols.Count > plIdx ? cols[plIdx].Trim() : "";
                        var loadType = loadTypeIdx >= 0 && cols.Count > loadTypeIdx ? cols[loadTypeIdx].Trim() : "";
                        var actualStr = actualIdx >= 0 && cols.Count > actualIdx ? cols[actualIdx].Trim() : "";
                        var recId = recIdIdx >= 0 && cols.Count > recIdIdx ? cols[recIdIdx].Trim() : "";
                        var receiver = receiverIdx >= 0 && cols.Count > receiverIdx ? cols[receiverIdx].Trim() : "";
                        var clientVal = clientIdx >= 0 && cols.Count > clientIdx ? cols[clientIdx].Trim() : "";
                        var pmVal = pmIdx >= 0 && cols.Count > pmIdx ? cols[pmIdx].Trim() : "";
                        var unloaderVal = unloaderIdx >= 0 && cols.Count > unloaderIdx ? cols[unloaderIdx].Trim() : "";
                        var mrrVal = mrrIdx >= 0 && cols.Count > mrrIdx ? cols[mrrIdx].Trim() : "";
                        var clientPoVal = clientPoIdx >= 0 && cols.Count > clientPoIdx ? cols[clientPoIdx].Trim() : "";
                        var freeIssueVal = freeIssueIdx >= 0 && cols.Count > freeIssueIdx ? cols[freeIssueIdx].Trim() : "";
                        var linkVal = linkIdx >= 0 && cols.Count > linkIdx ? cols[linkIdx].Trim() : "";
                        var matMtrCheckVal = matMtrCheckIdx >= 0 && cols.Count > matMtrCheckIdx ? cols[matMtrCheckIdx].Trim() : "";
                        var matMtrCommVal = matMtrCommIdx >= 0 && cols.Count > matMtrCommIdx ? cols[matMtrCommIdx].Trim() : "";
                        var qcMtrCheckVal = qcMtrCheckIdx >= 0 && cols.Count > qcMtrCheckIdx ? cols[qcMtrCheckIdx].Trim() : "";
                        var qcMtrCommVal = qcMtrCommIdx >= 0 && cols.Count > qcMtrCommIdx ? cols[qcMtrCommIdx].Trim() : "";

                        loadType = StandardizeLoadType(loadType, "", vendor);
                        if (IsStructuralSteel(loadType, vendor, "")) continue;

                        var arrivalDate = ParseDate(actualStr);
                        if (!arrivalDate.HasValue) continue;

                        // Focus on 2026 data
                        if (arrivalDate.Value.Year < 2026) continue;

                        var client = !string.IsNullOrEmpty(clientVal) ? clientVal : "";
                        if (string.IsNullOrEmpty(client) && clientLookup.TryGetValue(job, out var c)) client = c;
                        if (string.IsNullOrEmpty(client)) client = "CNRL";

                        var staging = AssignStagingLocation(loadType);

                        // Package breakdown based on load type
                        int pallets = 1;
                        int crates = 0;
                        int boxes = 0;
                        if (loadType.Contains("SNG") || loadType.Contains("Bolt")) { pallets = 1; boxes = 4; }
                        else if (loadType.Contains("Pipe")) { pallets = 1; crates = 0; boxes = 0; }
                        else if (loadType.Contains("Valve")) { pallets = 1; crates = 1; boxes = 0; }
                        else if (loadType.Contains("Flange") || loadType.Contains("Fitting")) { pallets = 2; boxes = 2; }
                        else if (loadType.Contains("Support")) { pallets = 1; crates = 2; boxes = 0; }
                        else if (loadType.Contains("Instrumentation") || loadType.Contains("Electrical")) { pallets = 0; crates = 1; boxes = 3; }

                        // Realistic active status: September 20 to October 2026 are active in yard
                        bool isRecent = arrivalDate.Value >= new DateTime(2026, 9, 20);
                        string status = isRecent
                            ? (!string.IsNullOrEmpty(receiver) ? "In Inspection" : "Landed - Pending Inspection")
                            : "Complete";

                        entries.Add(new ReceivingLogEntry
                        {
                            Id = idCounter++,
                            JpKey = $"{job}{po}",
                            JobNumber = job,
                            PoNumber = po,
                            VendorName = vendor,
                            PackingSlipNumber = string.IsNullOrWhiteSpace(pl) ? $"PS-{po}" : pl,
                            BolNumber = $"BOL-{po}",
                            PalletCount = pallets,
                            CrateCount = crates,
                            BoxCount = boxes,
                            LoadType = loadType,
                            StagingLocation = staging,
                            ArrivalDateTime = arrivalDate.Value,
                            Status = status,
                            IsAdminVerified = true,
                            ClientName = client,
                            ProjectManager = pmVal,
                            ReceiverName = receiver,
                            Unloader = unloaderVal,
                            MrrNumber = mrrVal,
                            ClientPo = clientPoVal,
                            FreeIssue = freeIssueVal,
                            DocumentLink = linkVal,
                            MaterialsMtrCheck = matMtrCheckVal,
                            MaterialsMtrComments = matMtrCommVal,
                            QcMtrCheck = qcMtrCheckVal,
                            QcMtrComments = qcMtrCommVal,
                            RecId = recId
                        });
                    }
                }
            }
            catch { }
        }

        // 4 Active Shipments for CNRL Job 251078 across each stage of physical receiving pipeline
        entries.Add(new ReceivingLogEntry
        {
            Id = idCounter++,
            JpKey = "251078C0435",
            JobNumber = "251078",
            PoNumber = "0435",
            VendorName = "Comco Pipe and Supply Company",
            PackingSlipNumber = "PS-1368291",
            BolNumber = "BOL-CP-9941",
            PalletCount = 1,
            CrateCount = 1,
            BoxCount = 2,
            LoadType = "Flanges & Fittings",
            StagingLocation = "Walkway",
            ArrivalDateTime = new DateTime(2026, 10, 5, 8, 30, 0),
            Status = "Landed - Pending Inspection",
            IsAdminVerified = true,
            ClientName = "CNRL",
            ProjectManager = "Ryan Kallis",
            Notes = "Base supports, reducers, tees & plugs landed in yard. Ready for physical inspection."
        });

        entries.Add(new ReceivingLogEntry
        {
            Id = idCounter++,
            JpKey = "251078C0438",
            JobNumber = "251078",
            PoNumber = "0438",
            VendorName = "Precision Fabrication (2020) Inc",
            PackingSlipNumber = "PS-PF-4412",
            BolNumber = "BOL-PF-8831",
            PalletCount = 1,
            CrateCount = 2,
            BoxCount = 0,
            LoadType = "Supports",
            StagingLocation = "Bay 2 - Fabrication Staging",
            ArrivalDateTime = new DateTime(2026, 10, 5, 9, 0, 0),
            Status = "Inspecting",
            ReceiverName = "Kris",
            ReceiveStartTime = new DateTime(2026, 10, 10, 9, 15, 0),
            IsAdminVerified = true,
            ClientName = "CNRL",
            ProjectManager = "Ryan Kallis",
            Notes = "6\" pipe shoes. Physical inspection currently in progress by Kris."
        });

        entries.Add(new ReceivingLogEntry
        {
            Id = idCounter++,
            JpKey = "251078C0433",
            JobNumber = "251078",
            PoNumber = "0433",
            VendorName = "Comco Pipe and Supply Company",
            PackingSlipNumber = "PS-1359102",
            BolNumber = "BOL-CP-8812",
            PalletCount = 2,
            CrateCount = 1,
            BoxCount = 1,
            LoadType = "Valves",
            StagingLocation = "Bay 1 - Receiving Floor",
            ArrivalDateTime = new DateTime(2026, 10, 6, 10, 15, 0),
            Status = "Landed - Pending Inspection",
            IsAdminVerified = true,
            ClientName = "CNRL",
            ProjectManager = "Ryan Kallis",
            Notes = "Gate valves (1500# and 800# SW x TE). Includes 1 fully received line to test hide/show."
        });

        entries.Add(new ReceivingLogEntry
        {
            Id = idCounter++,
            JpKey = "251078C0270",
            JobNumber = "251078",
            PoNumber = "0270",
            VendorName = "Calgary Fasteners",
            PackingSlipNumber = "260904-02",
            BolNumber = "BOL-CF-2609",
            PalletCount = 1,
            CrateCount = 0,
            BoxCount = 4,
            LoadType = "Studs, Nuts, Gaskets (SNG / Bolt-Up)",
            StagingLocation = "Walkway",
            ArrivalDateTime = new DateTime(2026, 10, 9, 11, 30, 0),
            Status = "Pending Admin Approval",
            ReceiverName = "Brandon",
            ReceiveStartTime = new DateTime(2026, 10, 9, 14, 0, 0),
            ReceiveEndTime = new DateTime(2026, 10, 9, 15, 15, 0),
            IsAdminVerified = true,
            ClientName = "CNRL",
            ProjectManager = "Ryan Kallis",
            InspectionNotes = "B7 stud bolts and spiral wound gaskets inspected and counted. Good condition, MTRs attached. Staged in Bin 4D22.",
            Notes = "Physical receiving inspection complete by Brandon. Awaiting Materials Admin review & approval into inventory."
        });

        return entries;
    }

    private static string AssignStagingLocation(string loadType)
    {
        var l = loadType.ToLowerInvariant();
        if (l.Contains("pipe")) return "Yard Row A - Pipe Racks";
        if (l.Contains("valve")) return "Bay 1 - Receiving Floor";
        if (l.Contains("flange") || l.Contains("fitting")) return "Bay 1 - Receiving Floor";
        if (l.Contains("sng") || l.Contains("bolt")) return "Walkway";
        if (l.Contains("support")) return "Bay 2 - Fabrication Staging";
        if (l.Contains("instrument") || l.Contains("electrical")) return "Quarantine / Inspection Pad";
        return "Walkway";
    }


    private Dictionary<string, string> LoadActiveJobs(string filePath)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Default mapping for Job 251078
        dict["251078"] = "CNRL";
        dict["251078C"] = "CNRL";

        if (!File.Exists(filePath)) return dict;

        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            bool isHeader = true;
            foreach (var record in ReadCsvRecords(reader))
            {
                if (isHeader) { isHeader = false; continue; }
                if (record.Count >= 2 && !string.IsNullOrWhiteSpace(record[0]))
                {
                    var job = record[0].Trim();
                    // ONLY reflect 251078 job data
                    if (!job.StartsWith("251078", StringComparison.OrdinalIgnoreCase)) continue;

                    var client = record.Count >= 3 && !string.IsNullOrWhiteSpace(record[2]) ? record[2].Trim() : record[1].Trim();
                    dict[job] = client;
                    dict[job.TrimEnd('C', 'c')] = client;
                }
            }
        }
        catch { }

        return dict;
    }

    private Dictionary<string, List<RecLogEntry>> LoadRecLogLookup(string filePath)
    {
        var dict = new Dictionary<string, List<RecLogEntry>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath)) return dict;

        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            var recordsEnum = ReadCsvRecords(reader).GetEnumerator();
            if (!recordsEnum.MoveNext()) return dict;

            var headers = recordsEnum.Current;
            int jobIdx = headers.FindIndex(h => CleanHeader(h).Equals("Job", StringComparison.OrdinalIgnoreCase));
            int poIdx = headers.FindIndex(h => CleanHeader(h).Equals("PO", StringComparison.OrdinalIgnoreCase));
            int vendorIdx = headers.FindIndex(h => CleanHeader(h).Equals("Vendor", StringComparison.OrdinalIgnoreCase));
            int plIdx = headers.FindIndex(h => CleanHeader(h).Equals("Packing List", StringComparison.OrdinalIgnoreCase));
            int loadTypeIdx = headers.FindIndex(h => CleanHeader(h).Equals("Load Type", StringComparison.OrdinalIgnoreCase));
            int actualIdx = headers.FindIndex(h => CleanHeader(h).Equals("Actual Arrival", StringComparison.OrdinalIgnoreCase));
            int beginRecIdx = headers.FindIndex(h => CleanHeader(h).Equals("Begin Receiving", StringComparison.OrdinalIgnoreCase));
            int endRecIdx = headers.FindIndex(h => CleanHeader(h).Equals("End Receiving", StringComparison.OrdinalIgnoreCase));
            int mrrIdx = headers.FindIndex(h => CleanHeader(h).Equals("MRR Completed", StringComparison.OrdinalIgnoreCase));
            int recIdIdx = headers.FindIndex(h => CleanHeader(h).Equals("Rec ID", StringComparison.OrdinalIgnoreCase));
            int unloaderIdx = headers.FindIndex(h => CleanHeader(h).Equals("Unloader", StringComparison.OrdinalIgnoreCase));
            int receiverIdx = headers.FindIndex(h => CleanHeader(h).Equals("Receiver", StringComparison.OrdinalIgnoreCase));
            int linkIdx = headers.FindIndex(h => CleanHeader(h).Equals("Link", StringComparison.OrdinalIgnoreCase));

            while (recordsEnum.MoveNext())
            {
                var cols = recordsEnum.Current;
                if (jobIdx < 0 || poIdx < 0 || cols.Count <= Math.Max(jobIdx, poIdx)) continue;

                var job = cols[jobIdx].Trim();
                var po = cols[poIdx].Trim();
                if (string.IsNullOrEmpty(job) || string.IsNullOrEmpty(po)) continue;

                // ONLY reflect 251078 job data
                if (!job.StartsWith("251078", StringComparison.OrdinalIgnoreCase)) continue;

                var entry = new RecLogEntry
                {
                    Job = job,
                    Po = po,
                    Vendor = vendorIdx >= 0 && cols.Count > vendorIdx ? cols[vendorIdx].Trim() : "",
                    PackingList = plIdx >= 0 && cols.Count > plIdx ? cols[plIdx].Trim() : "",
                    LoadType = loadTypeIdx >= 0 && cols.Count > loadTypeIdx ? cols[loadTypeIdx].Trim() : "",
                    ActualArrival = actualIdx >= 0 && cols.Count > actualIdx ? cols[actualIdx].Trim() : "",
                    BeginReceiving = beginRecIdx >= 0 && cols.Count > beginRecIdx ? cols[beginRecIdx].Trim() : "",
                    EndReceiving = endRecIdx >= 0 && cols.Count > endRecIdx ? cols[endRecIdx].Trim() : "",
                    MrrCompleted = mrrIdx >= 0 && cols.Count > mrrIdx ? cols[mrrIdx].Trim() : "",
                    RecId = recIdIdx >= 0 && cols.Count > recIdIdx ? cols[recIdIdx].Trim() : "",
                    Unloader = unloaderIdx >= 0 && cols.Count > unloaderIdx ? cols[unloaderIdx].Trim() : "",
                    Receiver = receiverIdx >= 0 && cols.Count > receiverIdx ? cols[receiverIdx].Trim() : "",
                    Link = linkIdx >= 0 && cols.Count > linkIdx ? cols[linkIdx].Trim() : ""
                };

                var key1 = $"{job}_{po}".ToUpperInvariant();
                var key2 = $"{job.TrimEnd('C', 'c')}_{po.TrimStart('0')}".ToUpperInvariant();

                AddLookup(dict, key1, entry);
                AddLookup(dict, key2, entry);
            }
        }
        catch { }

        return dict;
    }

    private static void AddLookup(Dictionary<string, List<RecLogEntry>> dict, string key, RecLogEntry entry)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<RecLogEntry>();
            dict[key] = list;
        }
        list.Add(entry);
    }

    private List<ExpectedOrder> ParseExpectedReceivingCsv(
        string filePath,
        Dictionary<string, List<RecLogEntry>> recLogLookup,
        Dictionary<string, string> clientLookup)
    {
        var result = new List<ExpectedOrder>();

        using var reader = new StreamReader(filePath, Encoding.UTF8);
        var recordsEnum = ReadCsvRecords(reader).GetEnumerator();
        if (!recordsEnum.MoveNext()) return result;

        var headers = recordsEnum.Current;
        int dateIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Date", StringComparison.OrdinalIgnoreCase));
        int jobIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Job", StringComparison.OrdinalIgnoreCase));
        int poIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("PO", StringComparison.OrdinalIgnoreCase));
        int vendorIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Vendor", StringComparison.OrdinalIgnoreCase));
        int plIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Packing List", StringComparison.OrdinalIgnoreCase));
        int expIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Expected By", StringComparison.OrdinalIgnoreCase));
        int actualIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Actual Arrival", StringComparison.OrdinalIgnoreCase));
        int natureIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Shipment Nature", StringComparison.OrdinalIgnoreCase));
        int recFlagIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("PO Recieved", StringComparison.OrdinalIgnoreCase) || CleanHeader(h).StartsWith("PO Received", StringComparison.OrdinalIgnoreCase));
        int pmIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Project Manager", StringComparison.OrdinalIgnoreCase));
        int longLeadIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Long Lead", StringComparison.OrdinalIgnoreCase));
        int notesIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Notes", StringComparison.OrdinalIgnoreCase));

        int idCounter = 1;
        while (recordsEnum.MoveNext())
        {
            var cols = recordsEnum.Current;
            if (jobIdx < 0 || poIdx < 0 || cols.Count <= Math.Max(jobIdx, poIdx)) continue;

            var job = cols[jobIdx].Trim();
            var rawPo = cols[poIdx].Trim();
            if (string.IsNullOrEmpty(job) || string.IsNullOrEmpty(rawPo)) continue;

            // ONLY reflect 251078 job data
            if (!job.StartsWith("251078", StringComparison.OrdinalIgnoreCase)) continue;

            var vendor = vendorIdx >= 0 && cols.Count > vendorIdx ? cols[vendorIdx].Trim() : "";
            var packingSlip = plIdx >= 0 && cols.Count > plIdx ? cols[plIdx].Trim() : "";
            var expDateStr = expIdx >= 0 && cols.Count > expIdx ? cols[expIdx].Trim() : "";
            var actualArrivalStr = actualIdx >= 0 && cols.Count > actualIdx ? cols[actualIdx].Trim() : "";
            var nature = natureIdx >= 0 && cols.Count > natureIdx ? cols[natureIdx].Trim() : "";
            var poReceivedStr = recFlagIdx >= 0 && cols.Count > recFlagIdx ? cols[recFlagIdx].Trim() : "";
            var pm = pmIdx >= 0 && cols.Count > pmIdx ? cols[pmIdx].Trim() : "";
            var longLeadStr = longLeadIdx >= 0 && cols.Count > longLeadIdx ? cols[longLeadIdx].Trim() : "";
            var notes = notesIdx >= 0 && cols.Count > notesIdx ? cols[notesIdx].Trim() : "";
            var orderDateStr = dateIdx >= 0 && cols.Count > dateIdx ? cols[dateIdx].Trim() : "";

            DateTime? expectedDate = ParseDate(expDateStr);
            DateTime? actualArrivalDate = ParseDate(actualArrivalStr);
            DateTime? orderDate = ParseDate(orderDateStr);

            // Cross-reference with RecLog
            var key1 = $"{job}_{rawPo}".ToUpperInvariant();
            var key2 = $"{job.TrimEnd('C', 'c')}_{rawPo.TrimStart('0')}".ToUpperInvariant();
            List<RecLogEntry>? recMatches = null;
            if (recLogLookup.TryGetValue(key1, out var m1)) recMatches = m1;
            else if (recLogLookup.TryGetValue(key2, out var m2)) recMatches = m2;

            var loadType = nature;
            var unloader = "";
            var receiver = "";
            var recId = "";
            var driveLink = "";
            var allPackingSlips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(packingSlip)) allPackingSlips.Add(packingSlip);

            bool hasLanded = actualArrivalDate.HasValue;

            if (recMatches != null && recMatches.Count > 0)
            {
                var distinctTypes = recMatches
                    .Select(r => r.LoadType)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .ToList();

                if (distinctTypes.Count > 0)
                {
                    loadType = string.Join(", ", distinctTypes);
                }

                foreach (var rm in recMatches)
                {
                    if (!string.IsNullOrWhiteSpace(rm.PackingList))
                    {
                        allPackingSlips.Add(rm.PackingList);
                    }
                    if (!string.IsNullOrWhiteSpace(rm.ActualArrival))
                    {
                        hasLanded = true;
                        if (!actualArrivalDate.HasValue)
                        {
                            actualArrivalDate = ParseDate(rm.ActualArrival);
                        }
                    }
                    if (string.IsNullOrEmpty(unloader) && !string.IsNullOrEmpty(rm.Unloader)) unloader = rm.Unloader;
                    if (string.IsNullOrEmpty(receiver) && !string.IsNullOrEmpty(rm.Receiver)) receiver = rm.Receiver;
                    if (string.IsNullOrEmpty(recId) && !string.IsNullOrEmpty(rm.RecId)) recId = rm.RecId;
                    if (string.IsNullOrEmpty(driveLink) && !string.IsNullOrEmpty(rm.Link)) driveLink = rm.Link;
                }
            }

            loadType = StandardizeLoadType(loadType, nature, vendor);

            // Exclude structural steel as it is handled by another team
            bool isStructural = IsStructuralSteel(loadType, vendor, notes);
            if (isStructural) continue;

            var clientName = "";
            if (clientLookup.TryGetValue(job, out var cName)) clientName = cName;
            else if (clientLookup.TryGetValue(job.TrimEnd('C', 'c'), out var cName2)) clientName = cName2;
            if (string.IsNullOrEmpty(clientName)) clientName = "CNRL";

            var finalPackingSlip = allPackingSlips.Count > 0 ? string.Join(", ", allPackingSlips) : packingSlip;

            bool isReceived = poReceivedStr.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            string yardStatus = "In Transit";
            if (hasLanded)
            {
                yardStatus = "Landed in Yard";
            }
            else if (expectedDate.HasValue && expectedDate.Value.Date < DateTime.Today)
            {
                yardStatus = "Past Due";
            }

            string recStatus = "Not Received";
            if (isReceived)
            {
                recStatus = "Received";
            }
            else if (hasLanded)
            {
                recStatus = "Pending Inspection";
            }
            else if (allPackingSlips.Count > 1)
            {
                recStatus = "Partially Received / Split";
            }

            var order = new ExpectedOrder
            {
                Id = idCounter++,
                JobNumber = job,
                PoNumber = rawPo,
                VendorName = vendor,
                PackingSlipNumber = finalPackingSlip,
                LoadType = loadType,
                OrderDate = orderDate,
                ExpectedDate = expectedDate,
                HasLandedInYard = hasLanded,
                ActualArrivalDate = actualArrivalDate,
                YardStatus = yardStatus,
                ReceivingStatus = recStatus,
                ClientName = clientName,
                ProjectManager = pm,
                ShipmentNature = nature,
                IsLongLead = longLeadStr.Equals("Yes", StringComparison.OrdinalIgnoreCase) || longLeadStr.Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                Notes = notes,
                Unloader = unloader,
                Receiver = receiver,
                RecId = recId,
                DriveLink = driveLink,
                IsStructural = isStructural
            };

            result.Add(order);
        }

        return result;
    }

    private static string CleanHeader(string h)
    {
        return h.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    public static bool IsStructuralSteel(string loadType, string vendor, string notes)
    {
        var combined = $"{loadType} {vendor} {notes}".ToLowerInvariant();
        if (combined.Contains("structural") || combined.Contains("steel, structural") || combined.Contains("structural, steel") || combined.Contains("ctl steel"))
        {
            return true;
        }
        return false;
    }

    public static string StandardizeLoadType(string loadType, string nature, string vendor)
    {
        if (string.IsNullOrWhiteSpace(loadType))
        {
            loadType = nature;
        }

        if (string.IsNullOrWhiteSpace(loadType))
        {
            var vLower = vendor.ToLowerInvariant();
            if (vLower.Contains("valve") || vLower.Contains("comco")) return "Valves";
            if (vLower.Contains("fastener") || vLower.Contains("bolt")) return "SNG / Bolt-Up";
            if (vLower.Contains("pipe") || vLower.Contains("piping")) return "Pipe";
            if (vLower.Contains("flange") || vLower.Contains("armor alloys")) return "Flanges & Fittings";
            if (vLower.Contains("precision") || vLower.Contains("support")) return "Supports";
            if (vLower.Contains("electrical") || vLower.Contains("sonepar") || vLower.Contains("texcan")) return "Electrical";
            if (vLower.Contains("spartan") || vLower.Contains("instrument")) return "Instrumentation";
            return "General Materials";
        }

        loadType = loadType.Replace("P&F", "Pipe & Fittings")
                           .Replace("SNG", "SNG / Bolt-Up");

        return loadType.Trim();
    }

    public static DateTime? ParseDate(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;
        str = str.Trim();

        var formats = new[]
        {
            "MMM/d/yyyy H:mm",
            "MMM/dd/yyyy H:mm",
            "MMM/d/yyyy HH:mm",
            "MMM/dd/yyyy HH:mm",
            "MMM/d/yyyy",
            "MMM/dd/yyyy",
            "yyyy-MM-dd",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "MMM dd, yyyy",
            "MMMM dd, yyyy",
            "yyyy-MM-ddTHH:mm:ss"
        };

        if (DateTime.TryParseExact(str, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtGeneric))
        {
            return dtGeneric;
        }

        return null;
    }

    /// <summary>
    /// RFC 4180 compliant CSV stream parser supporting embedded newlines and quotes.
    /// </summary>
    public static IEnumerable<List<string>> ReadCsvRecords(TextReader reader)
    {
        var currentField = new StringBuilder();
        var currentRecord = new List<string>();
        bool inQuotes = false;

        while (reader.Read() is int ch && ch != -1)
        {
            char c = (char)ch;

            if (c == '"')
            {
                if (inQuotes && reader.Peek() == '"')
                {
                    reader.Read(); // consume escaped quote
                    currentField.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                currentRecord.Add(currentField.ToString());
                currentField.Clear();
            }
            else if ((c == '\r' || c == '\n') && !inQuotes)
            {
                if (c == '\r' && reader.Peek() == '\n')
                {
                    reader.Read(); // consume \n
                }
                currentRecord.Add(currentField.ToString());
                currentField.Clear();

                if (currentRecord.Count > 1 || (currentRecord.Count == 1 && !string.IsNullOrWhiteSpace(currentRecord[0])))
                {
                    yield return currentRecord;
                }
                currentRecord = new List<string>();
            }
            else
            {
                currentField.Append(c);
            }
        }

        if (currentField.Length > 0 || currentRecord.Count > 0)
        {
            currentRecord.Add(currentField.ToString());
            yield return currentRecord;
        }
    }

    private void MergeUnreceivedReport(
        List<ExpectedOrder> orders,
        string unrecFile,
        Dictionary<string, string> clientLookup)
    {
        if (!File.Exists(unrecFile)) return;

        try
        {
            using var reader = new StreamReader(unrecFile, Encoding.UTF8);
            var recordsEnum = ReadCsvRecords(reader).GetEnumerator();
            int poIdx = -1, descIdx = -1, bomIdx = -1, vendorIdx = -1, expIdx = -1;

            while (recordsEnum.MoveNext())
            {
                var cols = recordsEnum.Current;
                int pIdx = cols.FindIndex(h => CleanHeader(h).Equals("PO", StringComparison.OrdinalIgnoreCase));
                int dIdx = cols.FindIndex(h => CleanHeader(h).Contains("Drafting BOM Description", StringComparison.OrdinalIgnoreCase));
                if (pIdx >= 0 || dIdx >= 0)
                {
                    poIdx = pIdx;
                    descIdx = dIdx;
                    bomIdx = cols.FindIndex(h => CleanHeader(h).Equals("BOM ID", StringComparison.OrdinalIgnoreCase));
                    vendorIdx = cols.FindIndex(h => CleanHeader(h).Equals("Supplier", StringComparison.OrdinalIgnoreCase) || CleanHeader(h).Equals("Vendor", StringComparison.OrdinalIgnoreCase));
                    expIdx = cols.FindIndex(h => CleanHeader(h).Contains("Date Expected", StringComparison.OrdinalIgnoreCase));
                    break;
                }
            }

            if (poIdx < 0) return;

            int nextId = orders.Count > 0 ? orders.Max(o => o.Id) + 1 : 1;

            while (recordsEnum.MoveNext())
            {
                var cols = recordsEnum.Current;
                if (cols.Count <= poIdx) continue;

                var poRaw = cols[poIdx].Trim();
                if (string.IsNullOrEmpty(poRaw)) continue;

                // ONLY reflect 251078 job data
                if (!poRaw.StartsWith("251078", StringComparison.OrdinalIgnoreCase)) continue;

                var desc = descIdx >= 0 && cols.Count > descIdx ? cols[descIdx].Trim() : "";
                var supplier = vendorIdx >= 0 && cols.Count > vendorIdx ? cols[vendorIdx].Trim() : "";
                var expDateStr = expIdx >= 0 && cols.Count > expIdx ? cols[expIdx].Trim() : "";
                var expDate = ParseDate(expDateStr);

                string job = "251078C";
                string poSuffix = poRaw;
                if (poRaw.StartsWith("251078C", StringComparison.OrdinalIgnoreCase) && poRaw.Length > 7)
                {
                    poSuffix = poRaw.Substring(7);
                }
                else if (poRaw.StartsWith("251078", StringComparison.OrdinalIgnoreCase) && poRaw.Length > 6)
                {
                    poSuffix = poRaw.Substring(6).TrimStart('C', 'c');
                }

                // Check for existing order match
                var existing = orders.FirstOrDefault(o =>
                    o.JobNumber.StartsWith("251078", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(o.PoNumber, poSuffix, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(o.PoNumber, poSuffix.TrimStart('0'), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(o.PoNumber.TrimStart('0'), poSuffix.TrimStart('0'), StringComparison.OrdinalIgnoreCase)));

                if (existing != null)
                {
                    if (expDate.HasValue && (existing.ExpectedDate == null || expDate.Value.Year >= 2026 || poSuffix == "0435"))
                    {
                        existing.ExpectedDate = expDate.Value;
                        if (existing.ExpectedDate.Value.Date < new DateTime(2026, 10, 5) && !existing.HasLandedInYard)
                        {
                            existing.YardStatus = "Past Due";
                        }
                    }
                    if (string.IsNullOrWhiteSpace(existing.VendorName) && !string.IsNullOrWhiteSpace(supplier))
                    {
                        existing.VendorName = supplier;
                    }
                }
                else
                {
                    var loadType = StandardizeLoadType("", desc, supplier);
                    if (IsStructuralSteel(loadType, supplier, desc)) continue;

                    DateTime todayRef = new DateTime(2026, 10, 5);
                    string yardStatus = "In Transit";
                    if (expDate.HasValue && expDate.Value.Date < todayRef)
                    {
                        yardStatus = "Past Due";
                    }

                    orders.Add(new ExpectedOrder
                    {
                        Id = nextId++,
                        JobNumber = job,
                        PoNumber = poSuffix,
                        VendorName = !string.IsNullOrWhiteSpace(supplier) ? supplier : "Trans Am Piping Products Ltd",
                        PackingSlipNumber = "",
                        LoadType = loadType,
                        ExpectedDate = expDate,
                        HasLandedInYard = false,
                        YardStatus = yardStatus,
                        ReceivingStatus = "Not Received",
                        ClientName = "CNRL",
                        ShipmentNature = desc,
                        Notes = $"Unreceived Report item: {desc}"
                    });
                }
            }
        }
        catch { }
    }

    public List<MaterialItem> LoadMaterialItems()
    {
        var items = new List<MaterialItem>();
        var invFile = Path.Combine(_importsDirectory, "CNRL - Inventory - 251078.csv");

        if (File.Exists(invFile))
        {
            var vendorLookup = GetPoVendorLookup();
            try
            {
                using var reader = new StreamReader(invFile, Encoding.UTF8);
                var recordsEnum = ReadCsvRecords(reader).GetEnumerator();
                if (recordsEnum.MoveNext())
                {
                    var headers = recordsEnum.Current;
                    int typeIdx = headers.FindIndex(h => CleanHeader(h).Equals("Type", StringComparison.OrdinalIgnoreCase));
                    int formIdx = headers.FindIndex(h => CleanHeader(h).Equals("Form", StringComparison.OrdinalIgnoreCase));
                    int dateIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Date", StringComparison.OrdinalIgnoreCase));
                    int poIdx = headers.FindIndex(h => CleanHeader(h).Equals("PO", StringComparison.OrdinalIgnoreCase));
                    int catIdx = headers.FindIndex(h => CleanHeader(h).Equals("Category", StringComparison.OrdinalIgnoreCase));
                    int subcatIdx = headers.FindIndex(h => CleanHeader(h).Equals("Subcategory", StringComparison.OrdinalIgnoreCase));
                    int descIdx = headers.FindIndex(h => CleanHeader(h).Contains("Description", StringComparison.OrdinalIgnoreCase));
                    int bomIdx = headers.FindIndex(h => CleanHeader(h).Equals("BOMID", StringComparison.OrdinalIgnoreCase));
                    int qtyIdx = headers.FindIndex(h => CleanHeader(h).Equals("Qty", StringComparison.OrdinalIgnoreCase));
                    int tagIdx = headers.FindIndex(h => CleanHeader(h).Contains("Tag", StringComparison.OrdinalIgnoreCase));
                    int heatIdx = headers.FindIndex(h => CleanHeader(h).Contains("Heat", StringComparison.OrdinalIgnoreCase));
                    int locIdx = headers.FindIndex(h => CleanHeader(h).Equals("Location", StringComparison.OrdinalIgnoreCase));
                    int recIdx = headers.FindIndex(h => CleanHeader(h).Contains("Receiver") || CleanHeader(h).Contains("Kitter"));

                    int idCounter = 1;
                    while (recordsEnum.MoveNext())
                    {
                        var cols = recordsEnum.Current;
                        if (typeIdx < 0 || cols.Count <= typeIdx) continue;

                        var typeVal = cols[typeIdx].Trim();
                        // Only import Received inventory (Type == "Rec")
                        if (!typeVal.Equals("Rec", StringComparison.OrdinalIgnoreCase)) continue;

                        var form = formIdx >= 0 && cols.Count > formIdx ? cols[formIdx].Trim() : "";
                        var dateStr = dateIdx >= 0 && cols.Count > dateIdx ? cols[dateIdx].Trim() : "";
                        var po = poIdx >= 0 && cols.Count > poIdx ? cols[poIdx].Trim() : "";
                        var cat = catIdx >= 0 && cols.Count > catIdx ? cols[catIdx].Trim() : "";
                        var subcat = subcatIdx >= 0 && cols.Count > subcatIdx ? cols[subcatIdx].Trim() : "";
                        var desc = descIdx >= 0 && cols.Count > descIdx ? cols[descIdx].Trim() : "";
                        var bomId = bomIdx >= 0 && cols.Count > bomIdx ? cols[bomIdx].Trim() : "";
                        var qtyStr = qtyIdx >= 0 && cols.Count > qtyIdx ? cols[qtyIdx].Trim() : "";
                        var heat = heatIdx >= 0 && cols.Count > heatIdx ? cols[heatIdx].Trim() : "";
                        var loc = locIdx >= 0 && cols.Count > locIdx ? cols[locIdx].Trim() : "";
                        var receiver = recIdx >= 0 && cols.Count > recIdx ? cols[recIdx].Trim() : "";
                        var tag = tagIdx >= 0 && cols.Count > tagIdx ? cols[tagIdx].Trim() : "";

                        // Standardize heat number (replace embedded newlines)
                        if (!string.IsNullOrWhiteSpace(heat))
                        {
                            var heatLines = heat.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(h => h.Trim())
                                                .Where(h => !string.IsNullOrEmpty(h));
                            heat = string.Join(", ", heatLines);
                            if (heat.Length > 150)
                            {
                                heat = heat.Substring(0, 147) + "...";
                            }
                        }

                        decimal qty = 1;
                        if (!string.IsNullOrWhiteSpace(qtyStr))
                        {
                            var cleanedQty = qtyStr.Replace(",", "").Trim();
                            if (decimal.TryParse(cleanedQty, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedQty))
                            {
                                qty = parsedQty;
                            }
                        }

                        DateTime? recvDate = ParseDate(dateStr);

                        // Vendor lookup by PO
                        string vendor = "";
                        if (!string.IsNullOrEmpty(po))
                        {
                            if (vendorLookup.TryGetValue(po, out var v1)) vendor = v1;
                            else if (vendorLookup.TryGetValue(po.TrimStart('0'), out var v2)) vendor = v2;
                            else if (vendorLookup.TryGetValue(po.PadLeft(4, '0'), out var v3)) vendor = v3;
                            else if (vendorLookup.TryGetValue($"251078C{po}", out var v4)) vendor = v4;
                        }

                        if (string.IsNullOrEmpty(vendor))
                        {
                            vendor = "CNRL Supplier";
                        }

                        string fullPo = string.IsNullOrWhiteSpace(po) 
                            ? "251078C" 
                            : (po.StartsWith("251078", StringComparison.OrdinalIgnoreCase) ? po : $"251078C{po.PadLeft(4, '0')}");

                        string recId = !string.IsNullOrWhiteSpace(form) 
                            ? form 
                            : (!string.IsNullOrWhiteSpace(po) ? $"C{po.TrimStart('0')}" : $"REC-{idCounter:D4}");

                        items.Add(new MaterialItem
                        {
                            Id = idCounter++,
                            JobNumber = "251078",
                            PoNumber = fullPo,
                            ReceiverId = recId,
                            VendorName = vendor,
                            Description = string.IsNullOrWhiteSpace(desc) ? $"{cat} {subcat}".Trim() : desc,
                            BomId = string.IsNullOrWhiteSpace(bomId) ? "N/A" : bomId,
                            QuantityShipped = qty,
                            QuantityReceived = qty,
                            Unit = "EA",
                            HeatNumber = heat,
                            SerialNumber = tag,
                            BinLocation = string.IsNullOrWhiteSpace(loc) ? "Walkway" : loc,
                            ReceivedBy = string.IsNullOrWhiteSpace(receiver) ? "Kris" : receiver,
                            ReceivedDate = recvDate,
                            Status = "Complete"
                        });
                    }
                }
            }
            catch { }
        }

        if (items.Count == 0)
        {
            items = GetFallbackMaterialItems();
        }

        return items;
    }

    public static List<MaterialItem> GetFallbackMaterialItems()
    {
        return new List<MaterialItem>
        {
            new()
            {
                Id = 1,
                JobNumber = "251078",
                PoNumber = "251078C0013",
                ReceiverId = "80225923",
                VendorName = "Master Flo Valve",
                Description = "4\" HUB CONNECTION ANGLE CHOKE VALVE 1500# TAG# HV-XX05",
                BomId = "CNRL-CSS-026",
                QuantityShipped = 10,
                QuantityReceived = 10,
                Unit = "EA",
                HeatNumber = "1000353567, 1000353568",
                BinLocation = "Walkway",
                ReceivedBy = "Kris",
                ReceivedDate = new DateTime(2026, 9, 17),
                Status = "Complete"
            },
            new()
            {
                Id = 2,
                JobNumber = "251078",
                PoNumber = "251078C0433",
                ReceiverId = "C0433",
                VendorName = "Comco Pipe & Supply Company",
                Description = "1\" GATE VLV SB CL 800 SW x TE BB A350-LF2 CL1 TR 12",
                BomId = "CNRL-028",
                QuantityShipped = 4,
                QuantityReceived = 4,
                Unit = "EA",
                HeatNumber = "B2019",
                BinLocation = "Bay 1 - Receiving Floor",
                ReceivedBy = "Kris",
                ReceivedDate = new DateTime(2026, 9, 25),
                Status = "Complete"
            },
            new()
            {
                Id = 3,
                JobNumber = "251078",
                PoNumber = "251078C0430",
                ReceiverId = "C0430",
                VendorName = "Pickford",
                Description = "Grip-Tight Plug Purchase 16\" sch 60",
                BomId = "GT-PLUG-16",
                QuantityShipped = 2,
                QuantityReceived = 2,
                Unit = "EA",
                HeatNumber = "PK-88210",
                BinLocation = "Bay 1 - Receiving Floor",
                ReceivedBy = "Laurence",
                ReceivedDate = new DateTime(2026, 10, 2),
                Status = "Complete"
            },
            new()
            {
                Id = 4,
                JobNumber = "251078",
                PoNumber = "251078C0438",
                ReceiverId = "16748-001",
                VendorName = "Precision Fabrication",
                Description = "6\" PIPE SHOE TAG# S13-B-6-6 TRIM FOR 6\" XXS A333 REPAD",
                BomId = "CNRL-602",
                QuantityShipped = 2,
                QuantityReceived = 2,
                Unit = "EA",
                HeatNumber = "PF-9941",
                BinLocation = "Bay 2 - Fabrication Staging",
                ReceivedBy = "Tyler",
                ReceivedDate = new DateTime(2026, 10, 2),
                Status = "Complete"
            },
            new()
            {
                Id = 5,
                JobNumber = "251078",
                PoNumber = "251078C0442",
                ReceiverId = "C0442",
                VendorName = "Trans Am Piping Products Ltd",
                Description = "2\" 1500# RF Blind Flange A105N",
                BomId = "FLG2-9-15RFBL105",
                QuantityShipped = 2,
                QuantityReceived = 2,
                Unit = "EA",
                HeatNumber = "TA-88402",
                BinLocation = "Bay 1 - Receiving Floor",
                ReceivedBy = "Simon",
                ReceivedDate = new DateTime(2026, 10, 3),
                Status = "Complete"
            },
            new()
            {
                Id = 6,
                JobNumber = "251078",
                PoNumber = "251078C0045",
                ReceiverId = "C0045",
                VendorName = "Canadian Natural Resources Ltd.",
                Description = "12\" Sch 40(10.3) BE x BE A106-B SMLS",
                BomId = "PIPE1240106",
                QuantityShipped = 1786,
                QuantityReceived = 1786,
                Unit = "EA",
                HeatNumber = "H88120",
                BinLocation = "Yard Row A - Pipe Racks",
                ReceivedBy = "Yard Crew",
                ReceivedDate = new DateTime(2026, 9, 24),
                Status = "Complete"
            },
            new()
            {
                Id = 7,
                JobNumber = "251078",
                PoNumber = "251078C0077",
                ReceiverId = "C0077",
                VendorName = "Canadian Natural Resources Ltd.",
                Description = "12\"x 8\" Red Tee Sch 40/Std A234-WPB",
                BomId = "TEE12-8-40STD234",
                QuantityShipped = 4,
                QuantityReceived = 4,
                Unit = "EA",
                HeatNumber = "T91204",
                BinLocation = "Bay 1 - Receiving Floor",
                ReceivedBy = "Brandon",
                ReceivedDate = new DateTime(2026, 9, 24),
                Status = "Complete"
            }
        };
    }

    public List<InspectionLineItem> LoadVistaBomForPo(string poNumber, string? jpKey, int receivingLogEntryId)
    {
        var lines = new List<InspectionLineItem>();
        var vistaFile = Path.Combine(_importsDirectory, "251078 MMT - VISTA BOM.csv");

        if (File.Exists(vistaFile))
        {
            try
            {
                using var reader = new StreamReader(vistaFile, Encoding.UTF8);
                var recordsEnum = ReadCsvRecords(reader).GetEnumerator();
                int descIdx = -1, bomIdx = -1, orderedIdx = -1, recIdx = -1, dueIdx = -1, umIdx = -1, poIdx = -1, lineIdx = -1;

                while (recordsEnum.MoveNext())
                {
                    var cols = recordsEnum.Current;
                    int dIdx = cols.FindIndex(h => CleanHeader(h).Contains("Description", StringComparison.OrdinalIgnoreCase));
                    int bIdx = cols.FindIndex(h => CleanHeader(h).Equals("BOM ID", StringComparison.OrdinalIgnoreCase));
                    if (dIdx >= 0 && bIdx >= 0)
                    {
                        descIdx = dIdx;
                        bomIdx = bIdx;
                        orderedIdx = cols.FindIndex(h => CleanHeader(h).Contains("Ordered", StringComparison.OrdinalIgnoreCase));
                        recIdx = cols.FindIndex(h => CleanHeader(h).Contains("Received", StringComparison.OrdinalIgnoreCase));
                        dueIdx = cols.FindIndex(h => CleanHeader(h).Contains("Due", StringComparison.OrdinalIgnoreCase));
                        umIdx = cols.FindIndex(h => CleanHeader(h).Equals("UM", StringComparison.OrdinalIgnoreCase));
                        poIdx = cols.FindIndex(h => CleanHeader(h).Contains("PO", StringComparison.OrdinalIgnoreCase));
                        lineIdx = cols.FindIndex(h => CleanHeader(h).Contains("Line", StringComparison.OrdinalIgnoreCase));
                        break;
                    }
                }

                if (descIdx >= 0 && poIdx >= 0)
                {
                    string targetPo1 = (poNumber ?? "").Trim();
                    string targetPoClean = targetPo1.TrimStart('0');
                    string targetPoPadded = targetPo1.PadLeft(4, '0');
                    string targetJp = (jpKey ?? "").Trim();

                    int lineCounter = 1;
                    while (recordsEnum.MoveNext())
                    {
                        var cols = recordsEnum.Current;
                        if (cols.Count <= Math.Max(descIdx, poIdx)) continue;

                        var rowPo = cols[poIdx].Trim();
                        if (string.IsNullOrEmpty(rowPo)) continue;

                        bool matches = false;
                        if (!string.IsNullOrEmpty(targetJp) && rowPo.Equals(targetJp, StringComparison.OrdinalIgnoreCase))
                        {
                            matches = true;
                        }
                        else if (!string.IsNullOrEmpty(targetPo1))
                        {
                            if (rowPo.Equals(targetPo1, StringComparison.OrdinalIgnoreCase) ||
                                rowPo.EndsWith(targetPo1, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(targetPoClean) && (rowPo.EndsWith(targetPoClean, StringComparison.OrdinalIgnoreCase) || rowPo.Equals(targetPoClean, StringComparison.OrdinalIgnoreCase))) ||
                                rowPo.EndsWith(targetPoPadded, StringComparison.OrdinalIgnoreCase) ||
                                rowPo.Equals($"251078C{targetPoPadded}", StringComparison.OrdinalIgnoreCase))
                            {
                                matches = true;
                            }
                        }

                        if (!matches) continue;

                        var desc = cols[descIdx].Trim();
                        var bomId = bomIdx >= 0 && cols.Count > bomIdx ? cols[bomIdx].Trim() : "";
                        var ordStr = orderedIdx >= 0 && cols.Count > orderedIdx ? cols[orderedIdx].Trim() : "0";
                        var recStr = recIdx >= 0 && cols.Count > recIdx ? cols[recIdx].Trim() : "0";
                        var dueStr = dueIdx >= 0 && cols.Count > dueIdx ? cols[dueIdx].Trim() : "0";
                        var um = umIdx >= 0 && cols.Count > umIdx ? cols[umIdx].Trim() : "EA";
                        var lineNum = lineIdx >= 0 && cols.Count > lineIdx ? cols[lineIdx].Trim() : $"{lineCounter}.00";

                        decimal ordered = 0;
                        if (decimal.TryParse(ordStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var o)) ordered = o;

                        decimal received = 0;
                        if (decimal.TryParse(recStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var r)) received = r;

                        decimal due = 0;
                        if (decimal.TryParse(dueStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) due = d;

                        lines.Add(new InspectionLineItem
                        {
                            Id = lineCounter++,
                            ReceivingLogEntryId = receivingLogEntryId,
                            JpKey = string.IsNullOrWhiteSpace(jpKey) ? rowPo : jpKey,
                            PoNumber = rowPo,
                            PoLineNumber = lineNum,
                            ItemDescription = desc,
                            BomId = string.IsNullOrWhiteSpace(bomId) ? "N/A" : bomId,
                            QuantityOrdered = ordered,
                            QuantityPreviouslyReceived = received,
                            QuantityDue = due,
                            UnitOfMeasure = string.IsNullOrWhiteSpace(um) ? "EA" : um,
                            IsDescriptionMatched = false,
                            QuantityReceivedThisShipment = due > 0 ? due : 0,
                            HeatNumber = "",
                            SerialNumber = "",
                            StorageLocation = "",
                            Notes = ""
                        });
                    }
                }
            }
            catch { }
        }

        if (lines.Count == 0)
        {
            lines = GetFallbackInspectionLines(poNumber ?? string.Empty, jpKey, receivingLogEntryId);
        }

        return lines;
    }

    public static List<InspectionLineItem> GetFallbackInspectionLines(string poNumber, string? jpKey, int receivingLogEntryId)
    {
        var cleanPo = (poNumber ?? "").Trim();
        var key = string.IsNullOrWhiteSpace(jpKey) ? $"251078C{cleanPo.PadLeft(4, '0')}" : jpKey;

        if (cleanPo.Contains("0270") || cleanPo.Contains("270"))
        {
            return new List<InspectionLineItem>
            {
                new()
                {
                    Id = 1,
                    ReceivingLogEntryId = receivingLogEntryId,
                    JpKey = key,
                    PoNumber = "0270",
                    PoLineNumber = "1.00",
                    ItemDescription = "3/4\" x 5-1/2\" Stud Bolt A193 B7 w/ 2 Heavy Hex Nuts A194 2H",
                    BomId = "SNG-34-512-B7",
                    QuantityOrdered = 100,
                    QuantityPreviouslyReceived = 0,
                    QuantityDue = 100,
                    UnitOfMeasure = "EA",
                    IsDescriptionMatched = true,
                    QuantityReceivedThisShipment = 100,
                    HeatNumber = "B7-99214",
                    SerialNumber = "LOT-260904-A",
                    StorageLocation = "4 D 2 2",
                    Notes = "Count verified. MTR on file."
                },
                new()
                {
                    Id = 2,
                    ReceivingLogEntryId = receivingLogEntryId,
                    JpKey = key,
                    PoNumber = "0270",
                    PoLineNumber = "2.00",
                    ItemDescription = "1\" x 7\" Stud Bolt A193 B7 w/ 2 Heavy Hex Nuts A194 2H",
                    BomId = "SNG-10-700-B7",
                    QuantityOrdered = 80,
                    QuantityPreviouslyReceived = 0,
                    QuantityDue = 80,
                    UnitOfMeasure = "EA",
                    IsDescriptionMatched = true,
                    QuantityReceivedThisShipment = 80,
                    HeatNumber = "B7-88102",
                    SerialNumber = "LOT-260904-B",
                    StorageLocation = "4 D 2 2",
                    Notes = "Box unbroken. Thread inspection OK."
                },
                new()
                {
                    Id = 3,
                    ReceivingLogEntryId = receivingLogEntryId,
                    JpKey = key,
                    PoNumber = "0270",
                    PoLineNumber = "3.00",
                    ItemDescription = "3\" 600# 316SS Spiral Wound Gasket w/ Flexible Graphite Filler",
                    BomId = "GSK-SW-3-600",
                    QuantityOrdered = 25,
                    QuantityPreviouslyReceived = 0,
                    QuantityDue = 25,
                    UnitOfMeasure = "EA",
                    IsDescriptionMatched = true,
                    QuantityReceivedThisShipment = 25,
                    HeatNumber = "HT-GSK-316-44",
                    SerialNumber = "TAG-GSK-3-600",
                    StorageLocation = "4 D 2 2",
                    Notes = "Color code orange/green verified."
                },
                new()
                {
                    Id = 4,
                    ReceivingLogEntryId = receivingLogEntryId,
                    JpKey = key,
                    PoNumber = "0270",
                    PoLineNumber = "4.00",
                    ItemDescription = "4\" 600# 316SS Spiral Wound Gasket w/ Flexible Graphite Filler",
                    BomId = "GSK-SW-4-600",
                    QuantityOrdered = 20,
                    QuantityPreviouslyReceived = 0,
                    QuantityDue = 20,
                    UnitOfMeasure = "EA",
                    IsDescriptionMatched = true,
                    QuantityReceivedThisShipment = 20,
                    HeatNumber = "HT-GSK-316-52",
                    SerialNumber = "TAG-GSK-4-600",
                    StorageLocation = "4 D 2 2",
                    Notes = "Visual inspection passed."
                }
            };
        }

        return new List<InspectionLineItem>
        {
            new()
            {
                Id = 1,
                ReceivingLogEntryId = receivingLogEntryId,
                JpKey = key,
                PoNumber = cleanPo,
                PoLineNumber = "1.00",
                ItemDescription = $"Shipment Items for PO {cleanPo} (CNRL Job 251078)",
                BomId = "GEN-ITEM-01",
                QuantityOrdered = 1,
                QuantityPreviouslyReceived = 0,
                QuantityDue = 1,
                UnitOfMeasure = "EA",
                IsDescriptionMatched = false,
                QuantityReceivedThisShipment = 1,
                HeatNumber = "",
                SerialNumber = "",
                StorageLocation = "Walkway",
                Notes = ""
            }
        };
    }

    private Dictionary<string, string> GetPoVendorLookup()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Prepopulate known vendors for Job 251078
        dict["0013"] = "Master Flo Valve";
        dict["0014"] = "Master Flo Valve";
        dict["0001"] = "Comco Pipe and Supply Company";
        dict["0045"] = "Canadian Natural Resources Ltd.";
        dict["0074"] = "Canadian Natural Resources Ltd.";
        dict["0077"] = "Canadian Natural Resources Ltd.";
        dict["0142"] = "Canadian Natural Resources Ltd.";
        dict["0152"] = "Canadian Natural Resources Ltd.";
        dict["0355"] = "Trans Am Piping Products Ltd";
        dict["0361"] = "Comco Pipe and Supply Company";
        dict["0383"] = "Canadian Natural Resources Ltd.";
        dict["0430"] = "Pickford";
        dict["0433"] = "Comco Pipe & Supply Company";
        dict["0435"] = "Comco Pipe and Supply Company";
        dict["0438"] = "Precision Fabrication";
        dict["0442"] = "Trans Am Piping Products Ltd";

        // Also harvest from MML Expected Receiving
        var expFile = Path.Combine(_importsDirectory, "MML - Expected Receiving.csv");
        if (File.Exists(expFile))
        {
            try
            {
                using var reader = new StreamReader(expFile, Encoding.UTF8);
                var recordsEnum = ReadCsvRecords(reader).GetEnumerator();
                if (recordsEnum.MoveNext())
                {
                    var headers = recordsEnum.Current;
                    int jobIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Job", StringComparison.OrdinalIgnoreCase));
                    int poIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("PO", StringComparison.OrdinalIgnoreCase));
                    int vendorIdx = headers.FindIndex(h => CleanHeader(h).StartsWith("Vendor", StringComparison.OrdinalIgnoreCase));

                    while (recordsEnum.MoveNext())
                    {
                        var cols = recordsEnum.Current;
                        if (jobIdx < 0 || poIdx < 0 || vendorIdx < 0 || cols.Count <= Math.Max(jobIdx, Math.Max(poIdx, vendorIdx))) continue;
                        var job = cols[jobIdx].Trim();
                        var po = cols[poIdx].Trim();
                        var vendor = cols[vendorIdx].Trim();
                        if (job.StartsWith("251078", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(po) && !string.IsNullOrEmpty(vendor))
                        {
                            dict[po] = vendor;
                            dict[po.TrimStart('0')] = vendor;
                            dict[po.PadLeft(4, '0')] = vendor;
                            dict[$"{job}{po}"] = vendor;
                        }
                    }
                }
            }
            catch { }
        }

        return dict;
    }

    private static List<ExpectedOrder> GetFallbackSeedOrders()
    {
        var today = new DateTime(2026, 10, 5);
        return new List<ExpectedOrder>
        {
            new() { Id = 1, JobNumber = "251078C", PoNumber = "0430", VendorName = "Pickford", PackingSlipNumber = "PK-99120", LoadType = "Flanges & Fittings", ExpectedDate = today.AddDays(-3), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 2, JobNumber = "251078C", PoNumber = "0438", VendorName = "Precision Fabrication", PackingSlipNumber = "16748-001", LoadType = "Supports", ExpectedDate = today.AddDays(-3), HasLandedInYard = true, ActualArrivalDate = today.AddDays(-3).AddHours(14), YardStatus = "Landed in Yard", ReceivingStatus = "Pending Inspection", ClientName = "CNRL" },
            new() { Id = 3, JobNumber = "251078C", PoNumber = "0428", VendorName = "Calgary Fasteners", PackingSlipNumber = "CF-88201", LoadType = "SNG / Bolt-Up", ExpectedDate = today.AddDays(-3), HasLandedInYard = true, ActualArrivalDate = today.AddDays(-3).AddHours(10), YardStatus = "Landed in Yard", ReceivingStatus = "Pending Inspection", ClientName = "CNRL" },
            new() { Id = 4, JobNumber = "251078C", PoNumber = "0420", VendorName = "Master Flo Valve", PackingSlipNumber = "80225923", LoadType = "Valves", ExpectedDate = today, HasLandedInYard = true, ActualArrivalDate = today.AddHours(9), YardStatus = "Landed in Yard", ReceivingStatus = "Pending Inspection", ClientName = "CNRL" },
            new() { Id = 5, JobNumber = "251078C", PoNumber = "0425", VendorName = "Spartan Controls", PackingSlipNumber = "80789122", LoadType = "Instrumentation", ExpectedDate = today, HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 6, JobNumber = "251078C", PoNumber = "0431", VendorName = "CFP Industries", PackingSlipNumber = "CFP-3021", LoadType = "Pipe", ExpectedDate = today.AddDays(1), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 7, JobNumber = "251078C", PoNumber = "0435", VendorName = "Comco Pipe & Supply", PackingSlipNumber = "1368291-A", LoadType = "Supports", ExpectedDate = today.AddDays(1), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 8, JobNumber = "251078C", PoNumber = "0442", VendorName = "Trans Am Piping Products Ltd", PackingSlipNumber = "C26-34702", LoadType = "Flanges & Fittings", ExpectedDate = today.AddDays(2), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 9, JobNumber = "251078C", PoNumber = "0426", VendorName = "Grayloc Products", PackingSlipNumber = "GL-4402", LoadType = "Flanges & Fittings", ExpectedDate = today.AddDays(3), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 10, JobNumber = "251078C", PoNumber = "0440", VendorName = "Armor Alloys", PackingSlipNumber = "SC-119284", LoadType = "Flanges & Fittings", ExpectedDate = today.AddDays(4), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 11, JobNumber = "251078C", PoNumber = "0433", VendorName = "Comco Pipe & Supply", PackingSlipNumber = "1359102", LoadType = "Valves", ExpectedDate = today.AddDays(-10), HasLandedInYard = false, YardStatus = "Past Due", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 12, JobNumber = "251078C", PoNumber = "0434", VendorName = "Trans Am Piping Products Ltd", PackingSlipNumber = "C26-34410", LoadType = "Pipe", ExpectedDate = today.AddDays(-7), HasLandedInYard = false, YardStatus = "Past Due", ReceivingStatus = "Not Received", ClientName = "CNRL" }
        };
    }

    private class RecLogEntry
    {
        public string Job { get; set; } = "";
        public string Po { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string PackingList { get; set; } = "";
        public string LoadType { get; set; } = "";
        public string ActualArrival { get; set; } = "";
        public string BeginReceiving { get; set; } = "";
        public string EndReceiving { get; set; } = "";
        public string MrrCompleted { get; set; } = "";
        public string RecId { get; set; } = "";
        public string Unloader { get; set; } = "";
        public string Receiver { get; set; } = "";
        public string Link { get; set; } = "";
    }
}
