using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using System.Reflection.PortableExecutable;

// This process parses managed metadata/IL; it never Assembly.Loads the input.
try {
    if(args.Length!=2)throw new ArgumentException("Expected INPUT_ASSEMBLY NEW_OUTPUT_CS");
    string input=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
    using var guard=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(guard.Length>64L*1024*1024)throw new IOException("Decompilation input limit: 64 MiB.");
    using(var pe=new PEReader(guard,PEStreamOptions.LeaveOpen))if(!pe.HasMetadata)throw new NotSupportedException("This file is native machine code. This backend decompiles .NET assemblies only.");
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(110));
    var settings=new DecompilerSettings {ThrowOnAssemblyResolveErrors=false};
    var decompiler=new CSharpDecompiler(input,settings){CancellationToken=timeout.Token};
    string code=decompiler.DecompileWholeModuleAsString();
    if(code.Length>16*1024*1024)throw new IOException("Decompiled text exceeds 16 million characters.");
    using var destination=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None);
    using var writer=new StreamWriter(destination,new System.Text.UTF8Encoding(false));
    writer.WriteLine("// Decompiled locally with ILSpy 9.1.0.7988. Not original source. Review before building.");
    writer.WriteLine("// Missing references or obfuscation can affect accuracy. This file was not executed.");
    writer.Write(code);
    Console.WriteLine("C# decompilation saved.");return 0;
}catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
