using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace BoltWorks.LlamaLite {
static class DecompilerRunner {
    static string Quote(string value){return "\""+value+"\"";}
    public static string Run(string input,string parent,CancellationToken cancel){
        string helper=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"decompiler","BoltWorks.Decompiler.exe");
        if(!File.Exists(helper))throw new IOException("Offline .NET decompiler backend missing. Place the complete decompiler folder beside this application; no automatic download is performed.");
        input=Path.GetFullPath(input);parent=Path.GetFullPath(parent);if(!Directory.Exists(parent))throw new IOException("Choose an existing output parent folder.");
        string folder=Path.Combine(parent,"decompiled-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
        using(var source=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read)){
            if(source.Length>64L*1024*1024)throw new IOException("Decompilation input limit: 64 MiB.");cancel.ThrowIfCancellationRequested();Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"INCOMPLETE.txt"),"Decompilation has not completed. Partial output is not a successful result.");
            using(var copy=new FileStream(Path.Combine(folder,"original.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){var b=new byte[65536];int n;while((n=source.Read(b,0,b.Length))>0){cancel.ThrowIfCancellationRequested();copy.Write(b,0,n);}}
            string output=Path.Combine(folder,"decompiled.cs");
            var start=new ProcessStartInfo(helper,Quote(input)+" "+Quote(output)){WorkingDirectory=Path.GetDirectoryName(helper),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=new Process {StartInfo=start}){
                process.Start();var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var clock=Stopwatch.StartNew();
                try{while(!process.WaitForExit(100)){cancel.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>120)throw new TimeoutException("Decompilation exceeded 120 seconds.");}}
                finally{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}
                string log=stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult();File.WriteAllText(Path.Combine(folder,"backend-log.txt"),log,new UTF8Encoding(false));cancel.ThrowIfCancellationRequested();
                if(process.ExitCode!=0||!File.Exists(output))throw new IOException("Decompilation failed: "+log+"\r\nJob files: "+folder);
            }
            File.WriteAllText(Path.Combine(folder,"TASK.txt"),"Review decompiled.cs as reconstructed C#, not original source. The input was not executed. Explain important classes/methods and cite the code. Do not run original.bin. Missing local references or obfuscation may leave imperfect code. Build changes only through approved local commands; do not install packages or use the internet. Output is not a reconstructed project; prepare an appropriate project/toolchain before attempting a rebuild. Files over 512 KiB need splitting into smaller source files before using the coding agent's file tools.\r\n");
            File.Delete(Path.Combine(folder,"INCOMPLETE.txt"));return folder;
        }
    }
}
}
