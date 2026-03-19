using System.Text.Json;

namespace it15_webproject_mvc.Services
{
    public interface IDataCleansingService
    {
        /// <summary>
        /// Cleanses a raw JSON row: removes nulls, trims strings, removes empty values.
        /// Returns null if the row cannot be parsed.
        /// </summary>
        Dictionary<string, object?>? CleanseRow(string rawJson);
    }

    public class DataCleansingService : IDataCleansingService
    {
        public Dictionary<string, object?>? CleanseRow(string rawJson)
        {
            Dictionary<string, JsonElement>? rawDict;
            try
            {
                rawDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawJson);
            }
            catch
            {
                return null;
            }

            if (rawDict == null)
                return null;

            var cleanDict = new Dictionary<string, object?>();

            foreach (var kvp in rawDict)
            {
                // Remove null values
                if (kvp.Value.ValueKind == JsonValueKind.Null)
                    continue;

                // Trim strings and remove empty ones
                if (kvp.Value.ValueKind == JsonValueKind.String)
                {
                    var strVal = kvp.Value.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(strVal))
                        cleanDict[kvp.Key] = strVal;
                }
                else
                {
                    cleanDict[kvp.Key] = kvp.Value;
                }
            }

            return cleanDict;
        }
    }
}
