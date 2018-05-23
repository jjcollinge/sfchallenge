using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Common
{
    public class UserRequestModel
    {
        public UserRequestModel()
        {
            CurrencyAmounts = new Dictionary<string, double>();
        }

        public string Id { get; set; }
        public string Username { get; set; }
        public IEnumerable<KeyValuePair<string, double>> CurrencyAmounts { get; set; }

        public static implicit operator User(UserRequestModel request)
        {
            if (request.Id == null)
            {
                request.Id = Guid.NewGuid().ToString();
            }
            return new User(request.Id, request.Username, request.CurrencyAmounts, null);
        }
    }
}
