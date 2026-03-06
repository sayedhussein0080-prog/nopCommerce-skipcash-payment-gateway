using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Nop.Plugin.Payments.SkipCash.Services;

/// <summary>
/// Represents the HTTP client for SkipCash API interaction
/// </summary>
public class SkipCashHttpClient
{
    #region Fields

    private readonly HttpClient _httpClient;
    private readonly SkipCashPaymentSettings _settings;
    private readonly ILogger<SkipCashHttpClient> _logger;

    // Serialization options for outgoing requests - use PascalCase (matching SkipCash API)
    private static readonly JsonSerializerOptions _serializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Deserialization options for incoming responses - case insensitive
    private static readonly JsonSerializerOptions _deserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    #endregion

    #region Ctor

    public SkipCashHttpClient(HttpClient httpClient,
        SkipCashPaymentSettings settings,
        ILogger<SkipCashHttpClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        // Set base address
        var baseUrl = !string.IsNullOrEmpty(settings.ApiBaseUrl)
            ? settings.ApiBaseUrl
            : (settings.UseSandbox
                ? "https://skipcashtest.azurewebsites.net"
                : "https://api.skipcash.app");

        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    #endregion

    #region Methods

    /// <summary>
    /// Creates an online payment via SkipCash API
    /// </summary>
    /// <param name="request">The payment creation request</param>
    /// <returns>The API response containing payment URL</returns>
    public async Task<SkipCashCreatePaymentResponse> CreatePaymentAsync(SkipCashCreatePaymentRequest request)
    {
        try
        {
            // Set the keyId from settings
            request.KeyId = _settings.KeyId;
            // Uid should be a random UUID per request (used to randomize the signature)
            request.Uid = Guid.NewGuid().ToString();

            // Generate authorization signature
            var signature = GenerateSignature(request);

            var json = JsonSerializer.Serialize(request, _serializeOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // DEBUG: Log the full request body being sent
            _logger.LogWarning("SkipCash DEBUG - Request JSON: {Json}", json);
            _logger.LogWarning("SkipCash DEBUG - API URL: {Url}", _httpClient.BaseAddress + "api/v1/payments");

            // Add authorization header
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", signature);

            var response = await _httpClient.PostAsync("api/v1/payments", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("SkipCash CreatePayment response: {StatusCode} - {Content}",
                response.StatusCode, responseContent);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("SkipCash CreatePayment failed: {StatusCode} - {Content}",
                    response.StatusCode, responseContent);

                return new SkipCashCreatePaymentResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"API Error: {response.StatusCode} - {responseContent}"
                };
            }

            var result = JsonSerializer.Deserialize<SkipCashApiResponse>(responseContent, _deserializeOptions);

            return new SkipCashCreatePaymentResponse
            {
                IsSuccess = true,
                PaymentId = result?.ResultObj?.Id?.ToString(),
                PaymentUrl = result?.ResultObj?.PayUrl,
                StatusId = result?.ResultObj?.StatusId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating SkipCash payment");
            return new SkipCashCreatePaymentResponse
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Gets payment details from SkipCash
    /// </summary>
    /// <param name="paymentId">The SkipCash payment ID</param>
    /// <returns>Payment details</returns>
    public async Task<SkipCashPaymentDetails> GetPaymentDetailsAsync(string paymentId)
    {
        try
        {
            // Generate signature for GET request
            var signatureData = $"{paymentId},{_settings.KeyId}";
            var signature = ComputeHmacSha256(signatureData, _settings.KeySecret);

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", signature);

            var response = await _httpClient.GetAsync($"api/v1/payments/{paymentId}");
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("SkipCash GetPaymentDetails response: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("SkipCash GetPaymentDetails failed: {Content}", responseContent);
                return null;
            }

            var result = JsonSerializer.Deserialize<SkipCashApiResponse>(responseContent, _deserializeOptions);
            return result?.ResultObj;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SkipCash payment details for {PaymentId}", paymentId);
            return null;
        }
    }

    /// <summary>
    /// Refunds a payment via SkipCash API
    /// </summary>
    /// <param name="paymentId">SkipCash payment ID</param>
    /// <param name="amount">Amount to refund (null for full refund)</param>
    /// <returns>True if successful</returns>
    public async Task<bool> RefundPaymentAsync(string paymentId, decimal? amount = null)
    {
        try
        {
            var request = new
            {
                id = paymentId,
                keyId = _settings.KeyId,
                amount = amount
            };

            var signatureData = $"{paymentId},{_settings.KeyId},{amount}";
            var signature = ComputeHmacSha256(signatureData, _settings.KeySecret);

            var json = JsonSerializer.Serialize(request, _serializeOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", signature);

            var response = await _httpClient.PostAsync("api/v1/payments/refund", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("SkipCash Refund response: {StatusCode} - {Content}",
                response.StatusCode, responseContent);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refunding SkipCash payment {PaymentId}", paymentId);
            return false;
        }
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Generates HMACSHA256 authorization signature for payment creation
    /// Per SkipCash docs: combine fields as comma-separated string, encrypt with HMACSHA256, convert to base64
    /// </summary>
    private string GenerateSignature(SkipCashCreatePaymentRequest request)
    {
        // Build the signature string as per SkipCash documentation
        // Format: key=value pairs separated by commas, only non-empty fields
        // Order: Uid, KeyId, Amount, FirstName, LastName, Phone, Email, Street, City, State, Country, PostalCode, TransactionId, Custom1
        var keyValuePairs = new List<KeyValuePair<string, string>>
        {
            new("Uid", request.Uid),
            new("KeyId", request.KeyId),
            new("Amount", request.Amount),
            new("FirstName", request.FirstName),
            new("LastName", request.LastName),
            new("Phone", request.Phone),
            new("Email", request.Email),
            new("Street", request.Street),
            new("City", request.City),
            new("State", request.State),
            new("Country", request.Country),
            new("PostalCode", request.PostalCode),
            new("TransactionId", request.TransactionId),
            new("Custom1", request.Custom1)
        };

        // Only include non-empty fields as per SkipCash docs
        var signatureData = string.Join(",",
            keyValuePairs
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .Select(kv => $"{kv.Key}={kv.Value}"));

        _logger.LogInformation("SkipCash signature data (masked): fields count = {Count}", 
            keyValuePairs.Count(kv => !string.IsNullOrEmpty(kv.Value)));

        return ComputeHmacSha256(signatureData, _settings.KeySecret);
    }

    /// <summary>
    /// Computes HMAC-SHA256 hash and returns base64 encoded result
    /// Per SkipCash reference implementation: key is used as raw string (UTF-8 bytes)
    /// </summary>
    private static string ComputeHmacSha256(string data, string key)
    {
        // Use key as raw UTF-8 bytes (matching PHP hash_hmac behavior)
        var keyBytes = Encoding.UTF8.GetBytes(key);
        using var hmac = new HMACSHA256(keyBytes);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var hashBytes = hmac.ComputeHash(dataBytes);
        return Convert.ToBase64String(hashBytes);
    }

    #endregion
}

#region Request / Response Models

/// <summary>
/// Represents a request to create an online payment
/// </summary>
public class SkipCashCreatePaymentRequest
{
    public string Uid { get; set; }
    public string KeyId { get; set; }
    public string Amount { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public string Street { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string Country { get; set; }
    public string PostalCode { get; set; }
    public string TransactionId { get; set; }
    public string Custom1 { get; set; }
    public string Custom2 { get; set; }
    public string Custom3 { get; set; }
    public string Subject { get; set; }
    public string Description { get; set; }
    public string WebHookUrl { get; set; }
    public string ReturnUrl { get; set; }
}

/// <summary>
/// Represents the response from creating a payment
/// </summary>
public class SkipCashCreatePaymentResponse
{
    public bool IsSuccess { get; set; }
    public string PaymentId { get; set; }
    public string PaymentUrl { get; set; }
    public int? StatusId { get; set; }
    public string ErrorMessage { get; set; }
}

/// <summary>
/// Represents the SkipCash API wrapper response
/// </summary>
public class SkipCashApiResponse
{
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public string Message { get; set; }
    public SkipCashPaymentDetails ResultObj { get; set; }
}

/// <summary>
/// Represents payment details returned by SkipCash
/// </summary>
public class SkipCashPaymentDetails
{
    public Guid? Id { get; set; }
    public string PayUrl { get; set; }
    public int? StatusId { get; set; }
    public string Status { get; set; }
    [JsonConverter(typeof(SkipCashDecimalConverter))]
    public decimal? Amount { get; set; }
    public string TransactionId { get; set; }
    public string Custom1 { get; set; }
    public string Custom2 { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
}

/// <summary>
/// Represents a SkipCash webhook notification payload
/// </summary>
public class SkipCashWebhookPayload
{
    public Guid? Id { get; set; }
    public string PaymentId { get; set; }
    public int? StatusId { get; set; }
    public string Status { get; set; }
    [JsonConverter(typeof(SkipCashDecimalConverter))]
    public decimal? Amount { get; set; }
    public string TransactionId { get; set; }
    public string Custom1 { get; set; }
    public string Custom2 { get; set; }
    public string Signature { get; set; }
}

/// <summary>
/// Converter to handle decimal parsing from string safely.
/// </summary>
public class SkipCashDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var d))
            return d;
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str)) return null;
            if (decimal.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val)) return val;
        }
        return 0m; // dummy match on failure
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

#endregion
