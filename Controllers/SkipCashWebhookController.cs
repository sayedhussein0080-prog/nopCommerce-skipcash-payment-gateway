using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.SkipCash.Services;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Payments.SkipCash.Controllers;

/// <summary>
/// Controller to handle SkipCash webhook notifications
/// </summary>
public class SkipCashWebhookController : Controller
{
    #region Fields

    private readonly IOrderService _orderService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly SkipCashPaymentSettings _settings;
    private readonly SkipCashHttpClient _skipCashHttpClient;
    private readonly ILogger _logger;

    #endregion

    #region Ctor

    public SkipCashWebhookController(
        IOrderService orderService,
        IOrderProcessingService orderProcessingService,
        SkipCashPaymentSettings settings,
        SkipCashHttpClient skipCashHttpClient,
        ILogger logger)
    {
        _orderService = orderService;
        _orderProcessingService = orderProcessingService;
        _settings = settings;
        _skipCashHttpClient = skipCashHttpClient;
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Handles SkipCash webhook POST notifications for payment status updates
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    [Route("Plugins/PaymentSkipCash/Webhook")]
    public async Task<IActionResult> Webhook()
    {
        try
        {
            // Read the request body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            await _logger.InformationAsync($"SkipCash Webhook received: {body}");

            var payload = System.Text.Json.JsonSerializer.Deserialize<SkipCashWebhookPayload>(body,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (payload == null)
            {
                await _logger.WarningAsync("SkipCash Webhook: payload is null");
                return BadRequest("Invalid payload");
            }

            // Verify webhook signature if webhook key is configured
            //if (!string.IsNullOrEmpty(_settings.WebhookKey))
            //{
            //    if (!VerifyWebhookSignature(payload, body))
            //    {
            //        await _logger.WarningAsync("SkipCash Webhook: signature verification failed");
            //        return Unauthorized("Invalid signature");
            //    }
            //}

            // Find the order by Custom1 (order ID) or TransactionId
            Order order = null;

            if (!string.IsNullOrEmpty(payload.Custom1) && int.TryParse(payload.Custom1, out var orderId))
            {
                order = await _orderService.GetOrderByIdAsync(orderId);
            }

            if (order == null && !string.IsNullOrEmpty(payload.TransactionId))
            {
                order = await _orderService.GetOrderByCustomOrderNumberAsync(payload.TransactionId);
            }

            if (order == null)
            {
                await _logger.WarningAsync($"SkipCash Webhook: order not found for Custom1={payload.Custom1}, TransactionId={payload.TransactionId}");
                return NotFound("Order not found");
            }

            // Update the payment transaction ID
            var skipCashPaymentId = payload.Id?.ToString() ?? payload.PaymentId;
            if (!string.IsNullOrEmpty(skipCashPaymentId))
            {
                order.AuthorizationTransactionId = skipCashPaymentId;
            }

            // Process based on SkipCash status
            // SkipCash StatusId values:
            // 0 = New, 1 = Pending, 2 = Paid, 3 = Canceled, 4 = Failed, 5 = Rejected, 6 = Refunded, 7 = Pending Refund, 8 = Refund Failed
            switch (payload.StatusId)
            {
                case 2: // Paid
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        order.CaptureTransactionId = skipCashPaymentId;
                        await _orderService.UpdateOrderAsync(order);
                        await _orderProcessingService.MarkOrderAsPaidAsync(order);
                        await _logger.InformationAsync($"SkipCash Webhook: Order #{order.Id} marked as paid");
                    }
                    break;

                case 3: // Canceled
                case 4: // Failed
                case 5: // Rejected
                    if (order.PaymentStatus == PaymentStatus.Pending)
                    {
                        // Add order note about the failure
                        await _orderService.InsertOrderNoteAsync(new OrderNote
                        {
                            OrderId = order.Id,
                            Note = $"SkipCash payment {payload.Status ?? "canceled/failed/rejected"}. Payment ID: {skipCashPaymentId}",
                            DisplayToCustomer = false,
                            CreatedOnUtc = DateTime.UtcNow
                        });

                        // Cancel the order if it's still pending
                        if (_orderProcessingService.CanCancelOrder(order))
                        {
                            await _orderProcessingService.CancelOrderAsync(order, true);
                            await _logger.InformationAsync($"SkipCash Webhook: Order #{order.Id} canceled due to payment status: {payload.Status}");
                        }
                    }
                    break;

                case 6: // Refunded
                    if (_orderProcessingService.CanRefundOffline(order))
                    {
                        await _orderProcessingService.RefundOfflineAsync(order);
                        await _logger.InformationAsync($"SkipCash Webhook: Order #{order.Id} refunded");
                    }
                    break;

                default:
                    await _logger.InformationAsync($"SkipCash Webhook: Unknown status {payload.StatusId} for order #{order.Id}");
                    break;
            }

            return Ok("Webhook processed");
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync("SkipCash Webhook error", ex);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Handles the return URL after payment on SkipCash
    /// </summary>
    [HttpGet]
    [IgnoreAntiforgeryToken]
    [Route("Plugins/PaymentSkipCash/PaymentReturn")]
    public async Task<IActionResult> PaymentReturn(int orderId)
    {
        // Redirect to order details page
        // The actual payment status update is handled by the webhook asynchronously
        return RedirectToRoute("OrderDetails", new { orderId });
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Verifies the webhook signature
    /// </summary>
    private bool VerifyWebhookSignature(SkipCashWebhookPayload payload, string rawBody)
    {
        try
        {
            if (string.IsNullOrEmpty(payload.Signature))
                return false;

            var keyBytes = Convert.FromBase64String(_settings.WebhookKey);
            using var hmac = new HMACSHA256(keyBytes);
            var dataBytes = Encoding.UTF8.GetBytes(rawBody);
            var hashBytes = hmac.ComputeHash(dataBytes);
            var computedSignature = Convert.ToBase64String(hashBytes);

            return string.Equals(computedSignature, payload.Signature, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
