using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BlockNotify
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Count() < 1)
            {
                Console.WriteLine("usage: blocknotify url/blocknotify/coinid/blockhash\n");
                return;
            }
            try
            {
                using (var httpClient = new HttpClient())
                {
                  var resp=  httpClient.GetAsync(args[0]);
                    resp.Wait();
                    var contentResult = resp.Result.Content.ReadAsStringAsync();
                    Console.WriteLine(contentResult.Result);

                }
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }


        }
    }
}
