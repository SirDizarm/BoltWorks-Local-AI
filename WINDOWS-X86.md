# BoltWorks Local AI on 32-bit Windows

This experimental 0.6.19 package targets Windows 10 x86 with .NET Framework 4.8 and an SSE2-capable processor. The app, CLI, Mini REA, and bundled llama.cpp executables are 32-bit. The runtime uses CPU inference and statically links its compiler runtime libraries.

1. Copy the complete x86 ZIP and a compatible GGUF model to your USB drive.
2. Extract the ZIP into its own folder on the destination PC. Keep `runtime` beside the executables.
3. Open `BoltWorks-Local-AI.exe`, assign the GGUF to **Chat**, and lock the slot.
4. For a first run, select **Low RAM** in **Model memory** and use a context of 1024 with a short reply limit. Start with the 135M model linked below. It is suitable for a small inference test and basic text generation.

The x86 package includes CPU text and compatible vision inference, GGUF tools, and the Mini REA static inspection tools. The separate image generation engine, CUDA add-on, and managed .NET decompiler backend are absent from this package.

The 32-bit process address space limits which models fit. Start with a small model and keep the context short. The vision model and projector take additional memory. CPU speed and available RAM vary by machine.

## Tested models and platforms

- [SmolLM2-135M-Instruct Q4_K_M GGUF](https://huggingface.co/bartowski/SmolLM2-135M-Instruct-GGUF) generated a short reply. The file is about 101 MiB and is supplied separately from the ZIP.
- Qwen2.5-0.5B-Instruct Q4_K_M answered a short arithmetic prompt. SmolVLM-500M-Instruct Q8_0 with its matching projector correctly described a test image.
- These runs used x86 processes on 64-bit Windows 11. A native 32-bit Windows installation and older Windows versions still need testing. Windows XP and earlier are outside this build's target.

## Rebuild

Run `build.ps1 -Platform x86` to compile the Windows app, CLI, and Mini REA. The default output is `dist\BoltWorks-Local-AI-x86`. Build the native runtime with `legacy\build-ai-runtime-x86.ps1`, passing a fresh [llama.cpp b11669 source tree](https://github.com/ggml-org/llama.cpp/releases/tag/b11669), the `bin` folder from [w64devkit x86 2.9.1](https://github.com/skeeto/w64devkit/releases/tag/v2.9.1), and a build directory. The script adjusts the source's lazy-read threshold and build target label for x86. Copy `llama-server.exe` and `llama-gguf-split.exe` from the build's `bin` folder to the app's `runtime` folder.
