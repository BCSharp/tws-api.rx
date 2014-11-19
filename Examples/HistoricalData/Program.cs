using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IBApi;
using IBApi.Reactive;

namespace HistoricalData
{
    class Program
    {
        static void Main(string[] args)
        {
            // host and port can be specified in the constructor or Connect()
            using (var client = new TwsClient(clientId: 1, host: "127.0.0.1", port: 7496))
            {
                Console.WriteLine("Connecting to TWS...");
                client.Connect();  // disposing will disconnect
                Console.WriteLine("Connected.");
            }
            Console.WriteLine("Finished.");
        }
    }
}
