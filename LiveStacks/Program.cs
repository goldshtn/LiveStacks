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

            Timer printTimer = new Timer(_ =>
            {
                Console.WriteLine(DateTime.Now.ToLongTimeString());
                var stacks = session.Stacks.TopStacks(options.TopStacks);
                session.Stacks.Clear();
                foreach (var stack in stacks)
                {
                    // TODO Resolve stack addresses, print folded, etc.
                    Console.WriteLine($"  {stack.Count,10} [PID {stack.ProcessID}]");
                }
            }, null, TimeSpan.FromSeconds(options.IntervalSeconds), TimeSpan.FromSeconds(options.IntervalSeconds));

            session.Start();

            GC.KeepAlive(printTimer);
        }
    }
}
