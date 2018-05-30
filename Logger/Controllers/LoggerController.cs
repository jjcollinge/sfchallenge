using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Mvc;

namespace Logger.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class LoggerController : Controller
    {
        private Logger logger;

        public LoggerController(Logger exporter)
        {
            this.logger = exporter;
        }

        // POST api/logger
        [HttpPost]
        public async Task<IActionResult> PostAsync([FromBody]Trade trade)
        {
            try
            {
                await this.logger.LogAsync(trade, CancellationToken.None);
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

        // GET api/logger/active
        [Route("active")]
        public async Task<IActionResult> GetAsync()
        {
            try
            {
                var count = await this.logger.ActiveTradeCountAsync();
                return this.Ok(count);
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

        // GET api/logger/done
        [Route("done")]
        [HttpGet]
        public async Task<IActionResult> GetTradesAsync()
        {
            var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
            var count = await this.logger.LoggedTradeCountAsync(ct);
            return this.Ok(count);
        }
    }
}
