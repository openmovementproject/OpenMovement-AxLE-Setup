using System;
using System.Diagnostics;
using System.IO;

namespace OpenMovement.AxLE.Setup
{
    public static class RedirectedProcess
    {        
        public static int Execute(string executable, string args)
        {
            // Clearer error if the executable does not exist (although will fail even if would otherwise find on PATH)
            if (!File.Exists(executable))
            {
                throw new Exception("Executable not found: " + executable + " cwd=" + Directory.GetCurrentDirectory());
            }

            // Create the process structure
            ProcessStartInfo processInformation = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = args,
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            // Create process
            Process process = new Process
            {
                EnableRaisingEvents = true,
                StartInfo = processInformation
            };

            // Start process and show output
            Console.WriteLine("PROCESS: " + executable + " " + args);
            process.OutputDataReceived += (sender, data) => Console.WriteLine("[O] {0}", data.Data);
            process.ErrorDataReceived += (sender, data) => Console.WriteLine("[E] {0}", data.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Block
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
