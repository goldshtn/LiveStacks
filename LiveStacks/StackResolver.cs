using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LiveStacks
{
    struct Symbol : IEquatable<Symbol> 
    {
        public string ModuleName { get; set; }
        public string MethodName { get; set; }
        public uint OffsetInMethod { get; set; }

        public static Symbol Unknown = new Symbol();

        public bool Equals(Symbol symbol)
        {
            return symbol.ModuleName == ModuleName &&
                symbol.MethodName == MethodName &&
                symbol.OffsetInMethod == OffsetInMethod;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Symbol))
                return false;

            Symbol other = (Symbol)obj;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            return 0; // TODO?
        }

        public static bool operator==(Symbol a, Symbol b) => a.Equals(b);
        public static bool operator!=(Symbol a, Symbol b) => !a.Equals(b);
    }

    class StackResolver
    {
        private Dictionary<int, ProcessStackResolver> _resolvers = new Dictionary<int, ProcessStackResolver>();

        public Symbol[] Resolve(int processID, ulong[] addresses)
        {
            ProcessStackResolver resolver;
            if (!_resolvers.TryGetValue(processID, out resolver))
            {
                resolver = new ProcessStackResolver(processID);
                _resolvers.Add(processID, resolver);
            }
            return resolver.Resolve(addresses);
        }
    }

    class ProcessStackResolver
    {
        private readonly int _processID;
        private ManagedTarget _managedTarget;
        private NativeTarget _nativeTarget;

        // TODO LRU cache of recently-resolved symbols

        public ProcessStackResolver(int processID)
        {
            _processID = processID;
            bool isManagedProcess = IsManagedProcess(processID);
            if (isManagedProcess && IsSameArchitecture(processID))
                _managedTarget = new ManagedTarget(processID);

            _nativeTarget = new NativeTarget(processID);
        }

        public Symbol[] Resolve(ulong[] addresses)
        {
            return addresses.Select(address => Resolve(address)).ToArray();
        }

        private Symbol Resolve(ulong address)
        {
            Symbol result = Symbol.Unknown;

            if (_managedTarget != null)
                result = _managedTarget.ResolveSymbol(address);

            if (String.IsNullOrEmpty(result.MethodName))
                result = _nativeTarget.ResolveSymbol(address);

            return result;
        }

        static private bool IsManagedProcess(int processID)
        {
            try
            {
                Process proc = Process.GetProcessById(processID);
                return proc.Modules.Cast<ProcessModule>().Any(
                    module => module.FileName.EndsWith("clr.dll", StringComparison.InvariantCultureIgnoreCase) ||
                    module.FileName.EndsWith("mscorwks.dll", StringComparison.InvariantCultureIgnoreCase) ||
                    module.FileName.EndsWith("coreclr.dll", StringComparison.InvariantCultureIgnoreCase));
            }
            catch (Exception)
            {
                return false; // This might be a process we can't touch
            }
        }

        static private bool Is64BitProcess(int processID)
        {
            if (!Environment.Is64BitOperatingSystem)
                return false;

            try
            {
                Process proc = Process.GetProcessById(processID);
                bool isWow64Process;
                if (!IsWow64Process(proc.Handle, out isWow64Process))
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                return !isWow64Process;
            }
            catch (Exception)
            {
                return false; // This might be a process we can't touch
            }
        }

        static private bool IsSameArchitecture(int processID)
        {
            return Is64BitProcess(processID) == Environment.Is64BitProcess;
        }

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process([In] IntPtr process, [Out] out bool wow64Process);
    }

    class NativeTarget : IDisposable
    {
        private IntPtr _hProcess;

        public NativeTarget(int processID)
        {
            _hProcess = new IntPtr(processID);
            // TODO Symbol path
            if (!SymInitialize(_hProcess, null, false))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        public void Dispose()
        {
            SymCleanup(_hProcess);
            _hProcess = IntPtr.Zero;
        }

        public unsafe Symbol ResolveSymbol(ulong address)
        {
            SYMBOL_INFO symbol = new SYMBOL_INFO();
            symbol.SizeOfStruct = (uint)(Marshal.SizeOf(typeof(SYMBOL_INFO)) - sizeof(char) * 255);
            ulong displacement;
            if (SymFromAddr(_hProcess, address, out displacement, ref symbol))
            {
                return new Symbol
                {
                    ModuleName = "[TODO]",
                    MethodName = new string(symbol.Name),
                    OffsetInMethod = (uint)displacement
                };
            }
            return Symbol.Unknown;
        }

        private unsafe struct SYMBOL_INFO
        {
            public uint SizeOfStruct;
            public uint TypeIndex;
            public ulong Reserved1;
            public ulong Reserved2;
            public uint Index;
            public uint Size;
            public ulong ModBase;
            public uint Flags;
            public ulong Value;
            public ulong Address;
            public uint Register;
            public uint Scope;
            public uint Tag;
            public uint NameLen;
            public uint MaxNameLen;
            public fixed char Name[256];
        }

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymInitialize(IntPtr hProcess, string userSearchPath, [MarshalAs(UnmanagedType.Bool)] bool invadeProcess);

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymFromAddr(IntPtr hProcess, ulong address, out ulong displacement, ref SYMBOL_INFO symbol);

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymCleanup(IntPtr hProcess);
    }

    class ManagedTarget
    {
        private DataTarget _dataTarget;
        private ClrRuntime _runtime;
        private List<ModuleInfo> _modules;

        public ManagedTarget(int processID)
        {
            // TODO This can only be done from a process with the same bitness, so we might want to move this out
            _dataTarget = DataTarget.AttachToProcess(processID, 1000, AttachFlag.Passive);
            _runtime = _dataTarget.ClrVersions[0].CreateRuntime();  // TODO There could be more than one runtime
            _modules = new List<ModuleInfo>(_dataTarget.EnumerateModules());
        }

        public Symbol ResolveSymbol(ulong address)
        {
            // TODO Switch to binary search if this poses a perf issue
            var module = _modules.FirstOrDefault(m => m.ImageBase <= address && (m.ImageBase + m.FileSize) > address);
            var method = _runtime.GetMethodByAddress(address);
            if (method != null)
            {
                return new Symbol
                {
                    ModuleName = module?.FileName ?? "[unknown]",
                    MethodName = method.GetFullSignature(),
                    OffsetInMethod = (uint)(address - method.NativeCode)
                };
            }
            return new Symbol
            {
                ModuleName = module?.FileName ?? "[unknown]",
                OffsetInMethod = (uint)(address - module?.ImageBase ?? 0)
            };
        }
    }
}
