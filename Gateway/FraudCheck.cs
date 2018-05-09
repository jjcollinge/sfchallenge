using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var token = Guid.NewGuid().ToString();
            string hashString = RunHash(token);
            //Generate CPU Load but don't slow down the requests... 
            Task.Run(() =>
            {
                for (int i = 0; i < 500; i++)
                {
                    var hash = RunHash(token);
                    Console.WriteLine(hash);
                }
            });
            

            return hashString;
        }

        private static string RunHash(string hashString)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(Guid.NewGuid().ToString());
            SHA256Managed hashstring = new SHA256Managed();
            byte[] hash = hashstring.ComputeHash(bytes);
            foreach (byte x in hash)
            {
                hashString += String.Format("{0:x2}", x);
            }

            return hashString;
        }
    }
}
