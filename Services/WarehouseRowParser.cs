using System.Text.Json;
using it15_webproject_mvc.Models;
using Microsoft.Extensions.Logging;

namespace it15_webproject_mvc.Services
{
    public static class WarehouseRowParser
    {
        public static List<Dictionary<string, string>> ParseWarehouseRows(
            IEnumerable<WarehouseRecord> warehouseRows,
            List<string> allColumns,
            ILogger? logger = null)
        {
            var parsedRows = new List<Dictionary<string, string>>();

            foreach (var row in warehouseRows)
            {
                if (TryParseWarehouseRow(row, allColumns, out var rowData, logger))
                {
                    parsedRows.Add(rowData);
                }
            }

            return parsedRows;
        }

        private static bool TryParseWarehouseRow(
            WarehouseRecord row,
            List<string> allColumns,
            out Dictionary<string, string> rowData,
            ILogger? logger)
        {
            rowData = new Dictionary<string, string>();

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.CleanData);
                if (dict == null)
                {
                    return false;
                }

                foreach (var kvp in dict)
                {
                    if (!allColumns.Contains(kvp.Key))
                    {
                        allColumns.Add(kvp.Key);
                    }

                    rowData[kvp.Key] = kvp.Value.ValueKind switch
                    {
                        JsonValueKind.String => kvp.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => kvp.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => string.Empty,
                        _ => kvp.Value.GetRawText()
                    };
                }

                return true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse warehouse record {RecordId}", row.WarehouseRecordID);
                return false;
            }
        }
    }
}
