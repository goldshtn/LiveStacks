using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiveStacks
{
    struct Symbol
    {
        public string ModuleName { get; set; }
        public string MethodName { get; set; }
        public int OffsetInMethod { get; set; }
    }

    class StackResolver
    {
        private readonly int _processID;

        // TODO LRU cache of recently-resolved symbols

        public StackResolver(int processID)
        {
            _processID = processID;
        }

        public Symbol[] Resolve(ulong[] addresses)
        {
            throw new NotImplementedException();
        }
    }
}
