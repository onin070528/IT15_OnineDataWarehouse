using it15_webproject_mvc.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace it15_webproject_mvc.Services
{
    public sealed record WarehouseTableData(
        List<string> Columns,
        List<Dictionary<string, string>> Rows,
        int RowCount);

    public class WarehouseTableService
    {
        private const string StatusActive = "Active";
        private readonly ApplicationDbContext _context;
        private readonly ILogger<WarehouseTableService> _logger;

        public WarehouseTableService(ApplicationDbContext context, ILogger<WarehouseTableService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<WarehouseTableData> GetWarehouseTableDataAsync(int orgId, string viewTable)
        {
            var warehouseRows = await _context.WarehouseRecords
                .AsNoTracking()
                .Where(w => w.OrganizationID == orgId && w.TargetTable == viewTable && w.RecordStatus == StatusActive)
                .OrderByDescending(w => w.Loaded_at)
                .ThenBy(w => w.RowNumber)
                .Take(100)
                .ToListAsync();

            var allColumns = new List<string>();
            var parsedRows = WarehouseRowParser.ParseWarehouseRows(warehouseRows, allColumns, _logger);

            return new WarehouseTableData(allColumns, parsedRows, warehouseRows.Count);
        }
    }
}
