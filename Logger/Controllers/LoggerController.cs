using System;
using System.Collections.Generic;
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
        public async Task<IActionResult> Post([FromBody]Transfer transfer)
        {
            try
            {
                await this.logger.LogAsync(transfer);
                return this.Ok();
            }
            catch (Exception)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }
    }
}
