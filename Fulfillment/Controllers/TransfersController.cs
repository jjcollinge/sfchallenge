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
    public class TransfersController : Controller
    {
        private Fulfillment fulfillment;

        public TransfersController(Fulfillment fulfillment)
        {
            this.fulfillment = fulfillment;
        }

        // POST api/transfers
        [HttpPost]
        public async Task<IActionResult> PostAsync([FromBody] TransferRequestModel transferRequest)
        {
            try
            {
                var transferId = await this.fulfillment.AddTransferAsync(transferRequest);
                return this.Ok(transferId);
            }
            catch (InvalidTransferRequestException ex)
            {
                return new ContentResult { StatusCode = 400, Content = ex.Message };
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

        [HttpGet]
        public async Task<IActionResult> GetAsync()
        {
            try
            {
                var count = await this.fulfillment.GetTransfersCountAsync();
                return this.Ok(count);
            }
            catch (FabricException)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }
    }
}
