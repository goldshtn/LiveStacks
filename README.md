# LiveStacks

This tool records, aggregates, and displays call stacks from interesting ETW events across the system. It resolves .NET symbols by using the CLRMD library to inspect the CLR data structures, which means it does not require CLR rundown events or NGen PDB files to be generated. On the other hand, it works only in live mode, when the collection and analysis happens on the target system. Importantly, if the target process exits before the tool had a chance to print stacks, symbol resolution will fail, so it is more suitable for longer-running processes.

> NOTE: This project is not done. There are still some unimplemented features, and the code hasn't been extensively tested. Caveat emptor, and pull requests welcome!

## Running

Open a command prompt window as administrator, and try some of the following examples.

Collect CPU sampling events system-wide and print the top hottest stacks when Ctrl+C is hit:

```
LiveStacks
```

Collect information for a specific process:

```
LiveStacks -p 7408
```

Trace stacks for triggered garbage collections (when the application calls `GC.Collect()`):

```
LiveStacks -e clr:gc:gc/triggered
```

Trace stacks for file I/O operations:

```
LiveStacks -e kernel:fileioinit
```

Trace stacks for image load (DLL/EXE) events with a custom display interval (-i) and number of top stacks to display (-T):

```
LiveStacks -e kernel:imageload -i 1 -T 5
```

* Other kernel keywords: https://github.com/Microsoft/perfview/blob/master/src/TraceEvent/Parsers/KernelTraceEventParser.cs

