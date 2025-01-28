using System.Diagnostics;
using withdll;

var parsedArgs = ParseArgs(["h", "help", "debug", "newconsole", "wait"], args);
if (!parsedArgs.TryGetValue("", out var freeArgs))
{
    freeArgs = [];
}

if (parsedArgs.ContainsKey("h") || parsedArgs.ContainsKey("help") || freeArgs.Count == 0)
{
    ShowHelp();
    return 1;
}

var dllPaths = (parsedArgs.TryGetValue("d", out var d1) ? d1 : []).Union(
                parsedArgs.TryGetValue("dll", out var d2) ? d2 : []).ToList();
try
{
    if (freeArgs.Count == 1 && int.TryParse(freeArgs[0], out var pid))
    {
        DllInjection.InjectDllsIntoRunningProcess(pid, dllPaths);
    }
    else
    {
        DllInjection.StartProcessWithDlls(
            new(freeArgs, parsedArgs.ContainsKey("debug"), parsedArgs.ContainsKey("newconsole"), parsedArgs.ContainsKey("wait")),
            dllPaths);
    }
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex}");
    return 1;
}

static Dictionary<string, List<string>> ParseArgs(string[] flagNames, string[] rawArgs)
{
    bool IsFlag(string v) => Array.IndexOf(flagNames, v) >= 0;

    var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    var lastOption = "";
    var firstFreeArgPassed = false;

    var argsToProcess = new Stack<string>(rawArgs.Reverse());

    while (argsToProcess.Count > 0)
    {
        var argToProcess = argsToProcess.Pop();

        if (!firstFreeArgPassed && argToProcess.StartsWith('-'))
        {
            if (argToProcess.Split('=', 2) is var splitArgs && splitArgs.Length > 1)
            {
                argsToProcess.Push(splitArgs[1]);
            }

            if (splitArgs[0] == "--")
            {
                lastOption = "";
                firstFreeArgPassed = true;
            }
            else if (splitArgs[0].TrimStart('-') is var option && IsFlag(option))
            {
                Debug.Assert(lastOption == "");
                result[option] = [];
            }
            else
            {
                Debug.Assert(lastOption == "");
                lastOption = option;
            }
        }
        else
        {
            // the logic is the same for options (lastOption) and free args
            if (result.TryGetValue(lastOption, out var values))
            {
                values.Add(argToProcess);
            }
            else
            {
                result[lastOption] = [argToProcess];
            }
            firstFreeArgPassed = lastOption == "";
            lastOption = "";
        }
    }
    return result;
}


static void ShowHelp()
{
    Console.WriteLine($"withdll - injects DLL(s) into a process");
    Console.WriteLine("""
Copyright (C) 2023 Sebastian Solnica (https://wtrace.net)

withdll [options] <new process command line | process ID>

Options:
  -h, -help         Show this help screen
  -d, --dll <path[@<ordinal|name>]>  A DLL to inject into the target process (can be set multiple times)
  -debug            Start process with a debugger (useful when using IFEO) [new processes only]
  -wait             Wait for the started process to finish [new processes only]
  -newconsole       Launch process with a new command window [new processes only]

Examples:

   withdll -d c:\temp\mydll1.dll -d c:\temp\mydll2.dll winver.exe
   withdll -d c:\temp\mydll.dll@2 1234
   withdll -d c:\temp\mydll.dll@ExportedFunc 1234
""");
}
