using it15_webproject_mvc.Data;
using it15_webproject_mvc.Data;
using Microsoft.EntityFrameworkCore;
using static it15_webproject_mvc.Constants.StatusConstants;

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
        private readonly ApplicationDbContext _context;

        public WarehouseSummaryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<WarehouseSummaryItem>> GetSummaryAsync(int orgId, bool includeStatus, bool includeBatchCount)
        {
            var warehouseRows = await _context.WarehouseRecords
                .AsNoTracking()
                .Where(w => w.OrganizationID == orgId && w.RecordStatus == StatusActive)
                .Select(w => new
                {
                    w.TargetTable,
                    w.DataSourceID,
                    w.Loaded_at,
                    w.Version,
                    w.BatchId
                })
                .ToListAsync();

            if (warehouseRows.Count == 0)
            {
                return [];
            }

            var sourceIds = warehouseRows.Select(w => w.DataSourceID).Distinct().ToList();
            var sources = await _context.DataSources
                .AsNoTracking()
                .Where(s => sourceIds.Contains(s.DataSourceID))
                .Select(s => new { s.DataSourceID, s.SourceName, s.Status })
                .ToDictionaryAsync(s => s.DataSourceID);

            return warehouseRows
                .GroupBy(w => new { w.TargetTable, w.DataSourceID })
                .Select(g =>
                {
                    sources.TryGetValue(g.Key.DataSourceID, out var source);
                    return new WarehouseSummaryItem(
                        source?.SourceName ?? "Unknown",
                        g.Key.TargetTable,
                        g.Max(x => x.Loaded_at),
                        g.Count(),
                        g.Max(x => x.Version),
                        includeStatus ? source?.Status : null,
                        includeBatchCount ? g.Select(x => x.BatchId).Distinct().Count() : null);
                })
                .OrderByDescending(s => s.TotalRows)
                .ToList();
        }
    }
}
