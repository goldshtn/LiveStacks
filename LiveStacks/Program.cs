using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiveStacks
{
    class Program
    {
        static void Main(string[] args)
        {
            Options options = new Options();
            if (!Parser.Default.ParseArguments(args, options))
                Environment.Exit(1);

            var session = new LiveSession(options.StackEvent, options.PidsToFilter);
            Console.CancelKeyPress += (_, __) =>
            {
                Console.WriteLine("Ctrl+C pressed, stopping...");
                session.Stop();
            };

            var resolver = new StackResolver();
            object timerSyncObject = new object();
            Timer printTimer = new Timer(_ =>
            {
                // Prevent multiple invocations of the timer from running concurrently.
                lock (timerSyncObject)
                {
                    Console.WriteLine(DateTime.Now.ToLongTimeString());
                    var stacks = session.Stacks.TopStacks(options.TopStacks);
                    session.Stacks.Clear();
                    foreach (var stack in stacks)
                    {
                        // TODO Resolve stack addresses, print folded, etc.
                        Console.WriteLine($"  {stack.Count,10} [PID {stack.ProcessID}]");
                        foreach (var symbol in resolver.Resolve(stack.ProcessID, stack.Addresses))
                        {
                            Console.WriteLine($"    {symbol.ModuleName}!{symbol.MethodName}+0x{symbol.OffsetInMethod:X}");
                        }
                    }
                }
            }, null, TimeSpan.FromSeconds(options.IntervalSeconds), TimeSpan.FromSeconds(options.IntervalSeconds));
            // TODO If the interval is 0, the user wants to print every single stack

            session.Start();

            GC.KeepAlive(printTimer);
        }
    }
}
