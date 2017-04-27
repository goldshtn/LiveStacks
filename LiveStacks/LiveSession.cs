using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing;

namespace LiveStacks
{
    class LiveSession
    {
        private static readonly string[] SupportedProviders = new[] { "kernel", "clr" };

        private TraceEventSession _session;
        private List<int> _processIDs = new List<int>();
        private readonly bool _includeKernelFrames;
        private string _provider;
        private KernelTraceEventParser.Keywords _kernelKeyword;
        private ClrTraceEventParser.Keywords _clrKeyword;
        private string _clrEventName;
        private Dictionary<int, string> _lastEventNameByThread = new Dictionary<int, string>();

        public AggregatedStacks Stacks { get; private set; } = new AggregatedStacks();

        public ulong TotalEventsSeen { get; private set; }

        public LiveSession(string stackEvent, IEnumerable<int> processIDs, bool includeKernelFrames)
        {
            ParseProvider(stackEvent);
            if (_processIDs != null)
            {
                _processIDs.AddRange(processIDs);
            }
            _includeKernelFrames = includeKernelFrames;
        }

        /// <summary>
        /// Starts the ETW session, and processes the stack samples that come in. This method
        /// does not return until another thread calls <see cref="Stop"/> to stop the session.
        /// While this method is executing, live stack aggregates can be obtained from the 
        /// <see cref="Stacks"/> property, which is thread-safe.
        /// </summary>
        public void Start()
        {
            _session = new TraceEventSession($"LiveStacks-{Process.GetCurrentProcess().Id}");

            // TODO Make the CPU sampling interval configurable, although changing it doesn't seem to work in a VM?
            // _session.CpuSampleIntervalMSec = 10.0f;

            // TODO Should we use _session.StackCompression? What would the events look like?

            if (_provider == "kernel")
            {
                _session.EnableKernelProvider(_kernelKeyword, stackCapture: _kernelKeyword);
                _session.Source.Kernel.StackWalkStack += OnKernelStackEvent;
            }

            if (_provider == "clr")
            {
                _session.EnableProvider(
                    ClrTraceEventParser.ProviderGuid,
                    matchAnyKeywords: (ulong)(_clrKeyword | ClrTraceEventParser.Keywords.Stack));
                _session.Source.Clr.All += OnAnyClrEvent;
                _session.Source.Clr.ClrStackWalk += OnClrStackEvent;
            }

            _session.Source.Process();
        }

        private void ParseProvider(string eventSpec)
        {
            string[] parts = eventSpec.Split(':');
            if (parts.Length != 2 && parts.Length != 3)
            {
                throw new ArgumentException(
                    "Event specification must have two or three components separated by a colon, " +
                    "e.g. 'kernel:profile' or 'clr:gc:gc/allocationtick'.");
            }
            if (!SupportedProviders.Contains(parts[0], StringComparer.InvariantCultureIgnoreCase))
            {
                throw new ArgumentException(
                    "Event specification provider was not recognized. Supported providers: " +
                    String.Join(", ", SupportedProviders));
            }
            _provider = parts[0].ToLower();
            ParseEvent(parts.Skip(1).ToArray());
        }

        private void ParseEvent(string[] parts)
        {
            if (_provider == "kernel")
            {
                string keyword = parts[0];
                KernelTraceEventParser.Keywords parsedKeyword;
                if (!Enum.TryParse(keyword, true, out parsedKeyword))
                {
                    throw new ArgumentException("Unrecognized kernel keyword: '" + keyword + "'.");
                }
                _kernelKeyword = parsedKeyword;
            }
            else if (_provider == "clr")
            {
                if (parts.Length != 2)
                {
                    throw new ArgumentException("CLR event specification must contain a keyword and an event name.");
                }

                string keyword = parts[0];
                string eventName = parts[1];
                ClrTraceEventParser.Keywords parsedKeyword;
                if (!Enum.TryParse(keyword, true, out parsedKeyword))
                {
                    throw new ArgumentException("Unrecognized CLR keyword: '" + keyword + "'.");
                }
                _clrKeyword = parsedKeyword;
                _clrEventName = eventName.ToLower();
            }
        }

        private void OnAnyClrEvent(TraceEvent anyEvent)
        {
            if (anyEvent.EventName != "ClrStack/Walk")
                _lastEventNameByThread[anyEvent.ThreadID] = anyEvent.EventName;
        }

        private void OnClrStackEvent(ClrStackWalkTraceData stack)
        {
            ++TotalEventsSeen;

            if (!ProcessFilter(stack.ProcessID))
                return;

            string lastEventName;
            if (!_lastEventNameByThread.TryGetValue(stack.ThreadID, out lastEventName))
                return;

            if (!lastEventName.Equals(_clrEventName, StringComparison.InvariantCultureIgnoreCase))
                return;

            ulong[] addresses = new ulong[stack.FrameCount];
            for (int i = 0; i < addresses.Length; ++i)
            {
                addresses[i] = stack.InstructionPointer(i);
            }
            Stacks.AddStack(stack.ProcessID, addresses);
        }

        private void OnKernelStackEvent(StackWalkStackTraceData stack)
        {
            ++TotalEventsSeen;

            if (!ProcessFilter(stack.ProcessID))
                return;

            ulong[] addresses = new ulong[stack.FrameCount];
            int recordedIdx = 0;
            for (int originalFrameIdx = 0; originalFrameIdx < stack.FrameCount; ++originalFrameIdx)
            {
                ulong ip = stack.InstructionPointer(originalFrameIdx);
                if (_includeKernelFrames || (ip < 0x8000000000000000))
                    addresses[recordedIdx++] = ip;
            }
            if (recordedIdx > 0) // This could have been a purely kernel stack
            {
                Stacks.AddStack(stack.ProcessID, addresses);
            }
        }

        private bool ProcessFilter(int processID)
        {
            return _processIDs.Count == 0 || _processIDs.Contains(processID);
        }

        public void Stop()
        {
            _session.Stop();
        }
    }
}
