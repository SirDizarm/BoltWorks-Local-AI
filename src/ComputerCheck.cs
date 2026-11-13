using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
static class ComputerCheck {
    [StructLayout(LayoutKind.Sequential)] struct Status {public uint Length,Load;public ulong Total,Available,TotalCommit,AvailableCommit,TotalVirtual,AvailableVirtual,Extended;}
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref Status status);
    public sealed class Result {
        public string Model,Outcome,Answer;
        public long ModelBytes,PeakWorkingBytes;
        public double LoadSeconds,FirstTokenSeconds,TokensPerSecond;
        public int Tokens;
        public int ChecksPassed,ChecksRun;
        public string CheckResponses;
        public bool Completed;
    }
    public static ulong AvailableRam(){var s=new Status();s.Length=(uint)Marshal.SizeOf(s);if(!GlobalMemoryStatusEx(ref s))return 0;return s.Available;}
    public static Result Recommend(IEnumerable<Result> results){return results.Where(r=>r.Completed&&r.Tokens>=8&&r.TokensPerSecond>=1&&(r.ChecksRun==0||r.ChecksPassed>0)).OrderByDescending(r=>r.ChecksPassed).ThenByDescending(r=>r.TokensPerSecond).FirstOrDefault();}
    internal static string OtherRole(string path) {
        string name=Path.GetFileName(path).ToLowerInvariant();
        if(name.Contains("mmproj")||name.Contains("projector"))return "Vision companion. Select this as a projector alongside its matching vision model, not as the main chat model.";
        if(name.Contains("stable-diffusion")||name.Contains("sdxl")||name.StartsWith("flux"))return "Picture-generation model. Use the Create pictures slot and image runtime.";
        return null;
    }
    public static async Task<int> RunReport(string path,string[] models) {
        var slow=new Result{Completed=true,Tokens=30,TokensPerSecond=2};
        var failed=new Result{Completed=false,Tokens=50,TokensPerSecond=50};
        var fast=new Result{Completed=true,Tokens=35,TokensPerSecond=9};
        if(Recommend(new[]{slow,failed,fast})!=fast||Recommend(new[]{failed})!=null)
            throw new InvalidOperationException("Computer check recommendation failed validation.");
        slow.ChecksPassed=2;fast.ChecksPassed=1;
        if(Recommend(new[]{slow,fast})!=slow||OtherRole("mmproj-model.gguf")==null||OtherRole("stable-diffusion-v1.gguf")==null||OtherRole("Qwen3-0.6B.gguf")!=null)
            throw new InvalidOperationException("Model role or instruction ranking failed validation.");
        var results=new List<Result>();
        int threads=Math.Max(1,Math.Min(8,Environment.ProcessorCount/2));
        foreach(string model in models) {
            results.Add(await Test(model,threads,CancellationToken.None));
            File.WriteAllText(path,Report(results,threads),new UTF8Encoding(false));
        }
        return Recommend(results)==null?1:0;
    }
    public static async Task<Result> Test(string path,int threads,CancellationToken cancel) {
        var result=new Result{Model=path};
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
            timeout.CancelAfter(TimeSpan.FromSeconds(150));
            try {
                string role=OtherRole(path);
                if(role!=null){result.Outcome="Not a chat candidate: "+role;return result;}
                var set=GgufSet.Inspect(path);result.Model=set.First;result.ModelBytes=set.Bytes;
                ulong available=AvailableRam();
                if(available>0&&set.Bytes>(double)available*.65){result.Outcome="Skipped. Model files exceed 65% of currently available RAM. Smaller quantization recommended.";return result;}
                using(var engine=new Engine{Backend=ComputeBackend.Cpu,Memory=MemoryMode.Low}) {
                    var load=Stopwatch.StartNew();
                    await engine.Start(set.First,threads,2048,timeout.Token);
                    result.LoadSeconds=load.Elapsed.TotalSeconds;
                    using(var process=Process.GetProcessById(engine.ProcessId)) {
                        var watch=Stopwatch.StartNew();double first=-1;
                        string answer=await engine.Chat(new List<Dictionary<string,object>> {
                            ChatWindow.Message("system","Answer directly in English without reasoning tags."),
                            ChatWindow.Message("user","Explain how to create a folder and save a text file in it. Write about sixty words.")
                        },96,chunk=>{
                            if(first<0&&chunk.Length>0)first=watch.Elapsed.TotalSeconds;
                            try{process.Refresh();result.PeakWorkingBytes=Math.Max(result.PeakWorkingBytes,process.PeakWorkingSet64);}catch(InvalidOperationException){}
                        },timeout.Token);
                        double elapsed=watch.Elapsed.TotalSeconds;
                        result.Answer=answer;result.FirstTokenSeconds=Math.Max(0,first);
                        result.Tokens=await engine.CountTextTokens(answer,timeout.Token);
                        result.TokensPerSecond=result.Tokens/Math.Max(.001,elapsed);
                        process.Refresh();result.PeakWorkingBytes=Math.Max(result.PeakWorkingBytes,process.PeakWorkingSet64);
                        result.Completed=!string.IsNullOrWhiteSpace(answer)&&first>=0;
                        result.Outcome=!result.Completed?"Empty response":result.Tokens<8?"Reply too short to rank":result.TokensPerSecond>=5?"Interactive":result.TokensPerSecond>=1?"Usable, slower replies":"Very slow";
                        var replies=new StringBuilder();
                        string[] tasks={"What is 12 + 5? Reply with only the number.","Return exactly this JSON object with no other text: {\"ready\":true}"};
                        string[] expected={"17","{\"ready\":true}"};
                        for(int check=0;check<tasks.Length;check++) {
                            string reply=await engine.Chat(new List<Dictionary<string,object>> {
                                ChatWindow.Message("system","Follow the user's requested output format exactly."),ChatWindow.Message("user",tasks[check])
                            },40,chunk=>{},timeout.Token);
                            result.ChecksRun++;
                            string compact=System.Text.RegularExpressions.Regex.Replace(reply,@"\s+","");
                            if(compact==expected[check])result.ChecksPassed++;
                            replies.AppendLine("Check "+(check+1)+": "+reply);
                        }
                        result.CheckResponses=replies.ToString();
                        process.Refresh();result.PeakWorkingBytes=Math.Max(result.PeakWorkingBytes,process.PeakWorkingSet64);
                    }
                }
            }catch(OperationCanceledException){cancel.ThrowIfCancellationRequested();result.Completed=false;result.Outcome="Timed out after 150 seconds. Runtime unloaded.";}
            catch(Exception e){cancel.ThrowIfCancellationRequested();result.Completed=false;result.Outcome="Failed: "+e.Message;}
        }
        return result;
    }
    public static string Report(List<Result> results,int threads) {
        var s=new StringBuilder("BoltWorks Local AI computer check\r\n");
        s.AppendLine(DateTimeOffset.Now.ToString("o"));s.AppendLine(MemoryReport.Summary());
        s.AppendLine("CPU logical processors: "+Environment.ProcessorCount+"; tested threads: "+threads);
        s.AppendLine("CPU only; Low RAM mode; context 2048; reply limit 96; 150-second budget per model.");
        s.AppendLine("Throughput includes prompt processing. Peak working set is the runtime process's resident memory, not total system or GPU usage.");
        foreach(var r in results){s.AppendLine();s.AppendLine(r.Model);s.AppendLine(r.Outcome);s.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"Load {0:F2}s; first text {1:F2}s; output {2} tokens; {3:F2} tokens/s; peak resident {4:F0} MiB\r\n",r.LoadSeconds,r.FirstTokenSeconds,r.Tokens,r.TokensPerSecond,r.PeakWorkingBytes/1048576.0);s.AppendLine("Response: "+r.Answer);}
        foreach(var r in results)if(r.ChecksRun>0){s.AppendLine(Path.GetFileName(r.Model)+": simple instruction checks "+r.ChecksPassed+"/"+r.ChecksRun);s.AppendLine(r.CheckResponses);}
        var best=Recommend(results);
        s.AppendLine();s.AppendLine(best==null?"No recommendation: candidates failed loading, speed requirements, or both simple instruction checks.":"Recommended among tested models (simple checks first, then speed): "+best.Model);
        s.AppendLine("This short benchmark measures compatibility, speed and two exact-format instruction checks. It does not evaluate general reasoning, code, image reading, picture generation, or long-context memory use. A skipped model may still run with paging.");
        return s.ToString();
    }
}

