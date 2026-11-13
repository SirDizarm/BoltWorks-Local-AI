# Contributing

Build the app using [BUILDING.md](BUILDING.md). For a bug report, include the app version, the Windows version, the feature, steps to reproduce it, and expected and actual results. For inference bugs, include the model filename and runtime version. Remove credentials, machine-specific paths, and private data from shared logs and samples.

Mini REA contributions are welcome. Start with `src/MiniRea.cs` for firmware scans, `src/PeInspector.cs` for PE analysis, and `src/MiniReaNetwork.cs` for Ethernet inspection. Prefer small synthetic fixtures for new parsers and malformed-file handling. Router changes should document the device or fixture, which requests are sent, and what has been verified.

Pull requests should describe the behavior changed and the checks run. Include upstream licenses and notices for any third-party code you add.
