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

namespace PortfolioData
{
    static class Program
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
                    et => Console.WriteLine("TWS {2} #{0}: {1}", et.Code, et.Message, et.IsError()? "error" : "message"),
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

                Console.WriteLine("Requesting protfolio snapshot...");
                var snapshot = client.RequestPortfolioSnapshot();
                subscriptions.Add(snapshot.SubscribeToConsole());

                var live = client.RequestPortfolioData();
                subscriptions.Add(live.SubscribeToConsole());

                snapshot.Wait(); // let the snapshot be fully printed on console before going further
                Console.WriteLine("Press ENTER to start live updates, \nENTER again to request stapshot update in the middle of live updates, \nthen ENTER to stop live updates.");
                Console.ReadLine();
                var live_connect = live.Connect();
                Console.ReadLine();
                subscriptions.Add(client.RequestPortfolioSnapshot().SubscribeToConsole());
                Console.ReadLine();
                live_connect.Dispose();

                Console.WriteLine("Press ENTER to disconnect.");
                Console.ReadLine();
            }
            Console.WriteLine("Finished.");
        }

        static IDisposable SubscribeToConsole(this IObservable<AccountData> ostm)
        {
            return ostm
                    .Where(acc =>             // This is many lines of data, filter out some for brevity
                                acc.PositionLine != null
                               || acc.Key.StartsWith("AccountTime")
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
                    );
        }
    }
}
