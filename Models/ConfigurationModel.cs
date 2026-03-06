using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Payments.SkipCash.Models;

public record ConfigurationModel : BaseNopModel
{
    public int ActiveStoreScopeConfiguration { get; set; }

    [NopResourceDisplayName("Plugins.Payment.SkipCash.Fields.ApiBaseUrl")]
    public string ApiBaseUrl { get; set; }
    public bool ApiBaseUrl_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.SkipCash.Fields.KeyId")]
    public string KeyId { get; set; }
    public bool KeyId_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.SkipCash.Fields.KeySecret")]
    public string KeySecret { get; set; }
    public bool KeySecret_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.SkipCash.Fields.MerchantUid")]
    public string MerchantUid { get; set; }
    public bool MerchantUid_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.SkipCash.Fields.WebhookKey")]
    public string WebhookKey { get; set; }
    public bool WebhookKey_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.SkipCash.Fields.UseSandbox")]
    public bool UseSandbox { get; set; }
    public bool UseSandbox_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.SkipCash.Fields.AdditionalFee")]
    public decimal AdditionalFee { get; set; }
    public bool AdditionalFee_OverrideForStore { get; set; }

    [NopResourceDisplayName("Plugins.Payment.SkipCash.Fields.AdditionalFeePercentage")]
    public bool AdditionalFeePercentage { get; set; }
    public bool AdditionalFeePercentage_OverrideForStore { get; set; }
}
