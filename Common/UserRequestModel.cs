using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Common
{
    public class UserRequestModel
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public UInt32 Quantity { get; set; }

        public UInt32 Balance { get; set; }

        public static implicit operator User(UserRequestModel request)
        {
            if (request.Id == null)
            {
                request.Id = Guid.NewGuid().ToString();
            }
            return new User(request.Id, request.Username, request.Quantity, request.Balance, null);
        }
    }
}
