# BoltWorks Local AI

BoltWorks Local AI is a Windows desktop app and command-line tool for running local AI models, working on code, and inspecting software and devices. Created by **Daniel Rydin**. This source release documents version **0.6.19**.

The app runs inference on your computer. A compatible model and its runtime must be in place before you go offline. CPU is the default for text and vision. Optional NVIDIA CUDA accelerates those workloads, and image generation has separate CPU and Vulkan options.

## What it does

| Feature | What you can do |
| --- | --- |
| Chat and image understanding | Chat with a local GGUF model and ask a compatible vision model to describe an attached image. Assign and lock separate models for chat, vision, and picture creation. |
| Model tools | Compare local chat models on this computer for speed, memory use, and simple instruction following. Inspect or split GGUF files and use Low RAM or experimental disk-backed CPU modes. |
| Coding workspace | Let a local model read, search, create, and edit files in a selected project. Review commands before they run and recover edits from backups. |
| Picture creation | Generate images from text or a starting image with a compatible Stable Diffusion checkpoint. Save PNGs and reuse results. This feature is experimental and has the color issue below. |
| Mini REA and legacy import | Inspect firmware signatures, executable headers, PE imports and exports, Windows resources, and embedded text, URLs, and paths. Export resources or byte ranges. The optional ILSpy backend decompiles managed .NET assemblies to C#, and resource replacement writes to a new copy. |
| Ethernet inspection | Read wired adapter and ARP information, check selected ports on a local device, and inspect accessible router pages with supplied credentials. The tool can collect rendered page text via Edge and list directories on an already accessible Telnet shell. Save observations for local AI analysis. |
| CLI and reusable prompts | Call the local CLI from your own scripts, tools, and plugins. Use JSON output where available and save reusable themes and skills. |

## Current limits and compatibility

- **Red tint in generated images.** The tested Stable Diffusion 1.5 Q4_0 checkpoint produced a heavy red cast. The laptop run took about ten minutes on CPU, according to the tester. The Windows 11 desktop CPU reproduced the PNG byte for byte. A GPU run and a full-frame decode still showed the tint. The cause remains open.
- **Windows testing.** A saved laptop report confirms local CPU inference with 7.65 GiB usable RAM. The report omits the laptop model and Windows version. Development checks ran on a Windows 11 x64 desktop with 31.80 GiB usable RAM. See the [hardware and test results](RELEASE-AUDIT.md#test-machines). The main app requires .NET Framework 4.8, and an experimental Windows 10 x86 build is available.
- **Windows XP demos.** Separate native tools cover binary inspection, short offline model answers, and experimental C code generation. An early inspector and 135M model completed on a 512 MB XP laptop. A later 3B coding attempt on that laptop was stopped before it produced a draft. See the [XP demo and test guide](legacy/XP-DEMO.md) for builds and results.
- **Mini REA is early-stage.** It is a starting point for static inspection that contributors can extend with format parsers, additional analysis providers, and tests. Signature matches can be false positives. Native machine-code decompilation and general firmware extraction are outside its current capabilities.

## Why the Ethernet tools are here

The original goal was to investigate whether older routers could be repurposed as small web servers for local pages. Reading the existing management page and checking available services are initial steps. A test on a Netgear WNR3500L v2 reached its management page using HTTP Basic authentication. A successful connection establishes access to that service. Router conversion, flashing, and deploying a web server remain future work.

## Build and use

Build the desktop app, CLI, and standalone Mini REA on Windows x64:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The executables are written to `dist\BoltWorks-Local-AI`. Follow the [runtime setup and build guide](BUILDING.md), then the [quick start](QUICKSTART.md). This is a source package. Model weights, external editors, and runtime binaries are obtained separately.

- [CLI](CLI.md), [model slots](MODEL-SLOTS.md), [computer check](COMPUTER-CHECK.md), [memory and GGUF](MEMORY-AND-LEGACY.md), [CUDA](CUDA-OPTIONAL.md)
- [Coding workspace](CODING.md), [external integrations and optional editor add-on](INTEGRATING.md)
- [Mini REA](MINI-REA.md), [binary inspection](EXE-DLL-INSPECTION.md), [.NET decompilation](DECOMPILE-AND-EDIT.md), [Ethernet inspection](ETHERNET.md), [Windows XP demos](legacy/XP-DEMO.md)
- [Release audit](RELEASE-AUDIT.md), [release history](CHANGELOG.md), [contributing](CONTRIBUTING.md)

## Credits and license

**Daniel Rydin** created BoltWorks Local AI, previously known as Llama Lite. Thanks to the Llama Lite contributors and the upstream projects listed in [Third-party software and models](THIRD-PARTY.md).

Project source is available under the full [MIT License](LICENSE.txt). You may use, modify, and redistribute it, including commercially. Keep the copyright and permission notices in copies or substantial portions of the software.

