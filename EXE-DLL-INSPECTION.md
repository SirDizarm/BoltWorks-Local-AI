Current update: LlamaLite 0.6.9 / Mini REA 0.4 adds separate **Decompile .NET** and **Replace in copy** actions. See [DECOMPILE-AND-EDIT.md](DECOMPILE-AND-EDIT.md). The structural inspection/extraction documentation below describes the original 0.3 functionality; its statements about no replacement apply to inspection/extraction, not the new editing action.

# EXE / DLL deep inspection — Mini REA 0.3

In LlamaLite 0.6.8, open **Work on code > EXE / DLL inspector** and choose a file. No model, Node, internet access or disassembler installation is needed. Inspection reads bytes; it never launches the target, loads it as a DLL or reflects over it as a managed assembly.

The inspector supports PE32 and PE32+ executable images. It reports:

- Machine identifier, image base, entry-point RVA, subsystem and selected declared flags.
- Section names, virtual addresses, raw file ranges and data directories.
- Normal imports by DLL and function name/ordinal, with IAT addresses.
- Exports, aliases, ordinal-only entries and forwarder strings.
- Windows resource type/name/language paths, indexes, codepages and raw byte ranges.
- CLR header indicators and managed metadata version strings.
- Certificate-table presence and trailing bytes after sections. Certificates are not verified; trailing bytes are not automatically unused.

For AI explanations, use **Inspect binary / firmware** to create an analysis project. It now includes **pe-details.txt**, alongside the original copy, SHA-256, strings, hex and firmware reports. The prepared task tells the local model to read the PE report and distinguish evidence from hypotheses. An import does not prove a function is called, a timestamp does not prove the build date, and a declared protection flag does not prove security.

## Extract resources

The dedicated inspector lists resource numbers beginning at zero. Enter the number at the bottom and click **Extract resource**, then choose a NEW output filename. It saves the exact resource bytes, without changing the original executable. The GUI checks that the source has not changed since inspection. Keep the inspection report to associate bytes with the original resource path.

Extraction is raw: an ICON resource is not necessarily a standalone .ico file, a BITMAP resource may lack a .bmp file header, and STRING/VERSION resources use structured binary formats. Manifests and RCDATA may contain directly usable text or arbitrary bytes, but their contents remain untrusted. This version does not automatically preview images, decode version/string tables, reconstruct image containers or replace resources inside an executable.

Standalone commands:

```powershell
.\mini-rea.exe pe 'C:\Programs\example.dll'
.\mini-rea.exe resource 'C:\Programs\example.dll' 0 'C:\Analysis\resource-0.bin'
```

Resource numbers refer to traversal order for that exact input. If the file changes, inspect it again. Output files must not exist; cancellation or I/O failure can leave partial output, so retry with a new filename. The original is opened read-only and held against writes during parsing/extraction.

## Scope and limits

This is structural inspection, not disassembly, decompilation or recovered source. Managed method/type tables, IL, managed embedded resources, delay imports, debug symbols, relocation contents and runtime-resolved imports are not decoded. Old DOS/NE/LE/LX executables retain the earlier basic header reporting but are not handled by this PE deep inspector. Packed/encrypted programs may reveal little until a separate suitable analysis method is available.

Input limit: 256 MiB. Bounds: 96 sections; 256 normal import descriptors; 4096 import entries/export entries/resource nodes; 512 resource leaves; resource depth 8; 256-byte import/export names; 128-character resource names; approximately 120,000 report characters. Truncation, ambiguous mappings, unsupported values and limits are marked INCOMPLETE instead of claiming a complete inspection. Valid resource entries listed before an error can still be extracted. The report is small enough for the existing coding workspace's paged reads.

Replacing content or removing external dependencies is a separate engineering task. Changing an image can break its layout or invalidate its signature; understanding a file does not guarantee we can rebuild a working replacement. Original source recovery is not guaranteed.

## Verification

Known-source native x86/x64 DLL fixtures were assembled locally and never executed. Tests checked normal imports, exported aliases, resource paths, byte-exact extraction, preservation of originals, rejection of overwrites/invalid indexes, managed EXE/DLL indicators, malformed offsets, short inputs and cyclic resource trees. Existing firmware inspection tests also passed. No proprietary application or actual legacy OS was used as a test target. The tool remains a Windows .NET Framework application; XP/98/95 compatibility has not been established.

Format reference: [Microsoft PE/COFF specification](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format). This implementation does not incorporate REA code or execute its scripts.

## Large PE reports (LlamaLite 0.6.10 / Mini REA 0.5)

