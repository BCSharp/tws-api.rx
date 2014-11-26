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
                    et => Console.WriteLine("TWS message #{0}: {1}", et.Code, et.Message),
                    ex => Console.WriteLine("TWS Exception: {0}", ex.Message)
                ));

                Console.WriteLine("Connecting to TWS...");
                try
                {
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    client.Connect(ct: cts.Token);  // disposing will disconnect
                }
                catch 
                {
                    Console.WriteLine("Connection failed, exiting...");
                    return;
                } 
                Console.WriteLine("Connected.");


                subscriptions.Add(client.RequestPortfolioSnapshot()
                    .Where(acc =>             // This is a lot of data, filter out some
                                acc.PositionLine != null
                               || acc.Key.StartsWith("Available")
                               || acc.Key.StartsWith("Cash")
                               || acc.Key.StartsWith("Equity")
                               || acc.Key.StartsWith("Excess")
                               || acc.Key.StartsWith("NetLiquidation")
                               || acc.Key.StartsWith("Total")
                    )
                    .Subscribe(
                        acc =>
                        {
                            Console.WriteLine(@"Account ""{3}"": {0} = {1} {2}", acc.Key, acc.Value, acc.Currency, acc.AccountName);
                            if (acc.PositionLine != null)
                                Console.WriteLine(
                                    "Position|qty: {0}|cost: {1}|price: {2}|{3}:{4}{5}",
                                    acc.PositionLine.Position, 
                                    acc.PositionLine.AverageCost,
                                    acc.PositionLine.Price,
                                    acc.PositionLine.SecType,
                                    acc.PositionLine.Symbol,
                                    acc.PositionLine.Series
                                );
                        },
                        ex => Console.WriteLine("Exception: {0}", ex.Message),
                        () => Console.WriteLine("Portfolio snapshot completed.")
                    )
                );


                Console.WriteLine("Press ENTER to disconnect.");
                Console.ReadLine();
            }
            Console.WriteLine("Finished.");
        }
    }
}