Print stacks in folded format, suitable for direct pass-through to the [FlameGraph.pl](https://github.com/BrendanGregg/FlameGraph) script, and only print once before quitting (-c).

```
LiveStacks -c 1 -f
```

## Example Output

Native process, heavy CPU consumption:

```
10:47:58 AM
        811 [EatCPU 7408]
    EatCPU.exe!DoWork+0x27
    EatCPU.exe!main+0x47
    EatCPU.exe!invoke_main+0x1E
    EatCPU.exe!__scrt_common_main_seh+0x15A
    EatCPU.exe!__scrt_common_main+0xD
    EatCPU.exe!mainCRTStartup+0x8
    KERNEL32.DLL!@BaseThreadInitThunk@12+0x24
    ntdll.dll!__RtlUserThreadStart+0x2F
    ntdll.dll!__RtlUserThreadStart@8+0x1B
         400 [EatCPU 7408]
    EatCPU.exe!DoWork+0x1E
    EatCPU.exe!main+0x47
    EatCPU.exe!invoke_main+0x1E
    EatCPU.exe!__scrt_common_main_seh+0x15A
    EatCPU.exe!__scrt_common_main+0xD
    EatCPU.exe!mainCRTStartup+0x8
    KERNEL32.DLL!@BaseThreadInitThunk@12+0x24
    ntdll.dll!__RtlUserThreadStart+0x2F
    ntdll.dll!__RtlUserThreadStart@8+0x1B
```

The numbers on top (811, 400) are the number of samples captured with this call stack. Then the process name and process ID are displayed, followed by the actual call stack.

Call stack from `GC/Triggered` event in Visual Studio process -- note the allocating stack, which is deep in the XML schema editor and validator:

```
5:17:04 PM
           2 [devenv 11200]
    clr.dll!EtwCallout+0x12E
    clr.dll!CoTemplate_qh+0x6F
    clr.dll!WKS::GCHeap::GarbageCollectGeneration+0x1E5
    clr.dll!WKS::gc_heap::try_allocate_more_space+0x14D
    clr.dll!WKS::gc_heap::allocate_more_space+0x18
    clr.dll!WKS::GCHeap::Alloc+0x5C
    clr.dll!Alloc+0x87
    clr.dll!AllocateObject+0x99
    clr.dll!JIT_New+0x6B
    System.dll!System.Uri.CreateHelper(System.String, Boolean, System.UriKind, System.UriFormatException ByRef)+0x6E
    System.dll!System.Uri.TryCreate(System.String, System.UriKind, System.Uri ByRef)+0x26
    Microsoft.VisualStudio.Shell.14.0.dll!Microsoft.VisualStudio.Shell.Url.Init(System.String)+0x1D
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlHelper.IsSamePath(System.String, System.String)+0x88
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Checker.HandleSchemaException(System.Exception, System.Xml.Schema.XmlSeverityType)+0x20B
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Checker.OnValidationError(System.Object, System.Xml.Schema.ValidationEventArgs)+0x1F
    System.Xml.dll!System.Xml.Schema.XmlSchemaValidator.SendValidationEvent(System.Xml.Schema.XmlSchemaValidationException, System.Xml.Schema.XmlSeverityType)+0x9F
    System.Xml.dll!System.Xml.Schema.XmlSchemaValidator.SendValidationEvent(System.String, System.String, System.Xml.Schema.XmlSeverityType)+0x8A
    System.Xml.dll!System.Xml.Schema.XmlSchemaValidator.ValidateAttribute(System.String, System.String, System.Xml.Schema.XmlValueGetter, System.String, System.Xml.Schema.XmlSchemaInfo)+0x519CB1
    System.Xml.dll!System.Xml.Schema.XmlSchemaValidator.ValidateAttribute(System.String, System.String, System.Xml.Schema.XmlValueGetter, System.Xml.Schema.XmlSchemaInfo)+0x1E
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Checker.ValidateXmlAttributes(Microsoft.XmlEditor.XmlElement)+0x154
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Checker.ValidateStartElement(Microsoft.XmlEditor.XmlElement)+0x186
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Checker.VisitXmlElement(Microsoft.XmlEditor.XmlElement)+0x125
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitorBase.Visit(Microsoft.XmlEditor.Node)+0xD6
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitor.Visit(Microsoft.XmlEditor.Node)+0x6F
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Checker.VisitXmlElement(Microsoft.XmlEditor.XmlElement)+0x1CA
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitorBase.Visit(Microsoft.XmlEditor.Node)+0xD6
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitor.Visit(Microsoft.XmlEditor.Node)+0x6F
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Checker.VisitXmlElement(Microsoft.XmlEditor.XmlElement)+0x1CA
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitorBase.Visit(Microsoft.XmlEditor.Node)+0xD6
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitor.Visit(Microsoft.XmlEditor.Node)+0x6F
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitorBase.VisitChildren(Microsoft.XmlEditor.XmlNode)+0x39
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitorBase.VisitXmlDocument(Microsoft.XmlEditor.XmlDocument)+0x19
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Checker.VisitXmlDocument(Microsoft.XmlEditor.XmlDocument)+0x5EA
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitorBase.Visit(Microsoft.XmlEditor.Node)+0x128
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlVisitor.Visit(Microsoft.XmlEditor.Node)+0x89
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Compiler.Compile(Microsoft.XmlEditor.XmlParseRequest, Microsoft.XmlEditor.XmlDocument, Microsoft.XmlEditor.ErrorNodeList)+0x91
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.Compiler.Compile(Microsoft.XmlEditor.XmlParseRequest, Microsoft.XmlEditor.ErrorNodeList)+0x3D
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlLanguageService.CompileDocument(Microsoft.XmlEditor.XmlParseRequest)+0x97
    Microsoft.XmlEditor.dll!Microsoft.XmlEditor.XmlLanguageService.ParseSource(Microsoft.VisualStudio.Package.ParseRequest)+0x231
    Microsoft.VisualStudio.Package.LanguageService.14.0.dll!Microsoft.VisualStudio.Package.LanguageService.ParseRequest(Microsoft.VisualStudio.Package.ParseRequest)+0x76
    Microsoft.VisualStudio.Package.LanguageService.14.0.dll!Microsoft.VisualStudio.Package.LanguageService.ParseThread()+0x141
    mscorlib.dll!System.Threading.ThreadHelper.ThreadStart_Context(System.Object)+0x9D
    mscorlib.dll!System.Threading.ExecutionContext.RunInternal(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object, Boolean)+0xEA
    mscorlib.dll!System.Threading.ExecutionContext.Run(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object, Boolean)+0x16
    mscorlib.dll!System.Threading.ExecutionContext.Run(System.Threading.ExecutionContext, System.Threading.ContextCallback, System.Object)+0x41
    mscorlib.dll!System.Threading.ThreadHelper.ThreadStart()+0x44
    clr.dll!CallDescrWorkerInternal+0x34
    clr.dll!CallDescrWorkerWithHandler+0x6B
    clr.dll!MethodDescCallSite::CallTargetWorker+0x16A
    clr.dll!ThreadNative::KickOffThread_Worker+0x173
    clr.dll!Thread::DoExtraWorkForFinalizer+0x1B1
    clr.dll!Thread::DoExtraWorkForFinalizer+0x234
    clr.dll!Thread::DoExtraWorkForFinalizer+0x5F8
    clr.dll!Thread::DoExtraWorkForFinalizer+0x690
    clr.dll!ThreadNative::KickOffThread+0x256
    clr.dll!Thread::intermediateThreadProc+0x55
    KERNEL32.DLL!BaseThreadInitThunk+0x24
    ntdll.dll!__RtlUserThreadStart+0x2F
    ntdll.dll!_RtlUserThreadStart+0x1B
```

Call stack from `SystemCall` event across the system -- the specific system call can be identified by looking at the WoW64 stack prior to entering the 64-bit NTDLL:

```
       17275 [devenv 11200]
    wow64win.dll!NtUserCallOneParam+0x14
    wow64win.dll!whNtUserCallOneParam+0x46
    wow64.dll!Wow64SystemServiceEx+0x153
    wow64cpu.dll!ServiceNoTurbo+0xB
    wow64.dll!Wow64KiUserCallbackDispatcher+0x3930
    wow64.dll!Wow64LdrpInitialize+0x120
    ntdll.dll!LdrpInitializeProcess+0xFC1
    ntdll.dll!_LdrpInitialize+0x506BC
    ntdll.dll!LdrpInitialize+0x3B
    ntdll.dll!LdrInitializeThunk+0xE
    win32u.dll!NtUserCallOneParam+0xC
    USER32.dll!GetQueueStatus+0x68
    msenv.dll!CMsoCMHandler::FContinueIdle+0x1B
    msenv.dll!SCM::FContinueIdle+0x25
    msenv.dll!SCM::FDoIdle+0xD2
    msenv.dll!SCM_MsoStdCompMgr::FDoIdle+0x11
    msenv.dll!MainMessageLoop::DoIdle+0x1A
    msenv.dll!CMsoCMHandler::EnvironmentMsgLoop+0x125
    msenv.dll!CMsoCMHandler::FPushMessageLoop+0x105
    msenv.dll!SCM::FPushMessageLoop+0xB9
    msenv.dll!SCM_MsoCompMgr::FPushMessageLoop+0x2A
    msenv.dll!CMsoComponent::PushMsgLoop+0x2E
    msenv.dll!VStudioMainLogged+0x5BD
    msenv.dll!VStudioMain+0x7C
    devenv.exe!util_CallVsMain+0xDE
    devenv.exe!CDevEnvAppId::Run+0xB99
    devenv.exe!WinMain+0xB8
    devenv.exe!__scrt_common_main_seh+0xFD
    KERNEL32.DLL!BaseThreadInitThunk+0x24
    ntdll.dll!+0x6587D
    ntdll.dll!+0x6584D
```

## Generating Flame Graphs

To generate a flame graph on Windows, you will need to [install Perl](https://www.perl.org/get.html) (I had a good experience with Strawberry Perl). On all platforms, you will need to download the [FlameGraph.pl](https://github.com/BrendanGregg/FlameGraph) script. Then, run LiveStacks in folded mode and pass the stacks through to FlameGraph.pl. In the following example, we sample system activity for 10 seconds, and then print output in folded format, which is later passed to the flame graph generator.

```
LiveStacks -c 1 -i 10 -f > folded.stacks
perl FlameGraph.pl folded.stacks > sampled.svg
```

Here is what a sample flame graph might look like:

![Flame graph generated from LiveStacks folded output](https://cdn.rawgit.com/goldshtn/LiveStacks/master/sampled.svg)

## Requirements/Limitations

Creating arbitrary kernel ETW sessions requires Windows 8 or later, and administrative privileges.

To resolve managed symbols, the target process must currently have the same bitness as the tool. If this condition isn't met, managed symbols are not resolved but native symbols will still work properly. This can be addressed in the future by moving the managed symbol resolution into a separate helper process.

Kernel symbols are currently not resolved, and filtered out by default.

## Overhead

The tool's overhead mostly depends on the number of events traced. For example, CPU sampling with default settings across the system runs at virtually 0% CPU overhead during collection. System call collection on an idle system was observed at 1-2% CPU usage, spiking to 5-10% when heavy I/O processes (issuing approximately 100K system calls per second) were running. As with most performance tools, measure in a stable test environment before deploying to production.

Additionally, processing symbols to display call stacks can lead to spikes of CPU, disk, and network activity as symbols are downloaded, loaded from disk, parsed in memory, and then cached. During heavy symbol processing, LiveStacks will routinely exhibit 100% CPU utilization for short time periods. To address this, you can limit the number of stacks displayed with the `-T` switch.

## Building

To build the tool, you will need Visual Studio 2015/2017, and the Windows SDK installed (for the symsrv.dll and dbghelp.dll files).
