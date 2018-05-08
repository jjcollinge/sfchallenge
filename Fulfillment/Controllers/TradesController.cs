using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class TradesController : Controller
    {
        private Fulfillment fulfillment;
        // Not thread safe as worst case is cooldown lasting slightly longer than required
        private static bool IsCoolingDown = false;

        public TradesController(Fulfillment fulfillment)
        {
            this.fulfillment = fulfillment;
        }

        // POST api/trades
        [HttpPost]
        public async Task<IActionResult> PostAsync([FromBody] TradeRequestModel tradeRequest)
        {
            if (IsCoolingDown)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                return new StatusCodeResult(429);
            }
            try
            {
                var tradeId = await this.fulfillment.AddTradeAsync(tradeRequest);
                return this.Ok(tradeId);
            }
            catch (InvalidTradeRequestException ex)
            {
                return new ContentResult { StatusCode = 400, Content = ex.Message };
            }
            catch (FabricNotPrimaryException)
            {
                return new ContentResult { StatusCode = 410, Content = "The primary replica has moved. Please re-resolve the service." };
            }
            catch (MaxPendingTradesExceededException)
            {
                if (!IsCoolingDown)
                {
                    IsCoolingDown = true;
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    IsCoolingDown = false;
                }
                return new StatusCodeResult(429);
            }
            catch (FabricException)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }


        [HttpGet]
        public async Task<IActionResult> GetAsync()
        {
            try
            {
                var count = await this.fulfillment.GetTradesCountAsync();
                return this.Ok(count);
            }
            catch (FabricException)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }
    }
}
