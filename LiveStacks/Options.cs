using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiveStacks
{
    class Options
    {
        [Option('f', "folded", Required =false, DefaultValue = false,
            HelpText = "Emit folded stacks in a format suitable for flame graph generation")]
        public bool FoldedStacks { get; set; }
        // TODO Do we want to include the actual flame graph generation too?

        [Option('T', "top", Required = false, DefaultValue = 10,
            HelpText = "Print the top stacks, sorted by popularity")]
        public int TopStacks { get; set; }

        [Option('i', "interval", Required = false, DefaultValue = 5,
            HelpText = "How often to print the stack summary")]
        public double IntervalSeconds { get; set; }

        [Option('c', "count", Required = false, DefaultValue = -1,
            HelpText = "How many times to print a summary before quitting (default = indefinite)")]
        public int Count { get; set; }

        [Option('K', "kernel", Required = false, DefaultValue = false,
            HelpText = "Include kernel frames in the stack report")]
        public bool IncludeKernelFrames { get; set; }

        [Option('P', "pname", Required = false, MutuallyExclusiveSet = "pname",
            HelpText = "Display stacks only from this process (by name)")]
        public string ProcessName { get; set; }

        [Option('p', "pid", Required = false, MutuallyExclusiveSet = "pid",
            HelpText = "Display stacks only from this process (by id)")]
        public int ProcessID { get; set; }

        [Option('e', "event", Required = false, DefaultValue = "kernel:profile",
            HelpText = "The event for which to capture call stacks. For kernel events, specify the " +
            "keyword only, e.g. \"kernel:profile\". For CLR events, specify the keyword and the event name, " +
            "e.g. \"clr:gc:gcstart\". Only kernel and CLR events are currently supported.")]
        public string StackEvent { get; set; }

        public IEnumerable<int> PidsToFilter
        {
            get
            {
                if (ProcessID != 0)
                    return new int[] { ProcessID };

                if (!String.IsNullOrEmpty(ProcessName))
                    throw new NotImplementedException();

                return Enumerable.Empty<int>();
            }
        }

        [HelpOption]
        public string GetUsage()
        {
            var helpText = HelpText.AutoBuild(new Options());
            helpText.Copyright = "Copyright Sasha Goldshtein, 2017 under the MIT License.";
            helpText.Heading = "LiveStacks - print and aggregate live stacks from ETW events.";
            return helpText.ToString();
        }
    }
}
