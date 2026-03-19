using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace it15_webproject_mvc.Services
{
    public class PayMongoSettings
    {
        public string SecretKey { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
    }

    public class PayMongoService
    {
        private readonly HttpClient _httpClient;
        private readonly PayMongoSettings _settings;
        private readonly ILogger<PayMongoService> _logger;
        private const string BaseUrl = "https://api.paymongo.com/v1";

        public PayMongoService(HttpClient httpClient, IConfiguration configuration, ILogger<PayMongoService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _settings = new PayMongoSettings();
            configuration.GetSection("PayMongo").Bind(_settings);

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.SecretKey}:"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public string PublicKey => _settings.PublicKey;

        /// <summary>
        /// Creates a PayMongo Checkout Session and returns the checkout URL and session ID.
        /// </summary>
        public async Task<(string? CheckoutUrl, string? SessionId, string? Error)> CreateCheckoutSession(
            string planName,
            long amountInCentavos,
            string currency,
            string description,
            string successUrl,
            string cancelUrl)
        {
            try
            {
                var payload = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            send_email_receipt = true,
                            show_description = true,
                            show_line_items = true,
                            description = description,
                            line_items = new[]
                            {
                                new
                                {
                                    currency = currency,
                                    amount = amountInCentavos,
                                    name = planName,
                                    quantity = 1,
                                    description = description
                                }
                            },
                            payment_method_types = new[] { "gcash", "grab_pay", "paymaya", "card" },
                            success_url = successUrl,
                            cancel_url = cancelUrl
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{BaseUrl}/checkout_sessions", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("PayMongo API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                    return (null, null, $"PayMongo API error: {response.StatusCode}");
                }

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                var sessionId = root.GetProperty("data").GetProperty("id").GetString();
                var checkoutUrl = root.GetProperty("data").GetProperty("attributes").GetProperty("checkout_url").GetString();

                return (checkoutUrl, sessionId, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create PayMongo checkout session");
                return (null, null, ex.Message);
            }
        }

        /// <summary>
        /// Retrieves a checkout session by ID to verify payment status.
        /// </summary>
        public async Task<(string? Status, string? PaymentId, string? Error)> GetCheckoutSession(string sessionId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/checkout_sessions/{sessionId}");
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("PayMongo API error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                    return (null, null, $"PayMongo API error: {response.StatusCode}");
                }

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                var attributes = root.GetProperty("data").GetProperty("attributes");

                var status = attributes.GetProperty("status").GetString();

                string? paymentId = null;
                if (attributes.TryGetProperty("payments", out var payments) && payments.GetArrayLength() > 0)
                {
                    paymentId = payments[0].GetProperty("id").GetString();
                }
                else if (attributes.TryGetProperty("payment_intent", out var paymentIntent))
                {
                    if (paymentIntent.ValueKind != JsonValueKind.Null)
                    {
                        paymentId = paymentIntent.GetProperty("id").GetString();
                    }
                }

                return (status, paymentId, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve PayMongo checkout session");
                return (null, null, ex.Message);
            }
        }
    }
}
