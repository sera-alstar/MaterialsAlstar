using Microsoft.EntityFrameworkCore;
using MaterialsAlstar.Models;

namespace MaterialsAlstar.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<MaterialItem> Materials => Set<MaterialItem>();
    public DbSet<ExpectedOrder> ExpectedOrders => Set<ExpectedOrder>();
    public DbSet<ReceivingLogEntry> ReceivingLogs => Set<ReceivingLogEntry>();
}


