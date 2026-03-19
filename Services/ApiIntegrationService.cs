using System.Net.Http.Headers;
using System.Text.Json;

namespace it15_webproject_mvc.Services
{
    public class ApiIntegrationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ApiIntegrationService> _logger;

        public ApiIntegrationService(HttpClient httpClient, ILogger<ApiIntegrationService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        private HttpRequestMessage CreateRequest(string baseUrl, string endpoint, string apiKey, string authMethod)
        {
            var requestUrl = baseUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            switch (authMethod)
            {
                case "Bearer":
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    break;
                case "ApiKey":
                    request.Headers.Add("X-API-Key", apiKey);
                    break;
                case "Basic":
                    var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(apiKey));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                    break;
            }

            return request;
        }

        public async Task<ApiPullResult> PullDataAsync(string baseUrl, string endpoint, string apiKey, string authMethod)
        {
            var result = new ApiPullResult();

            try
            {
                var request = CreateRequest(baseUrl, endpoint, apiKey, authMethod);
                var response = await _httpClient.SendAsync(request);

                result.StatusCode = (int)response.StatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    result.Success = false;
                    result.ErrorMessage = $"API returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
                    return result;
                }

                var content = await response.Content.ReadAsStringAsync();

                // Try to parse JSON response into rows
                result.Rows = ParseJsonToRows(content);
                result.RawResponse = content;
                result.Success = true;
                result.RowCount = result.Rows.Count;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error pulling data from API");
                result.Success = false;
                result.ErrorMessage = $"Connection error: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Request timed out. The API did not respond in time.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error pulling data from API");
                result.Success = false;
                result.ErrorMessage = $"Unexpected error: {ex.Message}";
            }

            return result;
        }

        public async Task<ApiPullResult> TestConnectionAsync(string baseUrl, string endpoint, string apiKey, string authMethod)
        {
            var result = new ApiPullResult();

            try
            {
                var request = CreateRequest(baseUrl, endpoint, apiKey, authMethod);
                var response = await _httpClient.SendAsync(request);
                result.StatusCode = (int)response.StatusCode;
                result.Success = response.IsSuccessStatusCode;

                if (!response.IsSuccessStatusCode)
                {
                    result.ErrorMessage = $"API returned {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private List<Dictionary<string, object?>> ParseJsonToRows(string json)
        {
            var rows = new List<Dictionary<string, object?>>();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Handle array response: [{ ... }, { ... }]
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    rows.Add(FlattenJsonElement(element));
                }
            }
            // Handle object with a data array: { "data": [{ ... }] } or { "results": [{ ... }] }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                JsonElement dataArray = default;
                bool found = false;

                // Look for common wrapper property names
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        dataArray = prop.Value;
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    foreach (var element in dataArray.EnumerateArray())
                    {
                        rows.Add(FlattenJsonElement(element));
                    }
                }
                else
                {
                    // Single object response — treat as one row
                    rows.Add(FlattenJsonElement(root));
                }
            }

            return rows;
        }

        private Dictionary<string, object?> FlattenJsonElement(JsonElement element)
        {
            var dict = new Dictionary<string, object?>();

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetDecimal(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
            }

            return dict;
        }
    }

    public class ApiPullResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RawResponse { get; set; }
        public int StatusCode { get; set; }
        public int RowCount { get; set; }
        public List<Dictionary<string, object?>> Rows { get; set; } = [];
    }
}
