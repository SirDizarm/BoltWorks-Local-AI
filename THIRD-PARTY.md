# Third-party software and models

BoltWorks Local AI's own source is licensed under MIT. Each dependency is distributed under its own terms.

- **llama.cpp and ggml** power local text and vision inference and GGUF utilities. [llama.cpp MIT license](https://github.com/ggml-org/llama.cpp/blob/master/LICENSE)
- **TinyCC 0.9.27** builds the sample program in the separate XP Code Lab demo. The demo archive includes the compiler's license, upstream source, modified linker source, and build instructions.
- **stable-diffusion.cpp** powers local picture generation. A compiled runtime can also include ggml, image and video libraries, and other dependencies. [stable-diffusion.cpp MIT license](https://github.com/leejet/stable-diffusion.cpp/blob/master/LICENSE)
- **ILSpy / ICSharpCode.Decompiler** powers optional managed .NET decompilation. Its self-contained build also includes .NET runtime components. [ILSpy license and notices](https://github.com/icsharpcode/ILSpy/blob/master/doc/README.md)
- **Microsoft Visual C++ runtime, Edge/WebView2, and NVIDIA CUDA libraries** have their own terms. Verify redistribution for the exact files before publishing a binary bundle. [Microsoft C++ redistribution](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files) · [NVIDIA CUDA terms](https://docs.nvidia.com/cuda/eula/index.html)
- **Model weights and projectors** are supplied by the user. Check the license for the exact file and its base model before redistribution. A GGUF conversion does not change the base model's terms. [SD 1.5 model license example](https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-v1-5/blob/main/README.md)

For a compiled release, keep the exact upstream licenses and notices for all files you ship. This source release includes the app source, build scripts, and documentation. It uses separate downloads for runtimes, model weights, and the editors.
