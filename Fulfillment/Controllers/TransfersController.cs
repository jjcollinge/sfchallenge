using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Controllers
{
    [Produces("application/json")]
    [Route("api/[controller]")]
    public class TransfersController : Controller
    {
        private Fulfillment fulfillment;

        public TransfersController(Fulfillment fulfillment)
        {
            this.fulfillment = fulfillment;
        }

        // POST api/transfers
        [HttpPost]
        public async Task<IActionResult> PostAsync([FromBody]TransferRequestModel transferRequest)
        {
            try
            {
                var transferId = await this.fulfillment.AddTransferAsync(transferRequest);
                return this.Ok(transferId);
            }
            catch (InvalidUserRequestException ex)
            {
                return new ContentResult { StatusCode = 400, Content = ex.Message };
            }
            catch (Exception)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }
    }
}
