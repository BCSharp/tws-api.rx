/* Copyright © 2014 Paweł A. Konieczny
 * See "LICENCE.txt" for details.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.PlatformServices;
using System.Reactive.Joins;
using System.Reactive.Threading;
using System.Reactive.Threading.Tasks;
using System.Reactive.Disposables;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace IBApi.Reactive
{
    /// <summary>
    ///     Description of open position in one security.
    /// </summary>
    public class PositionLine
    {
        // TODO: replace by primary constructor in C# 6.0
        public PositionLine(string accountName, string secType, string symbol, string series, decimal position, 
                            decimal price, decimal value, decimal averageCost, decimal unrealizedProfit, decimal realizedProfit)
        {
            AccountName = accountName;
            SecType = secType;
            Symbol = symbol;
            Series = series;
            Position = position;
            Price = price;
            Value = value;
            AverageCost = averageCost;
            UnrealizedProfit = unrealizedProfit;
            RealizedProfit = realizedProfit;
        }

        public string AccountName { get; private set; }
        public string SecType { get; private set; }
        public string Symbol { get; private set; }
        public string Series { get; private set; }
        public decimal Position { get; private set; }
        public decimal Price { get; private set; }
        public decimal Value { get; private set; }
        public decimal AverageCost { get; private set; }
        public decimal UnrealizedProfit { get; private set; }
        public decimal RealizedProfit { get; private set; }
    }

    public class AccountData
    {
        public AccountData(string accountName, string key, string value, string currency)
        {
            AccountName = accountName;
            Key = key;
            Value = value;
            Currency = currency;
        }

        public AccountData(PositionLine positionLine)
        {
            PositionLine = positionLine;
            AccountName = positionLine.AccountName;
            Key = positionLine.Symbol;
            Value = positionLine.Position.ToString();
            Currency = positionLine.SecType;
        }

        public string AccountName { get; private set; }
        public string Key { get; private set; }
        public string Value { get; private set; }
        public string Currency { get; private set; }

        public PositionLine PositionLine { get; private set; }
    }
}
