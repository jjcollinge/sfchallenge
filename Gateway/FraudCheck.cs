using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Gateway
{
    public class FraudCheck
    {
        public static string Check()
        {
            string hashString = string.Empty;
            for (int i = 0; i < 15; i++)
            {
                byte[] bytes = Encoding.Unicode.GetBytes(Guid.NewGuid().ToString());
                SHA256Managed hashstring = new SHA256Managed();
                byte[] hash = hashstring.ComputeHash(bytes);
                foreach (byte x in hash)
                {
                    hashString += String.Format("{0:x2}", x);
                }
            }

            return hashString;
        }
    }
}
