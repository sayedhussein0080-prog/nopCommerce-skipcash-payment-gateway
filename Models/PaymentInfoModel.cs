using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.SkipCash.Models;

public record PaymentInfoModel : BaseNopModel
{
    public string DescriptionText { get; set; }
}
