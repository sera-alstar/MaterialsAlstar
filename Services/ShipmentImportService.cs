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

        var clientLookup = LoadActiveJobs(activeJobsFile);
        var recLogLookup = LoadRecLogLookup(recLogFile);

        if (File.Exists(expFile))
        {
            orders = ParseExpectedReceivingCsv(expFile, recLogLookup, clientLookup);
        }

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

                    while (recordsEnum.MoveNext())
                    {
                        var cols = recordsEnum.Current;
                        if (jobIdx < 0 || poIdx < 0 || cols.Count <= Math.Max(jobIdx, poIdx)) continue;

                        var job = cols[jobIdx].Trim();
                        var po = cols[poIdx].Trim();
                        if (string.IsNullOrEmpty(job) || string.IsNullOrEmpty(po)) continue;

                        var vendor = vendorIdx >= 0 && cols.Count > vendorIdx ? cols[vendorIdx].Trim() : "";
                        var pl = plIdx >= 0 && cols.Count > plIdx ? cols[plIdx].Trim() : "";
                        var loadType = loadTypeIdx >= 0 && cols.Count > loadTypeIdx ? cols[loadTypeIdx].Trim() : "";
                        var actualStr = actualIdx >= 0 && cols.Count > actualIdx ? cols[actualIdx].Trim() : "";
                        var recId = recIdIdx >= 0 && cols.Count > recIdIdx ? cols[recIdIdx].Trim() : "";
                        var receiver = receiverIdx >= 0 && cols.Count > receiverIdx ? cols[receiverIdx].Trim() : "";

                        loadType = StandardizeLoadType(loadType, "", vendor);
                        if (IsStructuralSteel(loadType, vendor, "")) continue;

                        var arrivalDate = ParseDate(actualStr);
                        if (!arrivalDate.HasValue) continue;

                        // Focus on active/recent landed shipments for 2026
                        if (arrivalDate.Value.Year < 2026) continue;

                        var client = "";
                        if (clientLookup.TryGetValue(job, out var c)) client = c;

                        var staging = AssignStagingLocation(loadType);

                        entries.Add(new ReceivingLogEntry
                        {
                            Id = idCounter++,
                            JpKey = $"{job}{po}",
                            JobNumber = job,
                            PoNumber = po,
                            VendorName = vendor,
                            PackingSlipNumber = string.IsNullOrWhiteSpace(pl) ? $"PS-{po}" : pl,
                            BolNumber = $"BOL-{po}",
                            PalletCount = loadType.Contains("SNG") ? 2 : 1,
                            CrateCount = loadType.Contains("Valves") ? 1 : 0,
                            BoxCount = loadType.Contains("Pipe") ? 0 : 2,
                            LoadType = loadType,
                            StagingLocation = staging,
                            ArrivalDateTime = arrivalDate.Value,
                            Status = arrivalDate.Value >= new DateTime(2026, 10, 1)
                                ? (!string.IsNullOrEmpty(receiver) ? "In Inspection" : "Landed - Pending Inspection")
                                : "Complete",
                            IsAdminVerified = true,
                            ClientName = client,
                            ReceiverName = receiver,
                            RecId = recId
                        });
                    }
                }
            }
            catch { }
        }

        // Add 2 pending admin verification sample entries for client-supplied shipments without known job#
        entries.Add(new ReceivingLogEntry
        {
            Id = idCounter++,
            JpKey = "UNKNOWN-0091",
            JobNumber = null, // Client supplied - job unknown
            PoNumber = "0091",
            VendorName = "Emerson Automation Solutions",
            PackingSlipNumber = "EM-99201",
            BolNumber = "BOL-EM-8821",
            PalletCount = 1,
            CrateCount = 1,
            BoxCount = 0,
            LoadType = "Instrumentation",
            StagingLocation = "Walkway",
            ArrivalDateTime = new DateTime(2026, 10, 5, 14, 30, 0),
            Status = "Pending Admin Verification",
            IsAdminVerified = false,
            Notes = "Client-supplied instrumentation. Truck driver had no job# on BOL. Staged on Walkway for inspection."
        });

        entries.Add(new ReceivingLogEntry
        {
            Id = idCounter++,
            JpKey = "UNKNOWN-0104",
            JobNumber = null,
            PoNumber = "0104",
            VendorName = "Calgary Fasteners",
            PackingSlipNumber = "CF-40912",
            BolNumber = "BOL-CF-3301",
            PalletCount = 2,
            CrateCount = 0,
            BoxCount = 4,
            LoadType = "SNG / Bolt-Up",
            StagingLocation = "Walkway",
            ArrivalDateTime = new DateTime(2026, 10, 5, 15, 10, 0),
            Status = "Pending Admin Verification",
            IsAdminVerified = false,
            Notes = "Urgent bolt-up shipment arrived directly from courier. Needs Job# assignment."
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

            var clientName = "";
            if (clientLookup.TryGetValue(job, out var cName)) clientName = cName;
            else if (clientLookup.TryGetValue(job.TrimEnd('C', 'c'), out var cName2)) clientName = cName2;

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

    private static List<ExpectedOrder> GetFallbackSeedOrders()
    {
        var today = new DateTime(2026, 10, 5);
        return new List<ExpectedOrder>
        {
            new() { Id = 1, JobNumber = "251078C", PoNumber = "0430", VendorName = "Pickford", PackingSlipNumber = "PK-99120", LoadType = "Flanges & Fittings", ExpectedDate = today.AddDays(-3), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 2, JobNumber = "261030C", PoNumber = "0260", VendorName = "Sureway Metals", PackingSlipNumber = "SW-45210", LoadType = "Supports", ExpectedDate = today.AddDays(-3), HasLandedInYard = true, ActualArrivalDate = today.AddDays(-3).AddHours(14), YardStatus = "Landed in Yard", ReceivingStatus = "Pending Inspection", ClientName = "Enbridge" },
            new() { Id = 3, JobNumber = "261030C", PoNumber = "0262", VendorName = "Calgary Fasteners", PackingSlipNumber = "CF-88201", LoadType = "SNG / Bolt-Up", ExpectedDate = today.AddDays(-3), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "Enbridge" },
            new() { Id = 4, JobNumber = "241143C", PoNumber = "0650", VendorName = "Comco Pipe & Supply", PackingSlipNumber = "1368291-A", LoadType = "Valves", ExpectedDate = today, HasLandedInYard = true, ActualArrivalDate = today.AddHours(9), YardStatus = "Landed in Yard", ReceivingStatus = "Pending Inspection", ClientName = "CNRL" },
            new() { Id = 5, JobNumber = "261037C", PoNumber = "0280", VendorName = "Armor Alloys", PackingSlipNumber = "SC-119284", LoadType = "Flanges & Fittings", ExpectedDate = today, HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CONOCO" },
            new() { Id = 6, JobNumber = "261073C", PoNumber = "0056", VendorName = "CFP Industries", PackingSlipNumber = "CFP-3021", LoadType = "Pipe & Fittings", ExpectedDate = today, HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "Cenovus" },
            new() { Id = 7, JobNumber = "241143C", PoNumber = "0651", VendorName = "Precision Piping", PackingSlipNumber = "PP-7721", LoadType = "Pipe", ExpectedDate = today, HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 8, JobNumber = "261037C", PoNumber = "0288", VendorName = "Quest Metals", PackingSlipNumber = "QM-1092", LoadType = "Supports", ExpectedDate = today.AddDays(1), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CONOCO" },
            new() { Id = 9, JobNumber = "261030C", PoNumber = "0265", VendorName = "Apex Distribution", PackingSlipNumber = "APX-88231", LoadType = "Valves", ExpectedDate = today.AddDays(1), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "Enbridge" },
            new() { Id = 10, JobNumber = "251078C", PoNumber = "0426", VendorName = "Grayloc Products", PackingSlipNumber = "GL-4402", LoadType = "Flanges & Fittings", ExpectedDate = today.AddDays(3), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "CNRL" },
            new() { Id = 11, JobNumber = "261030C", PoNumber = "0278", VendorName = "Spartan Controls", PackingSlipNumber = "80789122", LoadType = "Instrumentation", ExpectedDate = today.AddDays(2), HasLandedInYard = false, YardStatus = "In Transit", ReceivingStatus = "Not Received", ClientName = "Enbridge" },
            new() { Id = 12, JobNumber = "261073C", PoNumber = "0042", VendorName = "Comco Pipe & Supply", PackingSlipNumber = "1359102", LoadType = "Valves", ExpectedDate = today.AddDays(-6), HasLandedInYard = false, YardStatus = "Past Due", ReceivingStatus = "Not Received", ClientName = "Cenovus" }
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
