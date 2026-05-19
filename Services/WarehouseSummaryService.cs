using it15_webproject_mvc.Data;
using Microsoft.EntityFrameworkCore;

namespace it15_webproject_mvc.Services
{
    public sealed record WarehouseSummaryItem(
        string SourceName,
        string TargetTable,
        DateTime LastLoaded,
        int TotalRows,
        int LatestVersion,
        string? Status = null,
        int? BatchCount = null);

    public class WarehouseSummaryService
    {
        private const string StatusActive = "Active";
        private readonly ApplicationDbContext _context;

        public WarehouseSummaryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<WarehouseSummaryItem>> GetSummaryAsync(int orgId, bool includeStatus, bool includeBatchCount)
        {
            return await _context.WarehouseRecords
                .AsNoTracking()
                .Include(w => w.DataSource)
                .Where(w => w.OrganizationID == orgId && w.RecordStatus == StatusActive)
                .GroupBy(w => new { w.TargetTable, w.DataSourceID })
                .Select(g => new WarehouseSummaryItem(
                    g.First().DataSource!.SourceName,
                    g.Key.TargetTable,
                    g.Max(w => w.Loaded_at),
                    g.Count(),
                    g.Max(w => w.Version),
                    includeStatus ? g.First().DataSource!.Status : null,
                    includeBatchCount ? g.Select(w => w.BatchId).Distinct().Count() : null))
                .OrderByDescending(s => s.TotalRows)
                .ToListAsync();
        }
    }
}
