﻿using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace LiveStacks
{
    class LiveSession
    {
        private TraceEventSession _session;
        private readonly string _stackEvent;
        private List<int> _processIDs = new List<int>();

        public AggregatedStacks Stacks { get; private set; } = new AggregatedStacks();

        public LiveSession(string stackEvent, IEnumerable<int> processIDs)
        {
            _stackEvent = stackEvent;
            if (_processIDs != null)
            {
                _processIDs.AddRange(processIDs);
            }
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
            // TODO Use the stack event to decide what to enable
            // TODO Make the CPU sampling interval configurable, although changing it doesn't seem to work in a VM?
            // _session.CpuSampleIntervalMSec = 10.0f;
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.Profile,
                KernelTraceEventParser.Keywords.Profile);
            // TODO Should we use _session.StackCompression? What would the events look like?

            _session.Source.Kernel.StackWalkStack += OnStackEvent;
            _session.Source.Process();
        }

        private void OnStackEvent(StackWalkStackTraceData stack)
        {
            if (!ProcessFilter(stack.ProcessID))
                return;

            ulong[] addresses = new ulong[stack.FrameCount];
            for (int i = 0; i < addresses.Length; ++i)
            {
                addresses[i] = stack.InstructionPointer(i);
            }
            Stacks.AddStack(stack.ProcessID, addresses);
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