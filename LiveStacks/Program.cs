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
        private static DateTime _lastTimerInvocation = DateTime.Now;

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

            // If there had been no timer, this is the only printout we will produce:
            if (_interval == TimeSpan.Zero)
            {
                OnTimer();
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("Total events seen: " + _session.TotalEventsSeen);
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
                // Prevent multiple invocations of the timer from running concurrently,
                // and if not enough time has elapsed, don't run the timer procedure again.
                // This may happen if there are a lot of symbols to resolve, and the timer
                // can't keep up (at least at first).
                lock (timerSyncObject)
                {
                    if (DateTime.Now - _lastTimerInvocation < _interval)
                        return;

                    _lastTimerInvocation = DateTime.Now;
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

            // Folded stacks are usually not meant for direct consumption, which means it doesn't make sense
            // to filter out top stacks. This can be done by the processing tool if necessary.
            if (_options.FoldedStacks)
                _options.TopStacks = int.MaxValue;
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
            Console.Error.WriteLine(DateTime.Now.ToLongTimeString());
            Stopwatch sw = Stopwatch.StartNew();
            var stacks = _session.Stacks.TopStacks(_options.TopStacks, _options.MinimumSamples);
            _session.Stacks.Clear();
            foreach (var stack in stacks)
            {
                if (_options.FoldedStacks)
                    PrintFoldedStack(stack);
                else
                    PrintNormalStack(stack);
            }
            Console.Error.WriteLine($"  Time aggregating/resolving: {sw.ElapsedMilliseconds}ms");
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
