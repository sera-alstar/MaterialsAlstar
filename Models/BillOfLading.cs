using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaterialsAlstar.Models;

public class BillOfLading
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "BOL Number")]
    public string BolNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Job #")]
    public string JobNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Ship From")]
    public string ShipFrom { get; set; } = "Alstar Group - Calgary Fab Shop\n8700 Venture Ave\nCalgary, AB T3S 0A2";

    [Required]
    [Display(Name = "Ship To")]
    public string ShipTo { get; set; } = "Cenovus";

    [Display(Name = "Batch ID")]
    public int BatchId { get; set; }

    /// <summary>
    /// Stores a list of integers and ranges of integers (e.g. "1, 3, 5-8, 12").
    /// </summary>
    [Required]
    [Display(Name = "Priorities")]
    public string Priorities { get; set; } = string.Empty;

    /// <summary>
    /// "Shiploose" or "Assembly"
    /// </summary>
    [Required]
    [Display(Name = "Type")]
    public string Type { get; set; } = "Shiploose";

    [Required]
    [Display(Name = "Kitter")]
    public string Kitter { get; set; } = string.Empty;

    [Display(Name = "Pallet Count")]
    public int PalletCount { get; set; }

    [Display(Name = "Crate Count")]
    public int CrateCount { get; set; }

    [Display(Name = "Date Created")]
    public DateTime DateCreated { get; set; } = DateTime.Now;

    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public static string GenerateNextBolNumber(IEnumerable<string> existingBolNumbers, DateTime now)
    {
        string yy = (now.Year % 100).ToString("00");
        string prefix = $"AO{yy}.BOL";
        int maxSeq = 0;

        foreach (var bol in existingBolNumbers)
        {
            if (string.IsNullOrWhiteSpace(bol)) continue;
            var trimmed = bol.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string numPart = trimmed.Substring(prefix.Length);
                if (int.TryParse(numPart, out int seq) && seq > maxSeq)
                {
                    maxSeq = seq;
                }
            }
            else
            {
                var match = Regex.Match(trimmed, @"^AO\d{2}\.BOL(\d+)$", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int seq) && seq > maxSeq)
                {
                    maxSeq = seq;
                }
            }
        }

        return $"{prefix}{(maxSeq + 1):000}";
    }

    private static string GetPersistenceFilePath()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MaterialsAlstar");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "bills_of_lading.json");
    }

    public static List<BillOfLading>? LoadPersistedBols()
    {
        try
        {
            var path = GetPersistenceFilePath();
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<BillOfLading>>(json);
            return (items != null && items.Count > 0) ? items : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SavePersistedBols(IEnumerable<BillOfLading> bols)
    {
        try
        {
            var path = GetPersistenceFilePath();
            var json = JsonSerializer.Serialize(bols.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }
}
