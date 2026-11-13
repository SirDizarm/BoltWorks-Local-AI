# Optional NVIDIA CUDA acceleration

BoltWorks Local AI starts in CPU mode every time, even when this add-on is installed. CPU computers do not need this download. No Ollama, background service, CUDA Toolkit installer or driver installer is included or required by BoltWorks Local AI.

1. Extract the BoltWorks Local AI x64 ZIP.
2. Place the separately obtained CUDA runtime in `runtime-cuda` next to `BoltWorks-Local-AI.exe` and the CPU `runtime` folder. The earlier CUDA archive was named `BoltWorks-LlamaLite-CUDA-b11960-win-x64.zip`.
3. Start BoltWorks Local AI. Under **Your AI models → AI processing**, explicitly select **NVIDIA CUDA (optional add-on)**. Start with 16 GPU layers. Lower this if graphics memory is insufficient; choose CPU to return to the portable default.
4. Choose and lock model files as usual. The selection applies to chat and image reading. It does not change the separate picture-generation backend.

CUDA needs a supported NVIDIA GPU and an already installed compatible NVIDIA driver. This package does not install drivers. The vision encoder stays on CPU to leave more graphics memory for the language model. Weights and working memory still use system RAM; offloading some layers does not remove the model's memory requirements.

The CUDA choice is deliberately not saved: a new app launch always selects CPU. Changing processing mode releases the loaded engine; the next task loads the selected engine. Closing BoltWorks Local AI stops its owned model processes. The CLI also defaults to CPU and does not read the GUI's selection:

```
BoltWorks-Local-AI-CLI.exe chat --model YOUR_MODEL.gguf --backend cuda --gpu-layers 16 --prompt "Hello"
```

The optional native package is large because NVIDIA's redistributable math libraries and the CUDA kernels are large. They remain separate from the CPU release. It contains only the local inference runtime and dependencies, not the Ollama application or service.

Build: official ggml-org/llama.cpp b11960, Windows x64 CUDA 12.4. Sources:

- https://github.com/ggml-org/llama.cpp/releases/tag/b11960
- llama CUDA archive SHA-256: 8D9D819261DE10CC2F03921B8B79C779C5C458E21EDB60C170EE30A7D462D684
- cudart archive SHA-256: 8C79A9B226DE4B3CACFD1F83D24F962D0773BE79F1E7B75C6AF4DED7E32AE1D6
- llama.cpp is MIT licensed; see the included license. NVIDIA components retain NVIDIA's terms: https://docs.nvidia.com/cuda/eula/index.html . Microsoft runtime components retain their included license.

No paid API or account is used. AI model weights are separate downloads.
