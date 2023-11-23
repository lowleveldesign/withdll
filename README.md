
## withdll - a small tool to perform DLL injections

![build](https://github.com/lowleveldesign/withdll/workflows/build/badge.svg)

This project is inspired by a sample with the same name from the [Detours repository](https://github.com/microsoft/Detours). I decided to create it as I was missing some features in the Detours' sample (most importantly, a way to inject a DLL into a running process). To make things more interesting, I decided to implement it in C#, using generated Detours bindings and NativeAOT with static detours linking. If you are interested in the binding generation, have a look at [this post](https://lowleveldesign.wordpress.com/2023/11/22/generating-c-bindings-for-native-windows-libraries/) on my blog.

You may **download the compiled binaries from the [release page](https://github.com/lowleveldesign/withdll/releases)**. Each release also contains compiled Detours sample libraries that are examples of WinAPI functions tracers. I write more on how to use them in [a guide on wtrace.net](https://wtrace.net/guides/using-withdll-and-detours-to-trace-winapi/).

Although withdll is a 64-bit application, it **supports injecting DLLs into both 32-bit and 64-bit processes**. 

Example command lines:

```
withdll.exe -d trcapi64.dll C:\Windows\System32\winver.exe
withdll.exe -d trcapi32.dll C:\Windows\SysWow64\winver.exe

withdll.exe -d trcapi32.dll 1234
```

Additionally, you may install withdll as a **Image File Execution Options debugger** for a given executable, which would allow you to inject a DLL (or DLLs) on every application launch. The **--debug** option is required for this to work so please make sure you add it, for example:

```
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\winver.exe]
"Debugger"="c:\\tools\\withdll.exe --debug -d c:\\tools\\trcapi64.dll"
```
