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

using IBApi;
using IBApi.Reactive;


namespace IBApi.Reactive
{
    /// <summary>
    ///     Bar represents a price/size bar for a given period consolidated from Ticks.
    ///     The Bar timestamp follows the begin-of-period logic.
    /// </summary>
    public class Bar
    {
        /// <summary>
        ///     Construct an empty Bar object with a given contract ID.
        ///     Quotes are set to neutral values.
        /// </summary>
        /// <param name="timestamp">
        ///     Elapsed 100ns units from 1/1/1 in UTC timezone till the beginning of bar period.
        /// </param>
        /// <param name="id">
        ///     Contract ID.
        /// </param>
        /// 
        public Bar(long timestamp, int id)
        {
            this._timestamp = timestamp;
            this._contractId = id;
            Open = Decimal.MinValue;
            High = Decimal.MinValue;
            Low = Decimal.MinValue;
            Close = Decimal.MinValue;
            Volume = 0;
            Wap = Decimal.MinValue;
        }


        /// <summary>
        ///     Construct a Bar object with given quotes.
        ///     Contract ID is set to not present (-1).
        /// </summary>
        /// <seealso cref="Bar(long, int)"/>
        /// 
        public Bar(long timestamp, decimal open, decimal high, decimal low, decimal close, long volume, decimal wap)
            : this(timestamp, -1, open, high, low, close, volume, wap)
        {}


        /// <summary>
        ///     Construct a Bar object with given quotes.
        /// </summary>
        /// <seealso cref="Bar(long, decimal, decimal, decimal, decimal, long, decimal)"/>
        /// 
        public Bar(long timestamp, int id, decimal open, decimal high, decimal low, decimal close, long volume, decimal wap)
        {
            this._timestamp = timestamp;
            this._contractId = id;
            Open = open + Zero00;
            High = high + Zero00;
            Low = low + Zero00;
            Close = close + Zero00;
            Volume = volume;
            Wap = wap;
        }


        private readonly decimal Zero00 = 0.00m;
        private readonly long _timestamp;   	            // begin of bar stamp in UTC ticks
        private int _contractId;				            // id of the contract; must be >= 0; -1 if not initialized

        public decimal Open { get; private set; }
        public decimal High { get; private set; }
        public decimal Low { get; private set; }
        public decimal Close { get; private set; }
        public long Volume { get; private set; }
        public decimal Wap { get; private set; }



        public bool IsEmpty
        {
            get { return Close == Decimal.MinValue; }
        }


        /// <summary>
        ///     Bar timestamp in 100ns units in UTC time.
        /// </summary>
        public long TimestampUtc
        {
            get { return _timestamp; }
        }

        public DateTime Timestamp(TimeZoneInfo timezone)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(new DateTime(_timestamp), timezone);
        }


        public int ContractId
        {
            get { return _contractId; }
        }


        public override String ToString()
        {
            return ToString(TimeZoneInfo.Local);
        }

        public String ToString(TimeZoneInfo tz)
        {
            DateTime ts = Timestamp(tz);
            if (IsEmpty)
                return String.Format("{0) |{1}|(empty)",
                    new DateTimeOffset(ts, tz.GetUtcOffset(ts)),
                    _contractId
                );
            else
                return String.Format("{0} {1}|{2},{3},{4},{5},{6}|{7}",
                    new DateTimeOffset(ts, tz.GetUtcOffset(ts)),
                    _contractId >= 0? String.Format("|{0}", _contractId) : String.Empty,
                    Open, High, Low, Close, Volume,
                    Wap != Decimal.MinValue ? Wap.ToString() : String.Empty
                );
        }
    }
}
