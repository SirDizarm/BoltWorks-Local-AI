# Release audit for 0.6.19

This is a feature and release audit. Windows results apply to the machines and features listed here.

## Test machines

| Machine | OS and RAM | What was tested |
| --- | --- | --- |
| Laptop in the CPU inference report | Windows version unrecorded, 7.65 GiB usable RAM | The computer check measured 4.49 GiB available at the start of the run. |
| Laptop in the router and image tests | Exact hardware and Windows version unverified | The saved router report records a management page read. The saved PNG shows the red tint. |
| Development desktop | Windows 11 x64, 31.80 GiB usable | Main app development checks, image reproduction, and the staged 3B Code Lab test in a 32-bit process. |
| Legacy laptop | Windows XP, 512 MB installed | Mini REA inspection and an offline 135M model reply completed. A later 3B coding attempt was stopped before it produced a draft. |

The project author identifies the Fujitsu Lifebook U7410 (Core i5-10210U, 8 GB RAM) as a Windows 11 machine. The saved laptop results lack enough machine details to assign them to that Fujitsu or to identify the Windows 10 laptop.

## Feature checks

| Claim | Evidence and scope |
| --- | --- |
| The source builds the app, CLI, and Mini REA | All three executables compiled from the staged public source on Windows 11 x64. The CLI reported 0.6.19 and returned hardware information. |
| Local inference works | The freshly compiled CLI ran a small Qwen3 0.6B GGUF through the existing CPU runtime and returned the requested number. The laptop computer check ran Qwen3 0.6B Q8_0 in CPU Low RAM mode with a 2048-token context. Its runtime peaked at 905 MiB resident memory. |
| Picture generation can produce heavily red images | The laptop PNG and the desktop CPU reproduction have identical SHA-256 hashes. GPU diffusion and full-frame decoding still showed a strong red cast with this SD 1.5 Q4_0 checkpoint. The approximately ten-minute laptop run was reported by the project author. |
| Mini REA does static inspection | Synthetic checks passed for seven firmware signatures, block boundaries, CRC and range checks, and carving. PE and text checks passed for imports, exports, resources, embedded strings, and a 300 MiB input. The optional ILSpy backend recovered C# from a generated .NET test program. The edited program rebuilt and ran, and separate x86/x64 resource replacement tests passed. |
| Ethernet supports investigating old routers | The local test confirmed address validation, TCP acceptance/refusal, and inventory. A saved laptop report shows an HTTP Basic read of a Netgear WNR3500L v2 management page. Prior browser and Telnet checks used local fixtures. Router conversion and web server deployment are unverified. |
| Legacy Windows support is experimental | The separate XP inspector and 135M runner completed on one 512 MB XP laptop. Newer XP builds and the 3B code-generation workflow have different validation results. [XP demo and test guide](legacy/XP-DEMO.md) |

## GitHub source package

The 0.6.19 source package contains the app, native XP demos, build scripts, feature guides, credits, and the full MIT license. The [runtime setup guide](BUILDING.md) explains how to obtain external dependencies.

The compiled Windows bundle needs a separate redistribution review for its exact dependencies. Keep the project license and applicable upstream notices with any binary release. [Third-party software and models](THIRD-PARTY.md)

The repository update distributes project source, dependency references and build scripts. This review covers the source tree. Runtime ZIPs, model weights, the older combined installer and XP demo binaries require their own distribution checks.
