using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Controllers
{
    [Produces("application/json")]
    [Route("api/Users")]
    public class UsersController : Controller
    {
        private readonly Fulfillment fulfillment;

        public UsersController(Fulfillment fulfillment)
        {
            this.fulfillment = fulfillment;
        }

        [HttpGet]
        public async Task<IActionResult> GetAsync()
        {
            try
            {
                var users = await this.fulfillment.GetUsersAsync();
                return this.Json(users);
            }
            catch (Exception)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAsync(string id)
        {
            try
            {
                var user = await this.fulfillment.GetUserAsync(id);
                if (user == null)
                {
                    return this.NotFound();
                }
                return this.Json(user);
            }
            catch (Exception)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }

        [HttpPost]
        public async Task<IActionResult> PostAsync([FromBody] UserRequestModel userRequest)
        {
            try
            {
                var id = await this.fulfillment.AddUserAsync(userRequest);
                return this.Ok(id);
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

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsync(string id)
        {
            try
            {
                var removed = await this.fulfillment.DeleteUserAsync(id);
                if (!removed)
                {
                    return this.NotFound();
                }
                return this.Ok();
            }
            catch (Exception)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }
    }
}