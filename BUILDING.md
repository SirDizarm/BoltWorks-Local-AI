# Build and runtime setup

For the experimental 32-bit Windows package and its CPU runtime build instructions, see [WINDOWS-X86.md](WINDOWS-X86.md).

Run `build.ps1` on Windows x64 with .NET Framework 4.8. The script uses the Windows .NET Framework C# compiler and builds `BoltWorks-Local-AI.exe`, `BoltWorks-Local-AI-CLI.exe`, and `mini-rea.exe`. Its optional `-OutputDirectory` parameter sets the output folder. The main build requires no model weights or external editor.

Set up only the dependencies for the features you use. Place runtime folders beside the executables and keep each native runtime's DLLs together.

| Feature | Setup |
| --- | --- |
| Chat, vision, coding assistant and model tools | Extract the Windows x64 CPU build from [llama.cpp b11669](https://github.com/ggml-org/llama.cpp/releases/tag/b11669) into `runtime`. The tested build reports commit `620d2225c`. Check for `llama-server.exe` and `llama-gguf-split.exe`. Add a compatible GGUF model and the matching projector for vision. |
| Optional text/vision CUDA | Follow [CUDA-OPTIONAL.md](CUDA-OPTIONAL.md) for the separate `runtime-cuda` directory and driver requirements. |
| Picture generation | Put `sd-cli.exe` and its matching libraries in `image-runtime`. The tested [stable-diffusion.cpp revision](https://github.com/leejet/stable-diffusion.cpp/commit/be48649) is `be48649`. Use its [build instructions](https://github.com/leejet/stable-diffusion.cpp/tree/be48649), choosing CPU or Vulkan support as needed. Check that `sd-cli.exe --help` lists `--prompt-file`, `--negative-prompt-file`, `--backend`, and `--batched-cfg`. Supply your own compatible SD 1.x or SDXL checkpoint. |
| Native Windows runtime libraries | If needed, install Microsoft's [x64 Visual C++ Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist) for the native engines. |
| .NET decompilation | Run `integration\decompiler\build.ps1 -Restore` with the .NET 10 SDK to publish the optional backend. Keep the entire backend output in `decompiler` beside the app. See the [.NET decompilation guide](DECOMPILE-AND-EDIT.md). |
| Router page capture | Install Microsoft Edge for the separate JavaScript browser. Basic HTTP and TCP inspection work through the app without Edge. |
| Windows XP demos | Follow the [XP demo guide](legacy/XP-DEMO.md) for the separate Mini REA and Code Lab builds. |

The first setup can be done on a connected PC. Copy the built app, selected runtimes, compatible models, and any optional editors to the offline machine. Run `BoltWorks-Local-AI-CLI.exe --help` to list the supported commands.
