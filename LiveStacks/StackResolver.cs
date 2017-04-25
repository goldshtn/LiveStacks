using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        public ulong Address { get; set; }

        public static Symbol Unknown(ulong address)
        {
            return new Symbol { Address = address };
        }

        public bool Equals(Symbol symbol)
        {
            return Address == symbol.Address;
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
            return Address.GetHashCode();
        }

        public static bool operator ==(Symbol a, Symbol b) => a.Equals(b);
        public static bool operator !=(Symbol a, Symbol b) => !a.Equals(b);

        public override string ToString()
        {
            if (String.IsNullOrEmpty(MethodName) && String.IsNullOrEmpty(ModuleName))
                return $"{Address,16:X}";
            else
                return $"{ModuleName}!{MethodName}+0x{OffsetInMethod:X}";
        }
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
        private const int E_ACCESSDENIED = -2147467259;
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

            // TODO Need IsSameArchitecture check for native target as well :-(
            try
            {
                _nativeTarget = new NativeTarget(processID);
            }
            catch (Win32Exception ex) when (ex.HResult == E_ACCESSDENIED)
            {
                // We can't open a process handle to certain processes,
                // so just skip resolving their symbols.
            }
        }

        public Symbol[] Resolve(ulong[] addresses)
        {
            return addresses.Select(address => Resolve(address)).ToArray();
        }

        private Symbol Resolve(ulong address)
        {
            Symbol result = Symbol.Unknown(address);

            if (_managedTarget != null)
                result = _managedTarget.ResolveSymbol(address);

            if (String.IsNullOrEmpty(result.MethodName) && _nativeTarget != null)
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
            catch (Win32Exception ex) when (ex.HResult == E_ACCESSDENIED)
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
        private Process _process;

        public NativeTarget(int processID)
        {
            _process = Process.GetProcessById(processID);
            _hProcess = _process.Handle;
            // TODO Need symsrv.dll to be around for symbol server loads to succeed.
            SymSetOptions(SYMOPT_DEFERRED_LOADS);
            string symbolPath = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
            // TODO Because we use invadeProcess=true, the caller must have the same bitness again
            //      We have the same restriction in the ManagedTarget, so probably what would make
            //      sense is to resolve symbols in a separate helper process :-(
            if (!SymInitialize(_hProcess, symbolPath, true))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        public void Dispose()
        {
            SymCleanup(_hProcess);
            _process.Dispose();
            _hProcess = IntPtr.Zero;
        }

        public unsafe Symbol ResolveSymbol(ulong address)
        {
            Symbol result = new Symbol
            {
                ModuleName = ModuleForAddress(address),
                Address = address
            };
            SYMBOL_INFO symbol = new SYMBOL_INFO();
            symbol.SizeOfStruct = 88;
            symbol.MaxNameLen = 256;
            ulong displacement;
            if (SymFromAddr(_hProcess, address, out displacement, ref symbol))
            {
                result.MethodName = symbol.Name;
                result.OffsetInMethod = (uint)displacement;
            }
            return result;
        }

        private string ModuleForAddress(ulong address)
        {
            return _process.Modules.Cast<ProcessModule>().FirstOrDefault(
                pm => pm.BaseAddress.ToInt64() <= (long)address &&
                (pm.BaseAddress.ToInt64() + pm.ModuleMemorySize) > (long)address)
                ?.ModuleName;
        }

        private struct SYMBOL_INFO
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
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Name;
        }

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymInitialize(IntPtr hProcess, string userSearchPath, [MarshalAs(UnmanagedType.Bool)] bool invadeProcess);

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymFromAddr(IntPtr hProcess, ulong address, out ulong displacement, ref SYMBOL_INFO symbol);

        private const uint SYMOPT_DEFERRED_LOADS = 0x00000004;

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        private static extern uint SymSetOptions(uint options);

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
                    OffsetInMethod = (uint)(address - method.NativeCode),
                    Address = address
                };
            }
            return new Symbol
            {
                ModuleName = module?.FileName ?? "[unknown]",
                OffsetInMethod = (uint)(address - module?.ImageBase ?? 0),
                Address = address
            };
        }
    }
}
