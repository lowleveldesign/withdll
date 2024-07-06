using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.SystemServices;
using Windows.Win32.System.ProcessStatus;
using Windows.Win32.System.Threading;
using PInvokeDetours = Microsoft.Detours.PInvoke;
using PInvokeWin32 = Windows.Win32.PInvoke;

namespace withdll;

static class DllInjection
{
    public static void StartProcessWithDlls(List<string> cmdlineArgs, bool debug, List<string> dllPaths)
    {
        unsafe
        {
            var startupInfo = new STARTUPINFOW() { cb = (uint)sizeof(STARTUPINFOW) };

            var cmdline = new Span<char>(ConvertStringListToNullTerminatedArray(cmdlineArgs));
            uint createFlags = debug ? (uint)PROCESS_CREATION_FLAGS.DEBUG_ONLY_THIS_PROCESS : 0;

            var pcstrs = dllPaths.Select(p => new PCSTR((byte*)Marshal.StringToHGlobalAnsi(p))).ToArray();

            try
            {
                if (!PInvokeDetours.DetourCreateProcessWithDlls(null, ref cmdline, null, null, false,
                    createFlags, null, null, startupInfo, out var processInfo,
                    pcstrs, null))
                {
                    throw new Win32Exception();
                }

                PInvokeWin32.CloseHandle(processInfo.hThread);
                PInvokeWin32.CloseHandle(processInfo.hProcess);

                if (debug)
                {
                    PInvokeWin32.DebugActiveProcessStop(processInfo.dwProcessId);
                }
            }
            finally
            {
                Array.ForEach(pcstrs, pcstr => Marshal.FreeHGlobal((nint)pcstr.Value));
            }
        }

        static char[] ConvertStringListToNullTerminatedArray(IList<string> strings)
        {
            var chars = new List<char>(strings.Select(a => a.Length + 3 /* two apostrophes and space */).Sum());
            foreach (var s in strings)
            {
                chars.Add('\"');
                chars.AddRange(s);
                chars.Add('\"');
                chars.Add(' ');
            }
            chars[^1] = '\0';
            return [.. chars];
        }

    }

    public static void InjectDllsIntoRunningProcess(int pid, List<string> dllPaths)
    {
        using var remoteProcessHandle = PInvokeWin32.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_CREATE_THREAD |
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION |
            PROCESS_ACCESS_RIGHTS.PROCESS_VM_WRITE | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ, false, (uint)pid);

        var isWow64 = IsWow64(remoteProcessHandle);

        QueueApcThread queueApcThreadFunc = isWow64 ? Imports.RtlQueueApcWow64Thread : Imports.NtQueueApcThread;

