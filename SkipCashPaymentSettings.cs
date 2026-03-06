using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.SkipCash;

/// <summary>
/// Represents settings of the SkipCash payment plugin
/// </summary>
public class SkipCashPaymentSettings : ISettings
{
    /// <summary>
    /// Gets or sets the SkipCash API base URL
    /// </summary>
    public string ApiBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the SkipCash Key ID (from merchant portal)
    /// </summary>
    public string KeyId { get; set; }

    /// <summary>
    /// Gets or sets the SkipCash Key Secret (from merchant portal)
    /// </summary>
    public string KeySecret { get; set; }

    /// <summary>
    /// Gets or sets the SkipCash Merchant UID
    /// </summary>
    public string MerchantUid { get; set; }

    /// <summary>
    /// Gets or sets the Webhook Key for verifying webhook signatures
    /// </summary>
    public string WebhookKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use the sandbox/test environment
    /// </summary>
    public bool UseSandbox { get; set; }

    /// <summary>
    /// Gets or sets an additional fee
    /// </summary>
    public decimal AdditionalFee { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to "additional fee" is specified as percentage
    /// </summary>
    public bool AdditionalFeePercentage { get; set; }
}
