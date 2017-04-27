using CommandLine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiveStacks
{
    class Program
    {
        private const double DefaultIntervalSeconds = 5.0;

        private static StackResolver _resolver = new StackResolver();
        private static LiveSession _session;
        private static Options _options;
        private static Timer _timer;
        private static int _invocationsLeft;
        private static TimeSpan _interval = TimeSpan.Zero;

        private static void Main()
        {
            ParseCommandLineArguments();
            SetIntervalAndRepetitions();

            try
            {
                _session = new LiveSession(_options.StackEvent, _options.PidsToFilter, _options.IncludeKernelFrames);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Error creating session: " + ex.Message);
                Environment.Exit(1);
            }
            Console.CancelKeyPress += (sender, args) =>
            {
                Console.Error.WriteLine("Ctrl+C pressed, stopping...");
                args.Cancel = true;
                _session.Stop();
            };

            SetupTimer();
            _session.Start();

            // Produce one final printout before quitting. If there was no interval, this could
            // be the only printout we produce.
            OnTimer();
        }

        private static void SetupTimer()
        {
            // If there is no interval, we don't need to print at timed intervals. Just wait for the
            // user to hit Ctrl+C and exit the session.
            if (_interval == TimeSpan.Zero)
                return;

            object timerSyncObject = new object();
            _timer = new Timer(_ =>
            {
                // Prevent multiple invocations of the timer from running concurrently.
                lock (timerSyncObject)
                {
                    OnTimer();
                    if (--_invocationsLeft == 0)
                    {
                        _session.Stop();
                    }
                }
            }, null, _interval, _interval);
        }

        private static void ParseCommandLineArguments()
        {
            var parser = new Parser(ps =>
            {
                ps.CaseSensitive = true;
                ps.IgnoreUnknownArguments = false;
            });
            _options = new Options();
            if (!parser.ParseArguments(Environment.GetCommandLineArgs(), _options))
                Environment.Exit(1);
        }

        private static void SetIntervalAndRepetitions()
        {
            if (_options.Count == 0)
            {
                // The user wants an indefinite number of repetitions if an interval is specified,
                // or run without printing until Ctrl+C if no interval is specified.
                _invocationsLeft = int.MaxValue;
                if (_options.IntervalSeconds != 0.0)
                    _interval = TimeSpan.FromSeconds(_options.IntervalSeconds);
            }
            else
            {
                // A number of printouts was specified, set a default interval if one was not provided.
                _invocationsLeft = _options.Count;
                _interval = TimeSpan.FromSeconds(_options.IntervalSeconds == 0.0 ? DefaultIntervalSeconds : _options.IntervalSeconds);
            }
        }

        private static void OnTimer()
        {
            if (_options.ClearScreen)
            {
                Console.Clear();
            }
            if (!_options.FoldedStacks)
            {
                Console.WriteLine(DateTime.Now.ToLongTimeString());
            }
            Stopwatch sw = Stopwatch.StartNew();
            var stacks = _session.Stacks.TopStacks(_options.TopStacks);
            _session.Stacks.Clear();
            foreach (var stack in stacks)
            {
                if (_options.FoldedStacks)
                    PrintFoldedStack(stack);
                else
                    PrintNormalStack(stack);
            }
            if (!_options.FoldedStacks)
            {
                Console.WriteLine($"  Time aggregating/resolving: {sw.ElapsedMilliseconds}ms");
            }
        }

        private static void PrintNormalStack(AggregatedStack stack)
        {
            int pid = stack.ProcessID;
            Console.WriteLine($"  {stack.Count,10} [{_resolver.ProcessName(pid)} {pid}]");
            foreach (var symbol in _resolver.Resolve(pid, stack.Addresses))
            {
                Console.WriteLine("    " + symbol.ToString());
            }
            Console.WriteLine();
        }

        private static void PrintFoldedStack(AggregatedStack stack)
        {
            int pid = stack.ProcessID;
            Console.Write($"{_resolver.ProcessName(pid)} ({pid})");
            foreach (var symbol in _resolver.Resolve(pid, stack.Addresses).Reverse())
            {
                Console.Write(";" + symbol.ToString());
            }
            Console.WriteLine(" " + stack.Count);
        }
    }
}