        var systemFolderPath = isWow64 ? Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) :
                                    Environment.GetFolderPath(Environment.SpecialFolder.System);

        var fnRtlExitUserThread = GetModuleExportOffset(Path.Combine(systemFolderPath, "ntdll.dll"), "RtlExitUserThread");
        var fnLoadLibraryW = GetModuleExportOffset(Path.Combine(systemFolderPath, "kernel32.dll"), "LoadLibraryW");

        var remoteNtdllAddress = GetModuleHandle(remoteProcessHandle, isWow64, "ntdll.dll");
        var remoteKernel32Address = GetModuleHandle(remoteProcessHandle, isWow64, "kernel32.dll");

        var remoteThreadStart = remoteNtdllAddress + (nint)fnRtlExitUserThread;

        if (Imports.RtlCreateUserThread(remoteProcessHandle.DangerousGetHandle(), nint.Zero, true, 0, 0, 0, remoteThreadStart,
             nint.Zero, out var remoteThreadHandle, out _) is var status && status != 0)
        {
            throw new Win32Exception((int)PInvokeWin32.RtlNtStatusToDosError(new NTSTATUS(status)));
        }

        try
        {
            unsafe
            {
                var allocLength = (nuint)dllPaths.Select(p => (p.Length + 1 /* +1 for null terminator */) * sizeof(char)).Sum();
                var allocAddr = PInvokeWin32.VirtualAllocEx(remoteProcessHandle, null, allocLength,
                    VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE | VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT, PAGE_PROTECTION_FLAGS.PAGE_READWRITE);
                if (allocAddr != null)
                {
                    try
                    {
                        var fnLoadLibraryWAddr = (remoteKernel32Address + (nint)fnLoadLibraryW);
                        var addr = (char*)allocAddr;
                        foreach (var dllPath in dllPaths)
                        {
                            var updatedDllPath = UpdateDllPathIfNeeded(dllPath, isWow64);
                            fixed (void* dllPathPtr = updatedDllPath)
                            {
                                // VirtualAllocEx initializes memory to 0 so we don't need to write the null terminator
                                if (!PInvokeWin32.WriteProcessMemory(remoteProcessHandle, allocAddr, dllPathPtr,
                                        (nuint)(dllPath.Length * sizeof(char)), null))
                                {
                                    throw new Win32Exception();
                                }
                            }

                            status = queueApcThreadFunc(remoteThreadHandle.DangerousGetHandle(),
                                        fnLoadLibraryWAddr, (nint)addr, nint.Zero, nint.Zero);
                            if (status != 0)
                            {
                                throw new Win32Exception((int)PInvokeWin32.RtlNtStatusToDosError(new NTSTATUS(status)));
                            }

                            addr += dllPath.Length + 1;
                        }

                        // APC is the first thing the new thread executes when resumed
                        if (PInvokeWin32.ResumeThread(remoteThreadHandle) < 0)
                        {
                            throw new Win32Exception();
                        }

                        if (PInvokeWin32.WaitForSingleObject(remoteThreadHandle, 5000) is var err && err == WAIT_EVENT.WAIT_TIMEOUT)
                        {
                            throw new Win32Exception((int)WIN32_ERROR.ERROR_TIMEOUT);
                        }
                        else if (err == WAIT_EVENT.WAIT_FAILED)
                        {
                            throw new Win32Exception();
                        }
                    }
                    finally
                    {
                        PInvokeWin32.VirtualFreeEx(remoteProcessHandle, allocAddr, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE);
                    }
                }
                else
                {
                    throw new Win32Exception();
                }
            }
        }
        finally
        {
            remoteThreadHandle.Dispose();
        }

        // Helper functions

        static bool IsWow64(SafeHandle remoteProcessHandle) =>
            Environment.Is64BitProcess && PInvokeWin32.IsWow64Process(remoteProcessHandle, out var f) && f;

        static string UpdateDllPathIfNeeded(string dllPath, bool isWow64)
        {
            if (isWow64 && dllPath.EndsWith("64.dll", StringComparison.OrdinalIgnoreCase))
            {
                var newDllPath = string.Concat(dllPath.AsSpan(0, dllPath.Length - 6), "32.dll");
                return (Path.Exists(newDllPath) && Is32BitModule(newDllPath)) ? newDllPath : dllPath;
            }
            return dllPath;

            static bool Is32BitModule(string modulePath)
            {
                using var pereader = new PEReader(File.OpenRead(modulePath));

                return pereader.PEHeaders.PEHeader!.Magic == PEMagic.PE32;
            }
        }

        static uint GetModuleExportOffset(string modulePath, string procedureName)
        {
            using var pereader = new PEReader(File.OpenRead(modulePath));

            var exportsDirEntry = pereader.PEHeaders.PEHeader!.ExportTableDirectory;

            unsafe
            {
                var exportsDir = (IMAGE_EXPORT_DIRECTORY*)pereader.GetSectionData(exportsDirEntry.RelativeVirtualAddress).Pointer;

                var functionNamesRvas = new Span<uint>(pereader.GetSectionData((int)exportsDir->AddressOfNames).Pointer,
                                                        (int)exportsDir->NumberOfNames);
                var functionNamesOrdinals = new Span<ushort>(pereader.GetSectionData((int)exportsDir->AddressOfNameOrdinals).Pointer,
                                                                (int)exportsDir->NumberOfNames);
                var addressOfFunctions = pereader.GetSectionData((int)exportsDir->AddressOfFunctions).Pointer;

                for (int i = 0; i < functionNamesRvas.Length; i++)
                {
                    var name = Marshal.PtrToStringAnsi((nint)pereader.GetSectionData((int)functionNamesRvas[i]).Pointer);
                    var index = functionNamesOrdinals[i];

                    if (name == procedureName)
                    {
                        return *(uint*)(addressOfFunctions + index * sizeof(uint));
                    }
                }
            }

            return 0;
        }

        static unsafe nint GetModuleHandle(SafeHandle processHandle, bool isWow64, string moduleName)
        {
            const uint MaxModulesNumber = 256;

            var moduleHandles = stackalloc HMODULE[(int)MaxModulesNumber];
            uint cb = MaxModulesNumber * (uint)Marshal.SizeOf<HMODULE>();
            uint cbNeeded = 0;

            var processRawHandle = (HANDLE)processHandle.DangerousGetHandle();
            PInvokeWin32.EnumProcessModulesEx(processRawHandle, moduleHandles, cb, &cbNeeded,
                isWow64 ? ENUM_PROCESS_MODULES_EX_FLAGS.LIST_MODULES_32BIT : ENUM_PROCESS_MODULES_EX_FLAGS.LIST_MODULES_64BIT);

            if (cb >= cbNeeded)
            {
                moduleName = Path.DirectorySeparatorChar + moduleName.ToUpper();
                var nameBuffer = stackalloc char[(int)PInvokeWin32.MAX_PATH];
                foreach (var iterModuleHandle in new Span<HMODULE>(moduleHandles, (int)(cbNeeded / Marshal.SizeOf<HMODULE>())))
                {
                    if (PInvokeWin32.GetModuleFileNameEx(processRawHandle, iterModuleHandle, nameBuffer,
                            PInvokeWin32.MAX_PATH) is var iterModuleNameLength && iterModuleNameLength > moduleName.Length)
                    {
                        var iterModuleNameSpan = new Span<char>(nameBuffer, (int)iterModuleNameLength);
                        if (IsTheRightModule(iterModuleNameSpan))
                        {
                            return iterModuleHandle;
                        }
                    }
                }
            }

            return nint.Zero;

            bool IsTheRightModule(ReadOnlySpan<char> m)
            {
                var moduleNameSpan = moduleName.AsSpan();
                for (int i = 0; i < moduleNameSpan.Length; i++)
                {
                    if (char.ToUpper(m[i + m.Length - moduleNameSpan.Length]) != moduleNameSpan[i])
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }

    delegate int QueueApcThread(nint ThreadHandle, nint ApcRoutine, nint ApcArgument1, nint ApcArgument2, nint ApcArgument3);
}

/// <summary>
/// NT function definitions are modified (or not) versions
/// from https://github.com/googleprojectzero/sandbox-attacksurface-analysis-tools
/// 
/// //  Copyright 2019 Google Inc. All Rights Reserved.
///
///  Licensed under the Apache License, Version 2.0 (the "License");
///  you may not use this file except in compliance with the License.
///  You may obtain a copy of the License at
///
///  http://www.apache.org/licenses/LICENSE-2.0
///
///  Unless required by applicable law or agreed to in writing, software
///  distributed under the License is distributed on an "AS IS" BASIS,
///  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
///  See the License for the specific language governing permissions and
///  limitations under the License.
/// </summary>
internal static partial class Imports
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CLIENT_ID
    {
        public IntPtr UniqueProcess;
        public IntPtr UniqueThread;
    }

    [LibraryImport("ntdll.dll")]
    internal static partial int RtlCreateUserThread(
        nint ProcessHandle,
        nint ThreadSecurityDescriptor,
        [MarshalAs(UnmanagedType.Bool)]
        bool CreateSuspended,
        uint ZeroBits,
        nuint MaximumStackSize,
        nuint CommittedStackSize,
        nint StartAddress,
        nint Parameter,
        out SafeFileHandle ThreadHandle,
        out CLIENT_ID ClientId
    );

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueueApcThread(
         nint ThreadHandle,
         nint ApcRoutine,
         nint ApcArgument1,
         nint ApcArgument2,
         nint ApcArgument3
    );

    [LibraryImport("ntdll.dll")]
    internal static partial int RtlQueueApcWow64Thread(
        nint ThreadHandle,
        nint ApcRoutine,
        nint ApcArgument1,
        nint ApcArgument2,
        nint ApcArgument3
    );
}
