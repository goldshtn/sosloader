using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace sosloader
{
    class DacLocator
    {
        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern bool SymInitialize(IntPtr hProcess, String symPath, bool fInvadeProcess);

        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern bool SymCleanup(IntPtr hProcess);

        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern bool SymFindFileInPath(IntPtr hProcess, String searchPath, String filename, uint id, uint two, uint three, uint flags, StringBuilder filePath, IntPtr callback, IntPtr context);

        private static void VerifyHr(int hr)
        {
            if (hr != 0)
                throw Marshal.GetExceptionForHR(hr);
        }

        /// <summary>
        /// Retrieves the debug support files (DAC, SOS, CLR) from the Microsoft symbol server
        /// and returns the path to the temporary directory in which they were placed. If the <paramref name="storageLocation"/>
        /// parameter is not null/empty, the files will be stored in that location.
        /// </summary>
        /// <param name="clrInfo">The CLR version for which to load the support files.</param>
        public static string GetDebugSupportFiles(ClrInfo clrInfo, DataTarget target, string storageLocation = null)
        {
            IntPtr processHandle = Process.GetCurrentProcess().Handle;
            if (!SymInitialize(processHandle, null, false))
            {
                Console.WriteLine("*** Error initializing dbghelp.dll symbol support");
                return null;
            }
            if (string.IsNullOrEmpty(storageLocation))
            {
                storageLocation = Path.Combine(Path.GetTempPath(), clrInfo.Version.ToString());
                if (!Directory.Exists(storageLocation))
                    Directory.CreateDirectory(storageLocation);
            }

            Console.WriteLine("CLR version: " + clrInfo.Version);
            string clrModuleName = clrInfo.Version.Major == 2 ? "mscorwks" : "clr";
            StringBuilder loadedClrFile = new StringBuilder(2048);
            if (!SymFindFileInPath(processHandle, null, clrModuleName + ".dll", clrInfo.DacInfo.TimeStamp,
                clrInfo.DacInfo.FileSize, 0, 0x2, loadedClrFile, IntPtr.Zero, IntPtr.Zero))
            {
                Console.WriteLine("*** Error retrieving CLR from symbol server");
            }
            else
            {
                File.Copy(loadedClrFile.ToString(), Path.Combine(storageLocation, clrModuleName + ".dll"), true);
            }

            string str = (IntPtr.Size == 4) ? "x86" : "amd64";
            VersionInfo clrVersion = clrInfo.Version;
            string sosFileName = string.Format("sos_{0}_{1}_{2}.{3}.{4}.{5:D2}.dll",
                str, target.Architecture, clrVersion.Major, clrVersion.Minor, clrVersion.Revision, clrVersion.Patch);
            StringBuilder loadedSOSFile = new StringBuilder(2048);
            if (!SymFindFileInPath(processHandle, null, sosFileName, clrInfo.DacInfo.TimeStamp,
                clrInfo.DacInfo.FileSize, 0, 0x2, loadedSOSFile, IntPtr.Zero, IntPtr.Zero))
            {
                Console.WriteLine("*** Error retrieving SOS from symbol server");
            }
            else
            {
                File.Copy(loadedSOSFile.ToString(), Path.Combine(storageLocation, "SOS.dll"), true);
            }

            StringBuilder loadedDacFile = new StringBuilder(2048);
            if (!SymFindFileInPath(processHandle, null, clrInfo.DacInfo.FileName, clrInfo.DacInfo.TimeStamp,
                clrInfo.DacInfo.FileSize, 0, 0x2 /*SSRVOPT_DWORD*/, loadedDacFile, IntPtr.Zero, IntPtr.Zero))
            {
                Console.WriteLine("*** Error retrieving DAC from symbol server");
            }
            else
            {
                File.Copy(loadedDacFile.ToString(), Path.Combine(storageLocation, "mscordacwks.dll"), true);
            }

            SymCleanup(processHandle);
            return storageLocation;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 2 || (args[0] != "download" && args[0] != "launch"))
            {
                Console.WriteLine("Usage: sosloader <download | launch> <dump file path>");
                return;
            }

            Console.WriteLine("\nPlease make sure that you have dbghelp.dll and symsrv.dll accessible\nto the application. Otherwise, we will not be able to retrieve files\nfrom the Microsoft symbol server.\n");

            string dumpFilePath = args[1];
            DataTarget target = DataTarget.LoadCrashDump(dumpFilePath);
            if (target.ClrVersions.Count == 0)
            {
                Console.WriteLine("This dump file does not have a CLR loaded in it.");
                return;
            }
            if (target.ClrVersions.Count > 1)
            {
                Console.WriteLine("This dump file has multiple CLR versions loaded in it.");
                return;
            }

            if (target.Architecture == Architecture.X86 && IntPtr.Size != 4)
            {
                Console.WriteLine("Please use the 32 bit version of sosloader to analyze this dump.");
                return;
            }
            if (target.Architecture == Architecture.Amd64 && IntPtr.Size != 8)
            {
                Console.WriteLine("Please use the 64 bit version of sosloader to analyze this dump.");
                return;
            }

            string dacLocation = target.ClrVersions[0].LocalMatchingDac;
            if (!String.IsNullOrEmpty(dacLocation))
            {
                //No symbol load needed, the files are available on the local machine
                Console.WriteLine("The debug support files are available on the local machine.");
                if (args[0] == "launch")
                {
                    Console.WriteLine("Launching windbg.exe with the provided dump file...");
                    string loadByCommand = ".loadby sos " + (target.ClrVersions[0].Version.Major == 4 ? "clr" : "mscorwks");
                    Process.Start("windbg.exe", String.Format("-z {0} -c \"{1}\"", dumpFilePath, loadByCommand));
                }
                return;
            }

            string debugSupportFilesLocation = DacLocator.GetDebugSupportFiles(target.ClrVersions[0], target);
            if (args[0] == "launch")
            {
                Console.WriteLine("Launching windbg.exe with the provided dump file (must be in path)...");
                string loadCommand = String.Format(".load {0}; .cordll -se -lp {1}",
                    Path.Combine(debugSupportFilesLocation, "sos"), debugSupportFilesLocation);
                Process.Start("windbg.exe", String.Format("-z {0} -c \"{1}\"", dumpFilePath, loadCommand));
            }
            else
            {
                Console.WriteLine("Debug support files are now available in " + debugSupportFilesLocation);
                Console.WriteLine("Use .load <location>\\sos to load SOS and .cordll -se -lp <location> to set up DAC");
            }
        }
    }
}
