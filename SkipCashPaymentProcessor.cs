using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.SkipCash.Components;
using Nop.Plugin.Payments.SkipCash.Services;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;

namespace Nop.Plugin.Payments.SkipCash;

/// <summary>
/// SkipCash payment processor
/// </summary>
public class SkipCashPaymentProcessor : BasePlugin, IPaymentMethod
{
    #region Fields

    private readonly SkipCashPaymentSettings _skipCashPaymentSettings;
    private readonly SkipCashHttpClient _skipCashHttpClient;
    private readonly ILocalizationService _localizationService;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly ISettingService _settingService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IWebHelper _webHelper;
    private readonly IOrderService _orderService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly ICustomerService _customerService;
    private readonly IAddressService _addressService;
    private readonly ICountryService _countryService;
    private readonly IStateProvinceService _stateProvinceService;
    private readonly ILogger _logger;

    #endregion

    #region Ctor

    public SkipCashPaymentProcessor(
        SkipCashPaymentSettings skipCashPaymentSettings,
        SkipCashHttpClient skipCashHttpClient,
        ILocalizationService localizationService,
        IOrderTotalCalculationService orderTotalCalculationService,
        ISettingService settingService,
        IShoppingCartService shoppingCartService,
        IWebHelper webHelper,
        IOrderService orderService,
        IOrderProcessingService orderProcessingService,
        ICustomerService customerService,
        IAddressService addressService,
        ICountryService countryService,
        IStateProvinceService stateProvinceService,
        ILogger logger)
    {
        _skipCashPaymentSettings = skipCashPaymentSettings;
        _skipCashHttpClient = skipCashHttpClient;
        _localizationService = localizationService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _settingService = settingService;
        _shoppingCartService = shoppingCartService;
        _webHelper = webHelper;
        _orderService = orderService;
        _orderProcessingService = orderProcessingService;
        _customerService = customerService;
        _addressService = addressService;
        _countryService = countryService;
        _stateProvinceService = stateProvinceService;
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Process a payment
    /// </summary>
    public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        // For redirection payment methods, we just mark the order as pending
        return Task.FromResult(new ProcessPaymentResult
        {
            NewPaymentStatus = PaymentStatus.Pending
        });
    }

