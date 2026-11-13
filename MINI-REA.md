# BoltWorks Mini REA 0.5

For low-memory 32-bit computers, see the separate [Mini REA XP edition](legacy/MINIREA-XP.md). It runs native static inspection without a model or .NET.

A small, early-stage inspection tool for firmware and executable files. The core scanner runs without Node or npm and requires .NET Framework 4.x. The optional .NET decompiler runs as a separate local process. File analysis is fully offline. Ethernet inventory reads local Windows information; the explicit probe command makes TCP connections to one chosen local wired address. See [ETHERNET.md](ETHERNET.md) for the new network commands and panel.

This is a first analysis component, not a replacement for REA, Binwalk or Ghidra. It contains no model weights. Tested on this Windows 11 x64 host; XP compatibility is untested and Windows 95/98 are not supported by this managed tool. The separate native USB collector remains available for exporting files from legacy machines.

## Decompile and replace content

Version 0.4 adds offline .NET-to-C# decompilation through the bundled ILSpy backend, and Windows resource replacement in new executable copies. See [DECOMPILE-AND-EDIT.md](DECOMPILE-AND-EDIT.md). The full backend adds about 83 MB on disk; keep its complete decompiler folder with the frontend. Native machine-code decompilation is not included. The package includes a small managed console demo and native DLL fixtures you can use to try these features.

## EXE / DLL inspection

Version 0.3 adds pe FILE and resource FILE INDEX NEW_OUTPUT commands, a desktop inspector, and pe-details.txt in imported analysis projects. See [EXE-DLL-INSPECTION.md](EXE-DLL-INSPECTION.md). The examples folder in this release contains small native x86/x64 DLLs and their assembly source, built as inspection test data. They are not installed or executed by the inspector.

## Use in BoltWorks Local AI

Open **Work on code > Inspect binary / firmware**. Choose a firmware image, binary or exported copy, then a parent folder for a new inspection project. No physical device needs to be connected. The project includes original.bin, inspection.txt, strings.txt, hex.txt and **firmware.txt**. Lock a local Chat model and run the prepared task to discuss the evidence. All input contents are untrusted data. The model's interpretation can be wrong; offsets and original bytes remain available for verification.

The previous Windows executable-header reports remain available. Mini REA additionally scans the entire input (maximum 256 MiB) for ELF, little-endian SquashFS, U-Boot legacy uImage, gzip, XZ, UBI erase-count and ZIP local-header signatures. It reports at most 256 findings and explicitly marks an early stop. Signature recognition is deliberately limited and can yield false positives or miss unsupported/packed/encrypted content.

ELF reports include declared word size, endianness and processor family. SquashFS v4 candidates receive basic superblock and range checks. U-Boot uImage candidates receive header CRC and range checks; payload CRC is not checked. Other signatures are explicitly marked as shallow candidates. A declared extent does not establish that a filesystem is valid, bootable, flashable or compatible with a particular device. No disassembly or decompilation is performed.

## Standalone commands

Run from a terminal on the offline laptop:

```powershell
.\mini-rea.exe scan 'C:\Firmware\backup.bin'
.\mini-rea.exe scan 'C:\Firmware\backup.bin' > 'C:\Firmware\report.txt'
.\mini-rea.exe carve 'C:\Firmware\backup.bin' 65536 1048576 'C:\Firmware\section.bin'
```

The last command is an illustrative range, not advice for any real image. Offset and length are decimal bytes. Review actual file offsets and bounds first. Carving copies exactly that range into a NEW file; it does not decompress or extract filesystem contents. Existing files are never overwritten. Cancellation or an I/O failure can leave partial output; retry with a new filename. Source files are opened read-only and are never executed. Use Ctrl+C to cancel.

The BoltWorks Local AI CLI additionally offers machine-readable output:

```powershell
.\BoltWorks-Local-AI-CLI.exe firmware-scan --file 'C:\Firmware\backup.bin' --json
```

Scanning uses a 64 KiB block with header lookahead and a separate streaming SHA-256 pass. It does not load the whole image into RAM. Imported copies and static reports can be read by the existing coding agent; execution of a carve command still uses the existing explicit command-approval flow. This is file/CLI integration, not a new MCP server.

## Repurposing home devices

This tool can help locate likely firmware components and identify embedded executable architectures. It does not obtain firmware from a device, unlock a bootloader, bypass firmware signatures, flash devices, start a server or add missing hardware capabilities. The next investigation requires the exact model, hardware revision, an acquired firmware image/backup and the device's recovery procedure. Routers may contain Linux and usable local services; USB modems may have much more restricted application environments. A USB interface alone does not establish that a device can host your software.

A service bound to localhost is reachable only on the device itself. A service intended for other machines on an isolated home network must listen on the appropriate LAN interface and use that device's LAN address. No internet connection is required for such a local network. Firmware analysis can also be performed entirely from files without connecting the device to the analysis computer.

Next useful extensions are bounded filesystem extraction and local disassembler adapters, selected after identifying the first real device/image. They are not implemented in 0.2.

## Verification and format references

Tests exercised seven signatures, a header across a block boundary, big-endian MIPS identification, valid/invalid uImage header CRC, impossible SquashFS ranges, exact carving, hashes, no-overwrite protection, rejected out-of-range copies, truncation, finding caps and app import integration. Tests use synthetic fixtures; no real router/modem has been inspected or modified in this session. Existing coding regression tests also pass.

Format references: [Linux ELF declarations](https://github.com/torvalds/linux/blob/master/include/uapi/linux/elf.h), [Linux SquashFS declarations](https://github.com/torvalds/linux/blob/master/fs/squashfs/squashfs_fs.h), [U-Boot image headers](https://github.com/u-boot/u-boot/blob/master/include/image.h). Broader context: [OpenWrt flash layout](https://openwrt.org/docs/techref/flash.layout), [Binwalk firmware analysis](https://github.com/ReFirmLabs/binwalk). No code from REA or these projects is bundled into Mini REA.
