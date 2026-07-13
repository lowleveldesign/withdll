
## withdll - a small tool to perform DLL injections

![build](https://github.com/lowleveldesign/withdll/workflows/build/badge.svg)

This project is inspired by a sample with the same name from the [Detours repository](https://github.com/microsoft/Detours). I decided to create it as I was missing some features in the Detours' sample (most importantly, a way to inject a DLL into a running process). To make things more interesting, I decided to implement it in C#, using generated Detours bindings and NativeAOT with static detours linking. If you are interested in the binding generation, have a look at [this post](https://lowleveldesign.wordpress.com/2023/11/23/generating-c-bindings-for-native-windows-libraries/) on my blog.

You may **download the compiled binaries from the [release page](https://github.com/lowleveldesign/withdll/releases)**. Each release also contains compiled Detours sample libraries that are examples of WinAPI functions tracers. 

Withdll is a 64-bit application (compiled with NativeAOT and statically linked with the detours library) but supports both 32-bit and 64-bit targets. An example command line to inject a DLL into a running process with PID 1234 may look as follows: 

```
withdll.exe -d trcapi32.dll 1234
```

And to start, for example, winver.exe with injected traceapi libraries, you may run:

```
withdll.exe -d trcapi64.dll C:\Windows\System32\winver.exe
withdll.exe -d trcapi32.dll C:\Windows\SysWow64\winver.exe
```

Please note that you may inject multiple DLLs at once. If you compile a library for 32-bit and 64-bit architectures, add a “bitness suffix” to its base name, and withdll will replace the suffix if the target process is 32-bit. For example, if we have trcapi32.dll and trcapi64.dll in the same folder and we run `withdll.exe -d trcapi64.dll C:\Windows\SysWow64\winver.exe`, winver.exe instance will have trcapi32.dll in its loaded module list. 

Finally, if you would like to **always inject a DLL into a given application**, you may use the Image File Execution Option registry key. However, to profit from this key, withdll must play the role of a debugger when launching the application. Therefore, when defining a Debugger value key, add an additional `--debug` switch to the withdll command, for example: 

```
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\winver.exe]
"Debugger"="c:\\tools\\withdll.exe --debug -d c:\\tools\\trcapi64.dll"
```

I also recorded a short video presenting the usage of withdll with the traceapi sample library: 

[![Using detours and withdll to trace Win API calls](https://img.youtube.com/vi/q_iBojsF1sA/mqdefault.jpg)](https://www.youtube.com/watch?v=q_iBojsF1sA)

### Detours syelog library and log collector (syelogd.exe)

Detours developers implemented a logging library, syelog, based on Windows named pipes. As you may see in the sltest example, it is straightforward to use. We may receive the logged messages with the syelogd application (also a Detours sample).

Each syelog message has a timestamp, process ID, facility number, severity code, and the textual message. Syelogd prints them in separate columns in the output. The timestamp could be either absolute (as in the example output) or relative to the last received message if you use the /d option. Having covered the receiver, let us focus on the senders.

### Detours sample libraries that log Win API functions calls

The Detours repository contains a few syelog-based tracers. The most thorough tracer is [**traceapi**](https://github.com/microsoft/Detours/tree/main/samples/traceapi). It hooks [a vast number of Win32 API functions](https://github.com/microsoft/Detours/blob/main/samples/traceapi/_win32.cpp). More tailored loggers include:

- [**tracemem**](https://github.com/microsoft/Detours/tree/main/samples/tracemem) to trace heap allocations 
- [**tracereg**](https://github.com/microsoft/Detours/tree/main/samples/tracereg) to trace registry operations 
- [**tracetcp**](https://github.com/microsoft/Detours/tree/main/samples/tracetcp) to trace TCP connections 
- [**tracessl**](https://github.com/microsoft/Detours/tree/main/samples/tracessl) to trace plain text messages sent over TLS (it hooks EncryptMessage and DecryptMessage functions) 

And, if we are not satisfied with the examples provided, it is quite easy to create a custom tracer (you may start by adding new hooks to, for example, trcmem.cpp). 

The last step to start collecting Win API traces is to put the tracing libraries into the memory of the process that we want to analyze. And that is the place where withdll comes to the rescue.

