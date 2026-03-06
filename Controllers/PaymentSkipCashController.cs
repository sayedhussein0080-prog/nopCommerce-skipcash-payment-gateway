using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.SkipCash.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.SkipCash.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class PaymentSkipCashController : BasePaymentController
{
    #region Fields

    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IPermissionService _permissionService;
    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;

    #endregion

    #region Ctor

    public PaymentSkipCashController(
        ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService,
        IStoreContext storeContext)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
        _storeContext = storeContext;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure()
    {
        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<SkipCashPaymentSettings>(storeScope);

        var model = new ConfigurationModel
        {
            ApiBaseUrl = settings.ApiBaseUrl,
            KeyId = settings.KeyId,
            KeySecret = settings.KeySecret,
            MerchantUid = settings.MerchantUid,
            WebhookKey = settings.WebhookKey,
            UseSandbox = settings.UseSandbox,
            AdditionalFee = settings.AdditionalFee,
            AdditionalFeePercentage = settings.AdditionalFeePercentage,
            ActiveStoreScopeConfiguration = storeScope
        };

        if (storeScope > 0)
        {
            model.ApiBaseUrl_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.ApiBaseUrl, storeScope);
            model.KeyId_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.KeyId, storeScope);
            model.KeySecret_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.KeySecret, storeScope);
            model.MerchantUid_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.MerchantUid, storeScope);
            model.WebhookKey_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.WebhookKey, storeScope);
            model.UseSandbox_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.UseSandbox, storeScope);
            model.AdditionalFee_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AdditionalFee, storeScope);
            model.AdditionalFeePercentage_OverrideForStore = await _settingService.SettingExistsAsync(settings, x => x.AdditionalFeePercentage, storeScope);
        }

        return View("~/Plugins/Payments.SkipCash/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return await Configure();

        var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
        var settings = await _settingService.LoadSettingAsync<SkipCashPaymentSettings>(storeScope);

        settings.ApiBaseUrl = model.ApiBaseUrl;
        settings.KeyId = model.KeyId;
        settings.KeySecret = model.KeySecret;
        settings.MerchantUid = model.MerchantUid;
        settings.WebhookKey = model.WebhookKey;
        settings.UseSandbox = model.UseSandbox;
        settings.AdditionalFee = model.AdditionalFee;
        settings.AdditionalFeePercentage = model.AdditionalFeePercentage;

        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.ApiBaseUrl, model.ApiBaseUrl_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.KeyId, model.KeyId_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.KeySecret, model.KeySecret_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.MerchantUid, model.MerchantUid_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.WebhookKey, model.WebhookKey_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
        await _settingService.SaveSettingOverridablePerStoreAsync(settings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

        await _settingService.ClearCacheAsync();

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }

    #endregion
}
