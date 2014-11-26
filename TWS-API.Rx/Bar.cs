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
        /// 
        public Bar(long timestamp)
        {
            this._timestamp = timestamp;
            Open = Decimal.MinValue;
            High = Decimal.MinValue;
            Low = Decimal.MinValue;
            Close = Decimal.MinValue;
            Volume = 0;
            Wap = Decimal.MinValue;
        }


        /// <summary>
        ///     Construct a Bar object with given quotes.
        /// </summary>
        /// <seealso cref="Bar(long)"/>
        /// 
        public Bar(long timestamp, decimal open, decimal high, decimal low, decimal close, long volume, decimal wap)
        {
            this._timestamp = timestamp;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
            Wap = wap;
        }


        private readonly long _timestamp;   	            // begin of bar stamp in UTC ticks

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


        public override String ToString()
        {
            return ToString(TimeZoneInfo.Local);
        }

        public String ToString(TimeZoneInfo tz)
        {
            DateTime ts = Timestamp(tz);
            if (IsEmpty)
                return String.Format("{0) |(empty)",
                    new DateTimeOffset(ts, tz.GetUtcOffset(ts))
                );
            else
                return String.Format("{0} |{1},{2},{3},{4},{5}|{6}",
                    new DateTimeOffset(ts, tz.GetUtcOffset(ts)),
                    Open, High, Low, Close, Volume,
                    Wap != Decimal.MinValue ? Wap.ToString() : String.Empty
                );
        }
    }
}
