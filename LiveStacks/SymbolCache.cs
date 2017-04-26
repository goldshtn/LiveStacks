using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiveStacks
{
    class SymbolCache
    {
        private Dictionary<ulong, Symbol> _cache = new Dictionary<ulong, Symbol>();
        private readonly int _maxEntries;

        public SymbolCache(int maxEntries)
        {
            _maxEntries = maxEntries;
        }

        public Symbol GetOrAdd(ulong address, Func<ulong, Symbol> resolver)
        {
            Symbol symbol;
            if (_cache.TryGetValue(address, out symbol))
                return symbol;

            if (_cache.Count == _maxEntries)
                DiscardOne();

            symbol = resolver(address);
            _cache.Add(address, symbol);
            return symbol;
        }

        private void DiscardOne()
        {
            // TODO This is a very trivial approach, we could switch to LRU cache if needed.
            _cache.Remove(_cache.First().Key);
        }
    }
}
