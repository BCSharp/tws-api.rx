/* Copyright © 2014 Paweł A. Konieczny
 * See "LICENCE.txt" for details.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IBApi;
using IBApi.Reactive;

namespace IBApi.Reactive
{
    class TwsSender : EClientSocket
    {
        public TwsSender(EWrapper listener) : base(listener) {}
    }
}
