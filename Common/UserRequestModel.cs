using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Common
{
    public class UserRequestModel
    {
        public string Username { get; set; }
        public UInt32 Quantity { get; set; }

        public UInt32 Balance { get; set; }

        public static implicit operator User(UserRequestModel request)
        {
            var id = Guid.NewGuid().ToString();
            return new User(id, request.Username, request.Quantity, request.Balance, null);
        }
    }
}
