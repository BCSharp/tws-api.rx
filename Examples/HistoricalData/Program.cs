using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Joins;
using System.Reactive.Linq;
using System.Reactive.PlatformServices;
using System.Reactive.Subjects;
using System.Reactive.Threading;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading;
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
            using (var subscriptions = new CompositeDisposable())
            {
                // The errors observable is always available
                // Here we just print them all to the console
                subscriptions.Add(client.Errors.Subscribe(
                    et => Console.WriteLine("TWS message #{0}{2}: {1}", et.Item2, et.Item3, et.Item1 >= 0? String.Format(" (req: {0})", et.Item1): ""),
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


                var contract = new Contract()
                {
                    Symbol = "MSFT",
                    Exchange = "SMART",
                    SecType = "STK",
                    Currency = "USD",
                };

                Console.WriteLine("Intraday data:");
                IObservable<Bar> hdata = client.RequestHistoricalData(
                    contract,
                    DateTime.Now,
                    duration: "1 D", // 1 day window
                    barSizeSetting: "1 min",
                    whatToShow: "TRADES",
                    useRTH: true
                );
                // hdata is a cold observable; the request to TWS is sent on subscription
                // Since Main() is not async, we cannot use await here
                try
                {
                    hdata.Do(Console.WriteLine).Timeout(TimeSpan.FromSeconds(15)).Wait();
                }
                catch (TimeoutException ex)
                {
                    Console.WriteLine(ex.Message);
                    return;
                }

                // Example: this is what it would look like with await
                //await hdata.Do(Console.WriteLine);

                // It also can be done w/o using Do() but with more ceremony.
                // Since it is a cold observable, it has to be turned hot with Publish()
                //var pub = hdata.Publish();
                //var sub = pub.Subscribe(Console.WriteLine);
                //var con = pub.Connect();
                //await pub;
                //sub.Dispose();
                //con.Dispose();

                Console.WriteLine("Daily data:");
                IObservable<Bar> hdata2 = client.RequestHistoricalData(
                    contract,
                    DateTime.Now,
                    duration: "1 W", // 1 week window
                    barSizeSetting: "1 day",
                    whatToShow: "TRADES",
                    useRTH: true
                );
                hdata2.Do(bar => Console.WriteLine(bar.ToString(TimeZoneInfo.Utc))).Wait();

                Console.WriteLine("Press ENTER to disconnect.");
                Console.ReadLine();
            }
            Console.WriteLine("Finished.");
        }
    }
}