    /// <summary>
    /// Post process payment - redirect to SkipCash payment page
    /// </summary>
    public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
    {
        var order = postProcessPaymentRequest.Order;
        var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

        // Build the webhook URL and return URL
        var storeUrl = _webHelper.GetStoreLocation();
        if (!storeUrl.EndsWith("/")) storeUrl += "/";
        var webhookUrl = $"{storeUrl}Plugins/PaymentSkipCash/Webhook";
        var returnUrl = $"{storeUrl}Plugins/PaymentSkipCash/PaymentReturn?orderId={order.Id}";

        // Get billing address
        var billingAddress = await _addressService.GetAddressByIdAsync(order.BillingAddressId);

        var rawPhone = billingAddress?.PhoneNumber ?? "";
        
        // Clean phone number for SkipCash (requires international format like +974... or +201...)
        var cleanPhone = new string(rawPhone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (!string.IsNullOrEmpty(cleanPhone))
        {
            if (cleanPhone.StartsWith("00"))
                cleanPhone = "+" + cleanPhone.Substring(2);
            else if (!cleanPhone.StartsWith("+"))
            {
                // Basic assumption: If it starts with 0 and is likely Egyptian (010, 011, 012, 015)
                if (cleanPhone.StartsWith("01") && cleanPhone.Length == 11)
                    cleanPhone = "+2" + cleanPhone;
                // Otherwise just prepend + (might not be accurate for all countries but satisfies regex)
                else if (cleanPhone.StartsWith("0"))
                    cleanPhone = "+" + cleanPhone.Substring(1); 
                else
                    cleanPhone = "+" + cleanPhone;
            }
        }
        
        if (string.IsNullOrEmpty(cleanPhone))
        {
            cleanPhone = "+97400000000"; // Fallback for empty phone because Phone is Mandatory
        }
        if (cleanPhone.Length > 15)
        {
            cleanPhone = cleanPhone.Substring(0, 15);
        }

        var firstName = (billingAddress?.FirstName ?? customer?.FirstName) ?? "Customer";
        var lastName = (billingAddress?.LastName ?? customer?.LastName) ?? "Name";

        // Remove special characters from names
        firstName = new string(firstName.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray()).Trim();
        lastName = new string(lastName.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray()).Trim();
        
        if (string.IsNullOrEmpty(firstName)) firstName = "Customer";
        if (string.IsNullOrEmpty(lastName)) lastName = "Name";

        if (firstName.Length > 60) firstName = firstName.Substring(0, 60);
        if (lastName.Length > 60) lastName = lastName.Substring(0, 60);

        var email = (billingAddress?.Email ?? customer?.Email) ?? "customer@example.com";
        if (email.Length > 255) email = email.Substring(0, 255);

        var transId = order.CustomOrderNumber ?? order.Id.ToString();
        transId = new string(transId.Where(c => char.IsLetterOrDigit(c)).ToArray());
        if (transId.Length > 40) transId = transId.Substring(0, 40);

        var custom1 = order.Id.ToString();
        if (custom1.Length > 300) custom1 = custom1.Substring(0, 300);

        var request = new SkipCashCreatePaymentRequest
        {
            Amount = order.OrderTotal.ToString("0.00"),
            FirstName = firstName,
            LastName = lastName,
            Phone = cleanPhone,
            Email = email,
            TransactionId = transId,
            Custom1 = custom1,
            ReturnUrl = returnUrl,
            WebHookUrl = webhookUrl
        };

        await _logger.InformationAsync($"SkipCash: Creating payment for order #{order.Id}, Amount={request.Amount}, TransactionId={request.TransactionId}");

        var response = await _skipCashHttpClient.CreatePaymentAsync(request);

        if (response.IsSuccess && !string.IsNullOrEmpty(response.PaymentUrl))
        {
            // Store the SkipCash payment ID in the order
            order.AuthorizationTransactionId = response.PaymentId;
            await _orderService.UpdateOrderAsync(order);

            await _logger.InformationAsync($"SkipCash: Redirecting order #{order.Id} to payment URL: {response.PaymentUrl}");

            // Redirect to SkipCash payment page
            var httpContext = Nop.Core.Infrastructure.EngineContext.Current.Resolve<IHttpContextAccessor>();
            httpContext.HttpContext?.Response.Redirect(response.PaymentUrl);
        }
        else
        {
            var errorMsg = $"SkipCash payment creation failed for order #{order.Id}: {response.ErrorMessage}";
            await _logger.ErrorAsync(errorMsg);
            

            // DEBUG: Log the request details so admin can see what was sent
            await _logger.ErrorAsync($"SkipCash DEBUG - KeyId={_skipCashPaymentSettings.KeyId}, " +
                $"Amount={request.Amount}, FirstName={request.FirstName}, LastName={request.LastName}, " +
                $"Phone={request.Phone}, Email={request.Email}, TransactionId={request.TransactionId}, " +
                $"WebHookUrl={request.WebHookUrl}, ReturnUrl={request.ReturnUrl}, " +
                $"ApiBaseUrl={_skipCashPaymentSettings.ApiBaseUrl}");

            // Add order note so admin can see the failure reason
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                Note = $"SkipCash payment creation failed: {response.ErrorMessage}",
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            // Redirect back to order details with error
            var httpContext = Nop.Core.Infrastructure.EngineContext.Current.Resolve<IHttpContextAccessor>();
            httpContext.HttpContext?.Response.Redirect(
                $"{storeUrl}orderdetails/{order.Id}");
        }
    }

    /// <summary>
    /// Returns a value indicating whether payment method should be hidden during checkout
    /// </summary>
    public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
    {
        // Always show SkipCash payment method
        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets additional handling fee
    /// </summary>
    public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
    {
        return await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
            _skipCashPaymentSettings.AdditionalFee, _skipCashPaymentSettings.AdditionalFeePercentage);
    }

    /// <summary>
    /// Captures payment
    /// </summary>
    public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
    {
        return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
    }

    /// <summary>
    /// Refunds a payment
    /// </summary>
    public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
    {
        var order = refundPaymentRequest.Order;
        var paymentId = order.AuthorizationTransactionId;

        if (string.IsNullOrEmpty(paymentId))
        {
            return new RefundPaymentResult { Errors = new[] { "SkipCash payment ID not found for this order" } };
        }

        var success = await _skipCashHttpClient.RefundPaymentAsync(
            paymentId,
            refundPaymentRequest.IsPartialRefund ? refundPaymentRequest.AmountToRefund : null);

        if (success)
        {
            return new RefundPaymentResult
            {
                NewPaymentStatus = refundPaymentRequest.IsPartialRefund
                    ? PaymentStatus.PartiallyRefunded
                    : PaymentStatus.Refunded
            };
        }

        return new RefundPaymentResult { Errors = new[] { "SkipCash refund request failed" } };
    }

    /// <summary>
    /// Voids a payment
    /// </summary>
    public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
    {
        return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });
    }

    /// <summary>
    /// Process recurring payment
    /// </summary>
    public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
    }

    /// <summary>
    /// Cancels a recurring payment
    /// </summary>
    public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
    {
        return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
    }

    /// <summary>
    /// Gets a value indicating whether customers can complete a payment after order is placed but not completed
    /// </summary>
    public Task<bool> CanRePostProcessPaymentAsync(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Allow re-processing if the order is still pending
        if (order.PaymentStatus != PaymentStatus.Pending)
            return Task.FromResult(false);

        // Allow within 1 hour
        return Task.FromResult((DateTime.UtcNow - order.CreatedOnUtc).TotalHours < 1);
    }

    /// <summary>
    /// Validate payment form
    /// </summary>
    public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
    {
        return Task.FromResult<IList<string>>(new List<string>());
    }

    /// <summary>
    /// Get payment information
    /// </summary>
    public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
    {
        return Task.FromResult(new ProcessPaymentRequest());
    }

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/PaymentSkipCash/Configure";
    }

    /// <summary>
    /// Gets a type of a view component for displaying plugin in public store
    /// </summary>
    public Type GetPublicViewComponent()
    {
        return typeof(SkipCashPaymentInfoViewComponent);
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    public override async Task InstallAsync()
    {
        // Default settings
        var settings = new SkipCashPaymentSettings
        {
            ApiBaseUrl = "https://skipcashtest.azurewebsites.net",
            UseSandbox = true,
            AdditionalFee = 0,
            AdditionalFeePercentage = false
        };
        await _settingService.SaveSettingAsync(settings);

        // Locales
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Payment.SkipCash.Fields.ApiBaseUrl"] = "API Base URL",
            ["Plugins.Payment.SkipCash.Fields.ApiBaseUrl.Hint"] = "The SkipCash API base URL. Use https://skipcashtest.azurewebsites.net for testing.",
            ["Plugins.Payment.SkipCash.Fields.KeyId"] = "Key ID",
            ["Plugins.Payment.SkipCash.Fields.KeyId.Hint"] = "Your SkipCash Key ID from the merchant portal.",
            ["Plugins.Payment.SkipCash.Fields.KeySecret"] = "Key Secret",
            ["Plugins.Payment.SkipCash.Fields.KeySecret.Hint"] = "Your SkipCash Key Secret from the merchant portal.",
            ["Plugins.Payment.SkipCash.Fields.MerchantUid"] = "Merchant UID",
            ["Plugins.Payment.SkipCash.Fields.MerchantUid.Hint"] = "Your SkipCash Merchant UID.",
            ["Plugins.Payment.SkipCash.Fields.WebhookKey"] = "Webhook Key",
            ["Plugins.Payment.SkipCash.Fields.WebhookKey.Hint"] = "Your SkipCash Webhook Key for verifying webhook signatures.",
            ["Plugins.Payment.SkipCash.Fields.UseSandbox"] = "Use Sandbox",
            ["Plugins.Payment.SkipCash.Fields.UseSandbox.Hint"] = "Check to use SkipCash sandbox/test environment.",
            ["Plugins.Payment.SkipCash.Fields.AdditionalFee"] = "Additional fee",
            ["Plugins.Payment.SkipCash.Fields.AdditionalFee.Hint"] = "Enter additional fee to charge your customers.",
            ["Plugins.Payment.SkipCash.Fields.AdditionalFeePercentage"] = "Additional fee. Use percentage",
            ["Plugins.Payment.SkipCash.Fields.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total.",
            ["Plugins.Payment.SkipCash.PaymentMethodDescription"] = "You will be redirected to SkipCash to complete the payment",
            ["Plugins.Payment.SkipCash.PaymentInfo"] = "You will be redirected to SkipCash secure payment page after placing your order."
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    public override async Task UninstallAsync()
    {
        // Settings
        await _settingService.DeleteSettingAsync<SkipCashPaymentSettings>();

        // Locales
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payment.SkipCash");

        await base.UninstallAsync();
    }

    /// <summary>
    /// Gets a payment method description
    /// </summary>
    public async Task<string> GetPaymentMethodDescriptionAsync()
    {
        return await _localizationService.GetResourceAsync("Plugins.Payment.SkipCash.PaymentMethodDescription");
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether capture is supported
    /// </summary>
    public bool SupportCapture => false;

    /// <summary>
    /// Gets a value indicating whether partial refund is supported
    /// </summary>
    public bool SupportPartiallyRefund => true;

    /// <summary>
    /// Gets a value indicating whether refund is supported
    /// </summary>
    public bool SupportRefund => true;

    /// <summary>
    /// Gets a value indicating whether void is supported
    /// </summary>
    public bool SupportVoid => false;

    /// <summary>
    /// Gets a recurring payment type of payment method
    /// </summary>
    public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

    /// <summary>
    /// Gets a payment method type (redirection to SkipCash)
    /// </summary>
    public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

    /// <summary>
    /// Gets a value indicating whether we should display a payment information page for this plugin
    /// </summary>
    public bool SkipPaymentInfo => false;

    #endregion
}