sealed class ComputerCheckWindow:Form {
    readonly ListBox models=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};
    readonly TextBox report=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false};
    readonly Button add=new Button{Text="Add GGUF models",AutoSize=true},remove=new Button{Text="Remove",AutoSize=true},run=new Button{Text="Run CPU check",AutoSize=true},stop=new Button{Text="Stop",AutoSize=true,Enabled=false},save=new Button{Text="Save report",AutoSize=true};
    readonly NumericUpDown threads=new NumericUpDown{Minimum=1,Maximum=64,Width=60};
    CancellationTokenSource active;
    readonly List<ComputerCheck.Result> results=new List<ComputerCheck.Result>();
    public ComputerCheckWindow(string current) {
        Text="BoltWorks Local AI — Check this computer";Size=new Size(870,660);MinimumSize=new Size(700,500);StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Segoe UI",10);threads.Value=Math.Max(1,Math.Min(8,Environment.ProcessorCount/2));
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(12)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,125));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(new Label{Text="Choose local chat GGUF models to compare. Each test loads one model, generates a short reply and unloads it. Large models may be skipped to leave RAM for Windows.\r\n"+MemoryReport.Summary(),Dock=DockStyle.Fill},0,0);
        layout.Controls.Add(models,0,1);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.AddRange(new Control[]{add,remove,new Label{Text="Threads",AutoSize=true},threads,run,stop,save});layout.Controls.Add(actions,0,2);layout.Controls.Add(report,0,3);Controls.Add(layout);
        if(File.Exists(current))models.Items.Add(current);
        add.Click+=delegate{using(var picker=new OpenFileDialog{Filter="Chat GGUF models|*.gguf",Multiselect=true})if(picker.ShowDialog(this)==DialogResult.OK)foreach(string path in picker.FileNames)try{string first=GgufSet.Inspect(path).First;if(!models.Items.Cast<string>().Any(p=>string.Equals(p,first,StringComparison.OrdinalIgnoreCase)))models.Items.Add(first);}catch(Exception e){MessageBox.Show(this,e.Message);}};
        remove.Click+=delegate{if(models.SelectedIndex>=0)models.Items.RemoveAt(models.SelectedIndex);};
        stop.Click+=delegate{if(active!=null)active.Cancel();};
        save.Click+=delegate{using(var picker=new SaveFileDialog{Filter="Report|*.txt",FileName="BoltWorks-Local-AI-computer-check.txt"})if(picker.ShowDialog(this)==DialogResult.OK)try{File.WriteAllText(picker.FileName,report.Text,new UTF8Encoding(false));}catch(Exception e){MessageBox.Show(this,e.Message);}};
        run.Click+=async delegate{await Run();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(active!=null){e.Cancel=true;active.Cancel();}};
    }
    async Task Run() {
        if(models.Items.Count==0)return;
        active=new CancellationTokenSource();add.Enabled=remove.Enabled=run.Enabled=threads.Enabled=false;stop.Enabled=true;results.Clear();
        int count=(int)threads.Value;
        try {
            foreach(string path in models.Items){report.Text=ComputerCheck.Report(results,count)+"\r\nTesting "+path+" …";var result=await ComputerCheck.Test(path,count,active.Token);results.Add(result);report.Text=ComputerCheck.Report(results,count);}
        }catch(OperationCanceledException){report.Text=ComputerCheck.Report(results,count)+"\r\nStopped. Active runtime unloaded.";}
        finally{active.Dispose();active=null;add.Enabled=remove.Enabled=run.Enabled=threads.Enabled=true;stop.Enabled=false;}
    }
}
}
