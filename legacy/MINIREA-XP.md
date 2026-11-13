# BoltWorks Mini REA XP 0.1

A native x86 command-line inspector for older, low-memory PCs. It targets Windows XP on a Pentium-class or newer processor and uses only Windows system libraries. Inspection uses fixed 64 KiB blocks with a small lookahead. Reports and byte carving work without AI weights, .NET, or internet access.

Extract the entire ZIP to a folder. Double-click `DEMO.BAT` to have Mini REA inspect its own executable. Open Command Prompt in that folder to inspect your own files:

```bat
MINIREA.EXE inspect "C:\OLDAPP\GAME.EXE" "C:\OLDAPP\GAME-REPORT.TXT"
MINIREA.EXE pe "C:\OLDAPP\GAME.EXE"
MINIREA.EXE scan "C:\FILES\FIRMWARE.BIN"
MINIREA.EXE strings "C:\OLDAPP\GAME.EXE" "config"
MINIREA.EXE carve "C:\FILES\FIRMWARE.BIN" 4096 64 "C:\FILES\CHUNK.BIN"
```

`inspect` combines executable headers, firmware signature candidates, and a strings preview. Give it a new report path to save a plain-text report. `pe` shows MZ, NE, LE, LX, PE32, and PE32+ header information where available, including PE sections and imported DLL names. It reports export, resource, and CLR directory locations when present. `scan` finds seven firmware signatures with basic ELF, SquashFS, and uImage header details. `strings` finds printable ASCII and UTF-16LE with a case-insensitive ASCII filter and byte offsets. `carve` copies a decimal byte range to a new file. Inspecting and carving open the input read-only.

String previews and signature reports are bounded. A string query can match beyond the displayed preview and reports the actual match offset. Signature matches are candidates and need further verification. This first XP edition covers static file inspection. Decompilation, resource editing, network inspection, and model integration remain features of other builds. You can copy the report to a newer computer for AI analysis.

## Verification

The native executable was tested as a 32-bit process on Windows 11. Checks covered PE32 and PE32+ headers, imports, truncated headers, firmware and text crossing block boundaries, safe range copying, and refusal to overwrite reports or inputs. A 300 MiB string search reached its last 32 bytes while the sampled process working set stayed below 5 MiB on that host. Actual Windows XP and Safe Mode still need a test on the laptop.

Build from the repository with `legacy/build-minirea-xp.ps1` and the x86 w64devkit compiler. Pass `-ToolchainBin` if it is installed elsewhere. The build uses the Windows XP subsystem version and was checked for SSE/AVX instructions. [Win32 file seek API](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfilepointerex) · [PE format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format)
