using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Payments.SkipCash.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.SkipCash.Components;

public class SkipCashPaymentInfoViewComponent : NopViewComponent
{
    private readonly ILocalizationService _localizationService;

    public SkipCashPaymentInfoViewComponent(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var model = new PaymentInfoModel
        {
            DescriptionText = await _localizationService.GetResourceAsync("Plugins.Payment.SkipCash.PaymentInfo")
        };

        return View("~/Plugins/Payments.SkipCash/Views/PaymentInfo.cshtml", model);
    }
}
