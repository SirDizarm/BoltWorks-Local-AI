# Decompile and edit — LlamaLite 0.6.9 / Mini REA 0.4

Two new actions are available in **Work on code > EXE / DLL inspector**. Choose and inspect a file first. Neither action executes the selected program. Outputs go to new files/folders and existing destinations are never overwritten.

## Decompile .NET

Click **Decompile .NET** and choose an existing parent folder. The bundled ILSpy backend creates a new job with original.bin, decompiled.cs, backend-log.txt and TASK.txt. Choose this job folder in Work on code to have your local model review/edit the recovered C#. Larger source files exceeding the coding tool's 512 KiB limit need splitting before model-based editing.

This supports managed .NET EXE/DLL assemblies containing IL. Native C/C++ executables are rejected with an explanation. Decompiled code is reconstructed, not original source: original comments and many names may be lost. Obfuscation and missing local dependencies can reduce quality. References beside the input may be used for resolution, but nothing is downloaded. The output is a C# file, not a recovered complete project; a compatible compiler/project/dependencies are needed to rebuild it. The app does not automatically compile or execute it.

The backend uses ICSharpCode.Decompiler **9.1.0.7988** (ILSpy, MIT) in a separate Windows x64 process, with a bundled .NET 10 runtime. No Node, cloud service, model, SDK or separate .NET 10 installation is required at use time. Keep the entire **decompiler** folder beside mini-rea.exe/the app. The lightweight frontend still requires .NET Framework. This backend is for modern Windows x64, not XP/98/95.

Limits: input up to 64 MiB; parent terminates the helper after 120 seconds, with a 110-second cooperative backend limit; output at most 16 million characters. These are not a hard memory quota. Failed/cancelled jobs retain an INCOMPLETE.txt marker and may contain partial output. A completed job has no such marker. Local third-party parsers are still software processing untrusted input; this is not an OS sandbox.

## Replace in copy

Select a **Windows resource number** from the inspection report, click **Replace in copy**, choose a replacement raw-data file, then choose a NEW executable filename. The GUI checks that the source has not changed since inspection. The editor:

1. Creates a temporary copy in the chosen output directory.
2. Calls Windows' resource-update API to replace exactly the selected type/name/language resource, allowing a different size.
3. Re-inspects the result, verifies the replacement bytes and every unselected resource's bytes, then publishes the new file.

The original remains unchanged. Failed edits do not publish an output; temporary copies are removed when possible. Replacement data must contain 1 byte to 16 MiB. The input uses the existing 256 MiB inspection limit. Editing is refused if inspection is incomplete, the resource path is unsupported, or the input contains a certificate table or trailing overlay bytes. This avoids rewriting attached data that the current editor cannot preserve reliably.

Use raw resource data of the correct format. A standalone .ico/.bmp file is not interchangeable with an ICON/BITMAP resource; manifests, dialogs, strings, version tables and grouped icons also have their own formats. This is not an image conversion tool, an arbitrary code patcher, or a managed .resources editor. Empty-file deletion is not supported. Matching bytes and valid PE structure do not prove the program will behave correctly. Strong names or other integrity mechanisms can be invalidated; signing is not repaired or verified.

## Standalone commands

```powershell
.\mini-rea.exe decompile 'C:\Apps\example.dll' 'C:\Analysis'
.\mini-rea.exe pe 'C:\Apps\example.exe'
.\mini-rea.exe replace-resource 'C:\Apps\example.exe' 0 'C:\Analysis\raw-resource.bin' 'C:\Analysis\example-edited.exe'
```

The parent folder must exist. Resource indexes refer to that exact input; inspect again after any change. The full portable app and standalone Mini REA package include the backend; copying only mini-rea.exe omits decompilation support.

## What was tested

A locally generated .NET console program was decompiled to C#, its returned message was edited, and the result was rebuilt with the local compiler. Running that rebuilt demo printed the changed message. The original hash stayed identical. This demonstrates an actual decompile/edit/rebuild workflow for a simple supported program, not a guarantee for arbitrary software.

Locally generated native x86/x64 DLL fixtures each had two resources. Replacing one with a larger payload produced new DLLs with the exact new bytes and the second resource unchanged. Tests also checked original preservation, refusal to overwrite, invalid indexes, overlay refusal and a clear failure for native-machine-code decompilation. Only our own generated managed demo was executed. No user-supplied program was launched.

References: [ILSpy](https://github.com/icsharpcode/ILSpy), [pinned decompiler package](https://www.nuget.org/packages/ICSharpCode.Decompiler/9.1.0.7988), [Windows resource update API](https://learn.microsoft.com/en-us/windows/win32/menurc/adding-deleting-and-replacing-resources). License files are included in the backend folder; the dependency lock file is in integration/decompiler.
