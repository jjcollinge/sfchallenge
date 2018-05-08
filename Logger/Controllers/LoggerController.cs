using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
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
        public async Task<IActionResult> Post([FromBody]Trade trade)
        {
            try
            {
                await this.logger.LogAsync(trade);
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
    }
}
