using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
            return ResolverFor(processID).Resolve(addresses);
        }

        public string ProcessName(int processID) => ResolverFor(processID).ProcessName;

        private ProcessStackResolver ResolverFor(int processID)
        {
            ProcessStackResolver resolver;
            if (!_resolvers.TryGetValue(processID, out resolver))
            {
                resolver = new ProcessStackResolver(processID);
                _resolvers.Add(processID, resolver);
            }
            return resolver;
        }
    }

    class ProcessStackResolver
    {
        private const int E_ACCESSDENIED = -2147467259;
        private readonly int _processID;
        private ManagedTarget _managedTarget;
        private NativeTarget _nativeTarget;
        private SymbolCache _symbolCache = new SymbolCache(1024);

        public ProcessStackResolver(int processID)
        {
            _processID = processID;
            bool isManagedProcess = IsManagedProcess(processID);
            if (isManagedProcess && IsSameArchitecture(processID))
                _managedTarget = new ManagedTarget(processID);

            // If we are a 32-bit process, we cannot resolve symbols
            // in a 64-bit target. Just bail.
            if (Is32BitAttachingTo64Bit(processID))
                return;

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

        public string ProcessName => _nativeTarget?.ProcessName;

        public Symbol[] Resolve(ulong[] addresses)
        {
            return addresses.Select(address => _symbolCache.GetOrAdd(address, Resolve)).ToArray();
        }

        private Symbol Resolve(ulong address)
        {
            Symbol result = Symbol.Unknown(address);

            if (_managedTarget != null)
                result = _managedTarget.ResolveSymbol(address);

            if (String.IsNullOrEmpty(result.MethodName) && _nativeTarget != null)
                result = _nativeTarget.ResolveSymbol(address);

            // TODO We are currently not resolving kernel symbols at all. They do not lie in
            //      any user-space module, so we don't recognize the addresses.

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
            catch (Win32Exception ex) when (ex.HResult == E_ACCESSDENIED)
            {
                return false; // This might be a process we can't touch
            }
        }

        static private bool Is32BitAttachingTo64Bit(int processID)
        {
            return !Environment.Is64BitProcess && Is64BitProcess(processID);
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
        private HashSet<string> _loadedModules = new HashSet<string>();

        public NativeTarget(int processID)
        {
            _process = Process.GetProcessById(processID);
            _hProcess = _process.Handle;
            // Note that symsrv.dll and an updated dbghelp.dll (from the Debugging Tools)
            // need to be around for the symbol loads to succeed. There is a post-build
            // step that copies them over to the output directory, or we could bundle them
            // with the project.
            SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS);
            string symbolPath = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
            if (!SymInitialize(_hProcess, symbolPath, invadeProcess: false))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        public void Dispose()
        {
            SymCleanup(_hProcess);
            _process.Dispose();
            _hProcess = IntPtr.Zero;
        }

        public string ProcessName => _process.ProcessName;

        public Symbol ResolveSymbol(ulong address)
        {
            var module = ModuleForAddress(address);
            Symbol result = new Symbol
            {
                ModuleName = module.ModuleName,
                Address = address
            };

            if (!String.IsNullOrEmpty(module.FileName) && !_loadedModules.Contains(module.FileName))
            {
                if (0 == SymLoadModule64(_hProcess, IntPtr.Zero, module.FileName, module.ModuleName,
                    module.BaseAddress, module.Size))
                {
                    return result;
                }
                _loadedModules.Add(module.FileName);
            }

            SYMBOL_INFO symbol = new SYMBOL_INFO();
            symbol.SizeOfStruct = 88;
            symbol.MaxNameLen = 1024;
            ulong displacement;
            if (SymFromAddr(_hProcess, address, out displacement, ref symbol))
            {
                result.MethodName = symbol.Name;
                result.OffsetInMethod = (uint)displacement;
            }
            return result;
        }

        private LightProcessModule ModuleForAddress(ulong address)
        {
            // System.Diagnostics.Process.Modules returns only the 64-bit modules
            // if attached to a 32-bit target, so we have to use our own implementation.
            return ProcessModules().FirstOrDefault(
                pm => pm.BaseAddress <= address &&
                (pm.BaseAddress + pm.Size) > address);
        }

        private IEnumerable<LightProcessModule> ProcessModules()
        {
            IntPtr[] moduleHandles = new IntPtr[1024];
            uint sizeNeeded;
            if (!K32EnumProcessModulesEx(_hProcess, moduleHandles, (uint)(moduleHandles.Length * IntPtr.Size),
                out sizeNeeded, LIST_MODULES_ALL))
            {
                yield break;
            }
            var buffer = new StringBuilder(2048);
            foreach (var moduleHandle in moduleHandles.Take((int)(sizeNeeded / IntPtr.Size)))
            {
                string fileName = "", baseName = "";
                if (0 != K32GetModuleFileNameEx(_hProcess, moduleHandle, buffer, (uint)buffer.Capacity))
                    fileName = buffer.ToString();
                if (0 != K32GetModuleBaseName(_hProcess, moduleHandle, buffer, (uint)buffer.Capacity))
                    baseName = buffer.ToString();
                MODULEINFO moduleInfo = new MODULEINFO();
                if (K32GetModuleInformation(_hProcess, moduleHandle, out moduleInfo, (uint)Marshal.SizeOf(moduleInfo)))
                {
                    yield return new LightProcessModule
                    {
                        FileName = fileName,
                        ModuleName = baseName,
                        BaseAddress = (ulong)moduleInfo.lpBaseOfDll.ToInt64(),
                        Size = moduleInfo.SizeOfImage
                    };
                }
            }
        }

        private struct LightProcessModule
        {
            public string ModuleName { get; set; }
            public string FileName { get; set; }
            public ulong BaseAddress { get; set; }
            public uint Size { get; set; }
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
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string Name;
        }

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymInitialize(IntPtr hProcess, string userSearchPath, [MarshalAs(UnmanagedType.Bool)] bool invadeProcess);

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymFromAddr(IntPtr hProcess, ulong address, out ulong displacement, ref SYMBOL_INFO symbol);

        private const uint SYMOPT_UNDNAME = 0x02;
        private const uint SYMOPT_DEFERRED_LOADS = 0x00000004;

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        private static extern uint SymSetOptions(uint options);

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern ulong SymLoadModule64(IntPtr hProcess, IntPtr hFile, string imageName, string moduleName, ulong baseAddress, uint size);

        [DllImport("dbghelp.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SymCleanup(IntPtr hProcess);

        private const uint LIST_MODULES_32BIT = 0x01;
        private const uint LIST_MODULES_64BIT = 0x02;
        private const uint LIST_MODULES_ALL = 0x03;
        private const uint LIST_MODULES_DEFAULT = 0x0;

        private struct MODULEINFO
        {
            public IntPtr lpBaseOfDll;
            public uint SizeOfImage;
            public IntPtr EntryPoint;
        }

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool K32EnumProcessModulesEx(IntPtr hProcess, IntPtr[] moduleHandles, uint sizeOfModuleHandles, out uint sizeNeeded, uint filterFlag);

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        private static extern uint K32GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, StringBuilder filename, uint size);

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        private static extern uint K32GetModuleBaseName(IntPtr hProcess, IntPtr hModule, StringBuilder baseName, uint size);

        [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool K32GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO moduleInfo, uint size);
    }

    class ManagedTarget
    {
        private DataTarget _dataTarget;
        private List<ClrRuntime> _runtimes;
        private List<ModuleInfo> _modules;

        public ManagedTarget(int processID)
        {
            // TODO This can only be done from a process with the same bitness, so we might want to move this out
            _dataTarget = DataTarget.AttachToProcess(processID, 1000, AttachFlag.Passive);
            _runtimes = _dataTarget.ClrVersions.Select(clr => clr.CreateRuntime()).ToList();
            _modules = new List<ModuleInfo>(_dataTarget.EnumerateModules());
        }

        public Symbol ResolveSymbol(ulong address)
        {
            var module = _modules.FirstOrDefault(
                m => m.ImageBase <= address && (m.ImageBase + m.FileSize) > address);
            var method = _runtimes.Select(runtime => runtime.GetMethodByAddress(address))
                                  .FirstOrDefault(m => m != null);
            if (method != null)
            {
                return new Symbol
                {
                    ModuleName = Path.GetFileName(module?.FileName ?? "[unknown]"),
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
