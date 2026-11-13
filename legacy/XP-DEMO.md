# Windows XP demos

The XP demos are separate from the BoltWorks Local AI desktop app. They run offline on the CPU. Copy a compatible GGUF separately to use AI. The AI runner requires a 32-bit x86 Windows XP installation or a compatible Windows environment and a processor with SSE2.

| Demo | Test result |
| --- | --- |
| Mini REA XP AI 0.1 | Binary inspection and SmolLM2 135M report interpretation completed on one 512 MB XP laptop. The model omitted one DLL that the inspector correctly listed. |
| Mini REA XP AI 0.2 | The graphical window, binary search and model answers passed on Windows 11 in 32-bit mode. The window still needs an XP hardware test. |
| XP Code Lab 0.4 | The splitter and staged Qwen2.5-Coder 3B inference passed in 32-bit mode on a Windows 11 x64 desktop with 31.80 GiB usable RAM. The model process peaked at about 312 MiB resident memory. A 3B coding attempt on the 512 MB XP laptop was stopped before it produced a draft. Its completion time and failure cause are unknown. |

## First test

Use Mini REA XP AI 0.2, the Archive Vault puzzle and a [SmolLM2 135M Q4_K_M GGUF](https://huggingface.co/bartowski/SmolLM2-135M-Instruct-GGUF). Extract both ZIPs to writable folders. Open BoltWorks-Mini-REA-XP.exe, browse for VAULT.EXE and click Inspect. Browse for the GGUF, then click Ask AI. [Mini REA instructions](MINIREA-XP-AI.md)

For a first coding run, use the [SmolLM2 360M IQ4_XS GGUF](https://huggingface.co/bartowski/SmolLM2-360M-Instruct-GGUF). Try the [Qwen2.5-Coder 3B IQ4_XS GGUF](https://huggingface.co/bartowski/Qwen2.5-Coder-3B-Instruct-GGUF) after that. [Code Lab instructions](XP-CODE-LAB.md)

For a test report, include your Windows edition and service pack, CPU, RAM, whether the model was on a hard drive, SSD or USB drive, the model filename, and what happened. The Code Lab writes MODEL.LOG and RESULTS.TXT beside DRAFT.C. If you stopped the run, say how long you waited and include any files it had written by then. The Archive Vault makes it possible to test without sharing personal executables.

## Release assets

Attach Mini REA XP AI 0.2, Archive Vault XP and XP Code Lab 0.4 as separate optional downloads on the GitHub release. Keep the known-working Mini REA XP AI 0.1 bundle and the updated diagnostic batch file available for reproducing the original XP test. Keep model files separate. The Windows 10 x86 build has its own [requirements and instructions](../WINDOWS-X86.md).
