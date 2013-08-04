sosloader
=========

Automatically loads SOS based on information present in the dump file. For most CLR versions after 4.0, the SOS and MSCORDACWKS binaries are present on the Microsoft public symbol server.

Recent versions of WinDbg can download the correct SOS and DAC when you run the ```!analyze -v``` command. However, this doesn't work with all SOS versions that are present on the symbol server. sosloader is more reliable, and will launch WinDbg automatically for you, preconfigured with the right SOS and DAC.

To use sosloader, you need to make sure that dbghelp.dll and symsrv.dll are accessible to the application when it runs. The project is configured to copy these files from the default Windows SDK installation directory -- you might need to modify this to suit your needs.

Usage:

```
sosloader.exe download mydump.dmp
sosloader.exe launch mydump.dmp
```

Note: The ```launch``` switch requires that windbg.exe is accessible.