﻿/* Copyright © 2014 Paweł A. Konieczny
 * See "LICENCE.txt" for details.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace IBApi.Reactive
{
    class TwsSender : EClientSocket
    {
        public TwsSender(EWrapper listener) : base(listener, new EReaderMonitorSignal()) {}
    }
}
