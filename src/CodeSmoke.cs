using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BoltWorks.LlamaLite {
static class CodeSmoke {
    public static async Task Lifecycle(string folder,string model) {
        Directory.CreateDirectory(folder);
        using(var engine=new Engine()) {using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60)))await engine.Start(model,4,16384,timeout.Token);engine.Dispose();}
        File.WriteAllText(Path.Combine(folder,"probe.txt"),await CodeCommand.Run(folder,"Write-Output 'AFTER_MODEL'\nGet-Location",CancellationToken.None));
        File.WriteAllText(Path.Combine(folder,"Greeting.cs"),"using System; class Greeting { static void Main() { Console.WriteLine(\"Hello\"); } }");
        string result=await Verify(folder);
        File.WriteAllText(Path.Combine(folder,"lifecycle.txt"),result);Check(result.Contains("Hello")&&result.Contains("Exit code: 0"),"Compiler after model unload failed: "+result);
    }
    public static async Task<string> Verify(string folder) {
        string result=await CodeCommand.Run(folder,"& 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe' /nologo /out:Greeting.exe Greeting.cs\nif ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\n& '.\\Greeting.exe'",CancellationToken.None);
        File.WriteAllText(Path.Combine(folder,"compile-result.txt"),result);Check(result.Contains("Hello")&&result.Contains("Exit code: 0"),"C# compile/run failed: "+result);return result;
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Reject(Action action,string message){try{action();}catch(IOException){return;}throw new Exception(message);}
    public static async Task Run(string folder,string model) {
        if(Directory.Exists(folder)&&Directory.GetFileSystemEntries(folder).Length>0)folder=Path.Combine(folder,"run-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);string root=Path.Combine(folder,"project");Directory.CreateDirectory(root);
        var files=new CodeWorkspace(root,Path.Combine(folder,"journal"));
        File.WriteAllText(Path.Combine(root,"main.txt"),"one\r\ntwo\r\n",new UTF8Encoding(true));
        Reject(()=>files.Read("../outside",0),"Traversal allowed");Reject(()=>files.Resolve("C:\\Windows"),"Absolute path allowed");Reject(()=>files.Resolve("file:stream"),"Alternate stream allowed");Reject(()=>files.Resolve(".git/config"),"Git metadata exposed");Reject(()=>files.Resolve(".env"),"Environment file exposed");
        Reject(()=>files.Edit("main.txt","one","ONE",false),"Unread edit allowed");files.Read("main.txt",0);
        File.AppendAllText(Path.Combine(root,"main.txt"),"three");Reject(()=>files.Edit("main.txt","one","ONE",false),"Concurrent edit overwritten");
        files.Read("main.txt",0);files.Edit("main.txt","one","ONE",false);
        Check(File.ReadAllText(Path.Combine(root,"main.txt"))=="ONE\r\ntwo\r\nthree","Replacement mismatch");Check(File.ReadAllBytes(Path.Combine(root,"main.txt"))[0]==239,"BOM lost");
        Check(Directory.GetFiles(files.Journal,"*.before").Length==1,"Backup missing");
        files.Edit("nested/new.txt",null,"hello",true);Reject(()=>files.Edit("nested/new.txt",null,"overwrite",true),"Create overwrote existing file");
        files.Read("main.txt",0);Reject(()=>files.Edit("main.txt","missing","bad",false),"Missing old accepted");
        Check(files.Search("main.txt","two").Contains("Character"),"Search failed");
        string success=await CodeCommand.Run(root,"Write-Output 'actual output'",CancellationToken.None);Check(success.Contains("Exit code: 0")&&success.Contains("actual output"),"Command output failed");
        string failed=await CodeCommand.Run(root,"exit 7",CancellationToken.None);Check(failed.Contains("Exit code: 7"),"Exit code lost");
        File.WriteAllText(Path.Combine(root,"CommandTest.cs"),"using System; class CommandTest { static void Main() { Console.WriteLine(\"COMMAND_OK\"); } }");
        string compiled=await CodeCommand.Run(root,"& 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe' /nologo /out:CommandTest.exe CommandTest.cs\nif ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\n& '.\\CommandTest.exe'",CancellationToken.None);
        File.WriteAllText(Path.Combine(folder,"command-compile.txt"),compiled);Check(compiled.Contains("Exit code: 0")&&compiled.Contains("COMMAND_OK"),"Command compiler/executable failed: "+compiled);
        using(var cancel=new CancellationTokenSource(500)){bool stopped=false;try{await CodeCommand.Run(root,"Start-Sleep -Seconds 30",cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"Command cancellation failed");}
        var replies=new Queue<string>(new[]{"not JSON","{\"tool\":\"read\",\"path\":\"nested/new.txt\"}","{\"tool\":\"replace\",\"path\":\"nested/new.txt\",\"old\":\"hello\",\"text\":\"updated\"}","{\"tool\":\"command\",\"text\":\"throw 'must not run'\"}","{\"tool\":\"finish\",\"text\":\"Edited the file; command denied.\"}"});
        bool denied=false,errorsFedBack=false;
        var agent=new CodeAgent((messages,cancel)=>{if(Engine.Json.Serialize(messages).Contains("Tool error"))errorsFedBack=true;return Task.FromResult(replies.Dequeue());},(command,cancel)=>{denied=true;return Task.FromResult(false);},s=>{});
        await agent.Run(files,"Update nested/new.txt",8,CancellationToken.None);
        Check(denied&&errorsFedBack&&File.ReadAllText(Path.Combine(root,"nested/new.txt"))=="updated","Agent loop failed");
        var summary=JsonValue.Object(Engine.Json.DeserializeObject(File.ReadAllText(Path.Combine(files.Journal,"summary.json"))));Check(Convert.ToInt32(summary["savedEdits"])==1&&Convert.ToInt32(summary["commandsRun"])==0,"Application action counts are inaccurate");
        string before=File.ReadAllText(Path.Combine(root,"main.txt"));
        await new CodeAgent((m,c)=>Task.FromResult("{\"tool\":\"replace\",\"path\":\"main.txt\",\"old\":\"ONE\"}"),(s,c)=>Task.FromResult(false),s=>{}).Run(files,"Inspect malformed edit",4,CancellationToken.None);
        Check(File.ReadAllText(Path.Combine(root,"main.txt"))==before,"Missing text caused an unintended deletion");
        string limit=await new CodeAgent((m,c)=>Task.FromResult("{\"tool\":\"list\",\"path\":\".\"}"),(s,c)=>Task.FromResult(false),s=>{}).Run(files,"Inspect",1,CancellationToken.None);Check(limit.Contains("step limit"),"False completion");
        File.WriteAllText(Path.Combine(folder,"result.txt"),"PASS: scoped paths, stale-read guard, UTF-8 BOM, backup, create conflicts, exact replace, search, real command output/exit codes/cancellation, malformed action feedback, denied command, agent editing and step limit.\r\n");
        if(!string.IsNullOrEmpty(model)) {
            using(var engine=new Engine()) {
                using(var loading=new CancellationTokenSource(TimeSpan.FromSeconds(60)))try {await engine.Start(model,4,16384,loading.Token);}catch(Exception e){throw new IOException("Native model startup failed: "+e.Message+"\n"+engine.Diagnostics,e);}
                const string source="using System;\nclass Greeting { static void Main() { Console.WriteLine(\"Helo\"); } }\n";
                File.WriteAllText(Path.Combine(root,"Greeting.cs"),source,new UTF8Encoding(false));
                var native=new CodeAgent((messages,cancel)=>engine.Chat(messages,2048,s=>{},cancel,CodeAgent.Format),(command,cancel)=>Task.FromResult(false),s=>File.AppendAllText(Path.Combine(folder,"native-log.txt"),s+"\r\n"));
                using(var budget=new CancellationTokenSource(TimeSpan.FromMinutes(4)))await native.Run(new CodeWorkspace(root,Path.Combine(folder,"native-journal")),"Read Greeting.cs and fix the typo Helo to Hello in its printed string. Preserve the program. Read it again to verify the saved edit, then finish. No commands needed.",12,budget.Token);
                Check(File.ReadAllText(Path.Combine(root,"Greeting.cs"))==source.Replace("Helo","Hello"),"Real model did not make expected edit");
                engine.Dispose();
                string built=await Verify(root);
                Check(built.Contains("Exit code: 0")&&built.Contains("Hello"),"Edited C# did not compile and execute: "+built);
                File.AppendAllText(Path.Combine(folder,"result.txt"),"PASS: real local model read and edited Greeting.cs; independently compiled with local C# compiler and executed with expected Hello output.\r\n"+built);
            }
        }
    }
}
}
