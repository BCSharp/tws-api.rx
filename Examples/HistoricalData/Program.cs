using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Disposables;

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
            using (var subscriptions = new CompositeDisposable())
            {
                // The errors observable is always available
                // Here we just print them all to the console
                subscriptions.Add(client.Errors.Subscribe(
                    et => Console.WriteLine("TWS message #{0}: {1}", et.Item2, et.Item3),
                    ex => Console.WriteLine("TWS Exception: {0}", ex.Message)
                ));

                Console.WriteLine("Connecting to TWS...");
                try
                {
                    client.Connect();  // disposing will disconnect
                }
                catch 
                {
                    Console.WriteLine("Connection failed, exiting...");
                    return;
                } 
                Console.WriteLine("Connected.");

                Console.WriteLine("Press ENTER to disconnect.");
                Console.ReadLine();
            }
            Console.WriteLine("Finished.");
        }
    }
}
