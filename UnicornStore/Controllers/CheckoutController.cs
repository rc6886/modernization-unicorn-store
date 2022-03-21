using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UnicornStore.Components;
using UnicornStore.Models;

namespace UnicornStore.Controllers
{
    [Authorize]
    public class CheckoutController : Controller
    {
        private const string PromoCode = "FREE";

        private readonly ILogger<CheckoutController> _logger;

        public CheckoutController(ILogger<CheckoutController> logger)
        {
            _logger = logger;
        }

        //
        // GET: /Checkout/
        public IActionResult AddressAndPayment()
        {
            return View();
        }

        //
        // POST: /Checkout/AddressAndPayment

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddressAndPayment(
            [FromServices] UnicornStoreContext dbContext,
            [FromForm] Order order,
            CancellationToken requestAborted)
        {
            if (!ModelState.IsValid)
            {
                return View(order);
            }

            var formCollection = await HttpContext.Request.ReadFormAsync();

            try
            {
                if (string.Equals(formCollection["PromoCode"].FirstOrDefault(), PromoCode,
                    StringComparison.OrdinalIgnoreCase) == false)
                {
                    return View(order);
                }
                else
                {
                    order.Username = HttpContext.User.Identity.Name;
                    order.OrderDate = DateTime.Now;

                    //Add the Order
                    // TODO: investigate why intermediary SaveChangesAsync() is necessary.
                    await dbContext.Orders.AddAsync(order);
                    await dbContext.SaveChangesAsync();

                    //Process the order
                    var cart = ShoppingCart.GetCart(dbContext, HttpContext);
                    await cart.CreateOrder(order);

                    // Save all changes
                    await dbContext.SaveChangesAsync(requestAborted);

                    var awsEventBridgeClient = new AmazonEventBridgeClient();

                    await awsEventBridgeClient.PutEventsAsync(new PutEventsRequest
                    {
                        Entries = new List<PutEventsRequestEntry>
                        {
                            new PutEventsRequestEntry
                            {
                                DetailType = "OrderCreated",
                                EventBusName = "unicorn-store",
                                Source = "unicorn-store/storefront",
                                Detail = JsonSerializer.Serialize(new OrderCreated
                                {
                                    OrderId = order.OrderId,
                                    OrderDate = order.OrderDate,
                                    Total = order.Total,
                                }),
                            },
                        },
                    });

                    _logger.LogInformation("User {userName} started checkout of {orderId}.", order.Username, order.OrderId);

                    return RedirectToAction("Complete", new { id = order.OrderId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Checkout failed");
                //Invalid - redisplay with errors
                return View(order);
            }
        }

        //
        // GET: /Checkout/Complete

        public async Task<IActionResult> Complete(
            [FromServices] UnicornStoreContext dbContext,
            int id)
        {
            var userName = HttpContext.User.Identity.Name;

            // Validate customer owns this order
            bool isValid = await dbContext.Orders.AnyAsync(
                o => o.OrderId == id &&
                o.Username == userName);

            if (isValid)
            {
                _logger.LogInformation("User {userName} completed checkout on order {orderId}.", userName, id);
                return View(id);
            }
            else
            {
                _logger.LogError(
                    "User {userName} tried to checkout with an order ({orderId}) that doesn't belong to them.",
                    userName,
                    id);
                return View("Error");
            }
        }
    }
}