EXE/DLL inspection no longer has a 256 MiB input-size cap. Files are read by offset rather than loaded entirely into memory. The on-screen report previews up to 120,000 characters per section; shortening imports no longer hides exports, Windows resources or CLR information. Parsing continues beyond the preview.

Use **Save full report** in the EXE/DLL inspector for a streamed text report with no report-length cap. Choose a new output filename. CLI `mini-rea.exe pe FILE` also streams the full report, so it can be redirected to a text file. Cancelled exports may leave a partial file.

The previous 4,096 import/export, 256 import-library and 512 resource-leaf caps are removed. File-bound checks, cycle detection, a resource nesting bound and one-million-node/alias safety budgets remain for malformed or extreme inputs. Names are bounded to 64 KiB. Any structural parsing failure is explicitly marked INCOMPLETE; full report means all information this parser supports, not every possible executable feature.

This change applies to PE inspection and resource extraction, not arbitrary-format support. Firmware scanning/import-project limits and the .NET decompiler's separate limits remain. Native machine-code decompilation and embedded text searching are not added by this update.

## Embedded asset browser (0.6.11)

Open Work on code > EXE / DLL inspector, choose a file, then select **Resources / images / scripts**. Select a row to preview it. The Image tab supports common PNG/JPEG/GIF/BMP images, ICO images, standard Windows bitmap resources and individual icon resources. Text / scripts shows recognizable UTF-8 or little-endian UTF-16 text; Hex bytes provides a byte preview. No script or inspected executable is run.

Selecting a row sets Resource # for Extract resource / Replace in copy. Extraction preserves original bytes: raw Windows icon/bitmap resources are not necessarily standalone ICO/BMP files. Previews have separate memory/display budgets (16 MiB resource data, 16 million decoded pixels, 256 KiB text, 16 KiB hex); extraction and full inspection reports are not shortened by these preview budgets.

The list uses virtual rows for large resource collections. It covers PE Windows resources only, not external launcher archives, encrypted assets, arbitrary embedded files, compiled code reconstructed as scripts, or managed .NET embedded-resource collections. These require additional format-specific readers.

## Report search (0.6.12)

The Inspection report tab has Find, Previous, Next, Match case and a match counter. Ctrl+F focuses Find; Enter or F3 moves forward, Shift+Enter or Shift+F3 moves backward. Navigation wraps at either end. Searches cover the displayed report only; save the full report to search entries omitted from a shortened section preview in your text editor.

## Save images and text/scripts (0.6.13)

Select an entry in Resources / images / scripts. **Save image as PNG** exports the displayed image (including decoded Windows icon/bitmap resources) as a standalone PNG. For animated or multi-image formats this saves the displayed frame only; Extract resource preserves the original resource bytes.

**Save text / script** is enabled for recognizable UTF-8 or UTF-16 text. Choose .txt, .js, .java, .json, .xml, .html, .css, .lua, .cs or another extension. Saving writes the complete selected resource bytes, including its original encoding/BOM; it never saves a truncated preview or its explanatory messages. Previewable resources are limited to 16 MiB; use Extract resource for larger raw resources. Text previews show up to 131,072 characters.

Both buttons require a new filename and leave the source executable unchanged. A text-looking resource is not necessarily valid source code. Saving as .java does not decompile Java .class files. External archives and managed embedded resource containers remain outside this resource browser's scope.

## Binary text, URL and path search (0.6.14)

Choose a file in the EXE / DLL inspector and open **Binary text / URLs / paths**. Enter a substring and click Search binary (or Enter). A blank query lists all detected strings. All / URLs / Paths filters and Match case narrow the results. Rows include a hexadecimal file offset, encoding, candidate kind and text; select a row to copy its detail text.

This scans the entire file, independently of the report preview and resource list, using 64 KiB read buffers. It finds printable ASCII runs and ASCII-range UTF-16 little/big-endian runs at either byte alignment, with a minimum length of four characters. Non-ASCII Unicode text, compressed/encrypted data and generated runtime strings are not decoded. URL/path classification is heuristic and is not evidence of a live service or a file on disk; nothing is executed or contacted.

The UI keeps the first 2,000 matching fragments. Export all matches rescans with the current query/filter and streams every match to a NEW text file with no total-result cap. Strings longer than 4,096 characters are emitted in fragments with 256-character overlap, so a substring of up to 256 characters crossing a fragment boundary can still match. Overlapping fragments can repeat content. Offsets identify the start of the displayed fragment, not necessarily the searched substring. UTF-16 alignment guesses can produce overlapping candidates.

Stop cancels a scan. Cancelled/failed export files may be partial; successful reports end with COMPLETE and a match count. Export uses the source file as it exists when the scan starts and holds it read-only during the scan.
