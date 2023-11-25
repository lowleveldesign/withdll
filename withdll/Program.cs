using System.Diagnostics;
using withdll;

public static class Program
{
    public static int Main(string[] args)
    {
        var parsedArgs = ParseArgs(["h", "help", "debug"], args);

        int pid = 0;
        if (parsedArgs.ContainsKey("") && parsedArgs[""].Count == 1)
        {
            int.TryParse(parsedArgs[""][0], out pid);
        }

        if (parsedArgs.ContainsKey("h") || parsedArgs.ContainsKey("help") ||
            !parsedArgs.ContainsKey("") || parsedArgs[""].Count == 0)
        {
            ShowHelp();
            return 1;
        }

        var dllpaths = (parsedArgs.TryGetValue("d", out var d1) ? d1 : new List<string>(0)).Union(
                        parsedArgs.TryGetValue("dll", out var d2) ? d2 : new List<string>(0))
                        .Select(Path.GetFullPath).Distinct().ToList();
        if (dllpaths.FirstOrDefault(p => !Path.Exists(p)) is {} p)
        {
            Console.WriteLine($"Error: DLL file '{p}' does not exist");
            return 1;
        }

        try
        {
            if (pid > 0)
            {
                DllInjection.InjectDllsIntoRunningProcess(pid, dllpaths);
            }
            else
            {
                DllInjection.StartProcessWithDlls(parsedArgs[""], parsedArgs.ContainsKey("debug"), dllpaths);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return 1;
        }
    }

    private static Dictionary<string, List<string>> ParseArgs(string[] flagNames, string[] rawArgs)
    {
        var args = rawArgs.SelectMany(arg => arg.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
        bool IsFlag(string v) => Array.IndexOf(flagNames, v) >= 0;

        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var lastOption = "";
        var firstFreeArgPassed = false;

        foreach (var arg in args)
        {
            if (!firstFreeArgPassed && arg.StartsWith("-", StringComparison.Ordinal))
            {
                var option = arg.TrimStart('-');
                if (IsFlag(option))
                {
                    Debug.Assert(lastOption == "");
                    result[option] = new();
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
                    values.Add(arg);
                }
                else
                {
                    result[lastOption] = new List<string> { arg };
                }
                firstFreeArgPassed = lastOption == "";
                lastOption = "";
            }
        }
        return result;
    }


    private static void ShowHelp()
    {
        Console.WriteLine($"withdll - injects DLL(s) into a process");
        Console.WriteLine("""
Copyright (C) 2023 Sebastian Solnica (https://wtrace.net)

withdll [options] <new process command line | process ID>

Options:
  -h, -help         Show this help screen
  -d, --dll <path>  A DLL to inject into the target process (can be set multiple times)

Examples:

   withdll -d c:\temp\mydll1.dll -d c:\temp\mydll2.dll notepad.exe test.txt
   withdll -d c:\temp\mydll.dll 1234
""");
    }
}

