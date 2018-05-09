using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;

namespace OrderBook.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class OrdersController : Controller
    {
        private static bool IsCoolingDown = false;
        private readonly OrderBook orderBook;

        public OrdersController(OrderBook orderBook)
        {
            this.orderBook = orderBook;
        }

        [HttpGet]
        public async Task<IActionResult> GetAsync()
        {
            try
            {
                var asks = await this.orderBook.GetAsksAsync();
                var bids = await this.orderBook.GetBidsAsync();
                var asksCount = asks.Count;
                var bidsCount = bids.Count;
                var view = new OrderBookViewModel
                {
                    Asks = asks,
                    Bids = bids,
                    AsksCount = asksCount,
                    BidsCount = bidsCount,
                };
                return this.Json(view);
            }
            catch (FabricException)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAsync()
        {
            try
            {
                await this.orderBook.ClearAllOrders();
                return this.Ok();
            }
            catch (FabricNotPrimaryException)
            {
                return new ContentResult { StatusCode = 410, Content = "The primary replica has moved. Please re-resolve the service." };
            }
            catch (FabricException)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }

        [Route("bids")]
        [HttpGet]
        public async Task<IActionResult> Bids()
        {
            try
            {
                var bids = await this.orderBook.GetBidsAsync();
                return this.Json(bids);
            }
            catch (InvalidAskException ex)
            {
                return new ContentResult { StatusCode = 400, Content = ex.Message };
            }
            catch (FabricException)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }

        [Route("asks")]
        [HttpGet]
        public async Task<IActionResult> Asks()
        {
            try
            {
                var bids = await this.orderBook.GetAsksAsync();
                return this.Json(bids);
            }
            catch (FabricException)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }

        [Route("bid")]
        [HttpPost]
        public async Task<IActionResult> Bid([FromBody] OrderRequestModel order)
        {
            if (IsCoolingDown)
            {
                ServiceEventSource.Current.ServiceMaxPendingCooldown();
                await Task.Delay(1200);
                return new StatusCodeResult(429);
            }

            try
            {
                var orderId = await this.orderBook.AddBidAsync(order);
                return this.Ok(orderId);
            }
            catch (InvalidAskException ex)
            {
                ServiceEventSource.Current.ServiceException(orderBook.Context, "Invalid ask", ex);
                return new ContentResult { StatusCode = 400, Content = ex.Message };
            }
            catch (FabricNotPrimaryException ex)
            {
                ServiceEventSource.Current.ServiceException(orderBook.Context, "NotPrimary", ex);

                return new ContentResult { StatusCode = 410, Content = "The primary replica has moved. Please re-resolve the service." };
            }
            catch (FabricException ex)
            {
                ServiceEventSource.Current.ServiceException(orderBook.Context, "FabricException", ex);

                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
            catch (MaxOrdersExceededException)
            {
                if (!IsCoolingDown)
                {
                    try
                    {
                        IsCoolingDown = false;
                        await Task.Delay(TimeSpan.FromSeconds(3));
                    }
                    finally
                    {
                        IsCoolingDown = true;
                    }
                }
                return new StatusCodeResult(429);

            }
        }

        [Route("ask")]
        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] OrderRequestModel order)
        {
            if (IsCoolingDown)
            {
                ServiceEventSource.Current.ServiceMaxPendingCooldown();
                await Task.Delay(1200);
                return new StatusCodeResult(429);
            }

            try
            {
                var orderId = await this.orderBook.AddAskAsync(order);
                return this.Ok(orderId);
            }
            catch (InvalidAskException ex)
            {
                ServiceEventSource.Current.ServiceException(orderBook.Context, "Invalid ask", ex);
                return new ContentResult { StatusCode = 400, Content = ex.Message };
            }
            catch (FabricNotPrimaryException ex)
            {
                ServiceEventSource.Current.ServiceException(orderBook.Context, "NotPrimary", ex);

                return new ContentResult { StatusCode = 410, Content = "The primary replica has moved. Please re-resolve the service." };
            }
            catch (FabricException ex)
            {
                ServiceEventSource.Current.ServiceException(orderBook.Context, "FabricException", ex);

                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
            catch (MaxOrdersExceededException)
            {
                if (!IsCoolingDown)
                {
                    try
                    {
                        IsCoolingDown = false;
                        await Task.Delay(TimeSpan.FromSeconds(3));
                    }
                    finally
                    {
                        IsCoolingDown = true;
                    }
                }
                return new StatusCodeResult(429);
            }
        }
    }
}
