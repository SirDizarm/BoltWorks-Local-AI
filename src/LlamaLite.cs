using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length>=3&&args[0]=="--computer-check") {
            try {var models=new string[args.Length-2];Array.Copy(args,2,models,0,models.Length);return ComputerCheck.RunReport(args[1],models).GetAwaiter().GetResult();}
            catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}
        }
        if(args.Length>=2&&args[0]=="--memory-test") {try{MemorySmoke.Run(args[1],args.Length>2?args[2]:null).GetAwaiter().GetResult();return 0;}catch(Exception e){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"failure.txt"),e.ToString());return 1;}}
        if(args.Length==2 && args[0]=="--code-verify") {try {CodeSmoke.Verify(args[1]).GetAwaiter().GetResult();return 0;}catch(Exception e){File.WriteAllText(Path.Combine(args[1],"verify-failure.txt"),e.ToString());return 1;}}
        if(args.Length==3 && args[0]=="--code-engine-test") {try {CodeSmoke.Lifecycle(args[1],args[2]).GetAwaiter().GetResult();return 0;}catch(Exception e){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"failure.txt"),e.ToString());return 1;}}
        if(args.Length>=2 && args[0]=="--code-test") {try {CodeSmoke.Run(args[1],args.Length>2?args[2]:null).GetAwaiter().GetResult();return 0;}catch(Exception e){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"failure.txt"),e.ToString());return 1;}}
        if (args.Length == 3 && args[0] == "--self-test") {
            try { File.WriteAllText(args[2],"Starting local test..."); Smoke.ProgressPath=args[2]; Smoke.Run(args[1]).GetAwaiter().GetResult(); File.AppendAllText(args[2], "\r\nPASS: invalid model rejected, load, authenticated streaming chat, cancellation during streaming, unload and process cleanup."); return 0; }
            catch(Exception e) { File.WriteAllText(args[2], e.ToString()); return 1; }
        }
        if(args.Length==3 && args[0]=="--lifecycle-worker") {
            using(var engine=new Engine()) { engine.Start(args[1],2,2048,CancellationToken.None).GetAwaiter().GetResult(); File.WriteAllText(args[2],engine.ProcessId.ToString()); Thread.Sleep(60000); } return 0;
        }
        if(args.Length==5 && args[0]=="--vision-test") {
            try { File.WriteAllText(args[4],"Starting Swift vision test..."); VisionSmoke.Run(args[1],args[2],args[3],args[4]).GetAwaiter().GetResult(); return 0; }
            catch(Exception e) { File.AppendAllText(args[4],"\r\nFAIL: "+e); return 1; }
        }
        if(args.Length==4 && args[0]=="--picture-test") {
            try {File.WriteAllText(args[3],"Starting standalone picture-engine test..."); PictureSmoke.Run(args[1],args[2],args[3]).GetAwaiter().GetResult(); return 0;}
            catch(Exception e) {File.AppendAllText(args[3],"\r\nFAIL: "+e); return 1;}
        }
        if(args.Length==2 && args[0]=="--integration-self-test") {try {IntegrationSmoke.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
        if(args.Length==2 && args[0]=="--slots-test") {try {ModelSlotsSmoke.Run(args[1]);return 0;}catch(Exception e){File.WriteAllText(args[1],e.ToString());return 1;}}
        if(args.Length==2 && args[0]=="--creative-self-test") {try {CreativeSmoke.Run(args[1]); return 0;} catch(Exception e) {File.WriteAllText(args[1],e.ToString()); return 1;}}
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>=2 && args[0]=="--render-preview") {
            using(var window=new ChatWindow()) { window.Show(); if(args.Length==3) window.PreviewTab(int.Parse(args[2])); Application.DoEvents(); window.Refresh(); using(var bitmap=new Bitmap(window.Width,window.Height)) { window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,window.Size)); bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png); } window.Close(); } return 0;
        }
        Application.Run(new ChatWindow()); return 0;
    }
}

// A job object ensures the model process also exits if this application crashes.
sealed class ProcessJob : IDisposable {
    IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] struct Basic { public long a,b; public uint flags; public UIntPtr c,d; public uint e; public UIntPtr f; public uint g,h; }
    [StructLayout(LayoutKind.Sequential)] struct Counters { public ulong a,b,c,d,e,f; }
    [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic basic; public Counters counters; public UIntPtr a,b,c,d; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a,string b);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr h,int c,IntPtr p,uint n);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr h,IntPtr p);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    public ProcessJob(Process process) {
        handle=CreateJobObject(IntPtr.Zero,null);
        var info=new Extended(); info.basic.flags=0x2000;
        int size=Marshal.SizeOf(info); IntPtr ptr=Marshal.AllocHGlobal(size);
        try { Marshal.StructureToPtr(info,ptr,false);
            if(handle==IntPtr.Zero || !SetInformationJobObject(handle,9,ptr,(uint)size) || !AssignProcessToJobObject(handle,process.Handle)) {
                Dispose(); throw new IOException("Windows could not attach the runtime to the app. Model loading was stopped.");
            }
        } finally { Marshal.FreeHGlobal(ptr); }
    }
    public void Dispose() { if(handle!=IntPtr.Zero) { CloseHandle(handle); handle=IntPtr.Zero; } }
}

sealed class Engine : IDisposable {
    public static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
    Process process; ProcessJob job; HttpClient client; string token; readonly StringBuilder log=new StringBuilder();
    public ComputeBackend Backend {get;set;}
    public MemoryMode Memory {get;set;}
    public int ActiveContext {get;private set;}
    public int GpuLayers=16;
    public bool Ready { get; private set; }
    public bool VisionEnabled { get; private set; }
    public bool Alive { get { try { return process!=null && !process.HasExited; } catch { return false; } } }
    public int ProcessId { get { return process==null ? 0 : process.Id; } }
    public string Diagnostics { get { lock(log) return log.ToString(); } }
    static string Quote(string s) { return "\""+s.Replace("\"", "")+"\""; }
    public async Task Start(string model,int threads,int context,CancellationToken cancel) {
        await Start(model,"",threads,context,cancel);
    }
    public async Task Start(string model,string projector,int threads,int context,CancellationToken cancel) {
        Dispose();
        ModelMemory.CheckHost();
        if(Memory==MemoryMode.Disk&&Backend!=ComputeBackend.Cpu)throw new IOException("Disk-backed mode requires CPU processing. Select CPU or a different memory mode.");
        model=GgufSet.Inspect(model).First;
        context=ModelMemory.Context(Memory,context);ActiveContext=context;
        CheckModel(model);
        bool vision=!string.IsNullOrWhiteSpace(projector);
        if(vision) { CheckModel(projector); if(string.Equals(Path.GetFullPath(model),Path.GetFullPath(projector),StringComparison.OrdinalIgnoreCase)) throw new IOException("Select the separate mmproj file as the vision projector, not the main model again."); }
        string runtime=Acceleration.Runtime(AppDomain.CurrentDomain.BaseDirectory,Backend);
        if(!File.Exists(runtime)) throw new FileNotFoundException("The runtime folder is missing. Extract the whole ZIP before opening the app.");
        RuntimePrerequisites.Check(runtime);
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); int port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        token=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
        lock(log) log.Clear();
        string visionArgs=vision ? " --mmproj "+Quote(projector)+" --no-mmproj-offload --image-max-tokens 1024" : " --no-mmproj";
        var start=new ProcessStartInfo(runtime,"-m "+Quote(model)+" --host 127.0.0.1 --port "+port+" --api-key "+token+" --offline --no-webui"+Acceleration.Arguments(Backend,GpuLayers)+ModelMemory.Arguments(Memory)+" --parallel 1 -c "+context+" -t "+threads+" --reasoning off"+visionArgs) {
            UseShellExecute=false, CreateNoWindow=true, WindowStyle=ProcessWindowStyle.Hidden,
            WorkingDirectory=Path.GetDirectoryName(runtime), RedirectStandardError=true, RedirectStandardOutput=true
        };
        // Ignore inherited llama environment settings, including remote model references.
        var keys=new List<string>(); foreach(string key in start.EnvironmentVariables.Keys) if(key.StartsWith("LLAMA_",StringComparison.OrdinalIgnoreCase)) keys.Add(key);
        foreach(string key in keys) start.EnvironmentVariables.Remove(key);
        start.EnvironmentVariables["HF_HUB_OFFLINE"]="1";
        process=new Process { StartInfo=start };
        DataReceivedEventHandler append=delegate(object sender,DataReceivedEventArgs e) { if(e.Data!=null) lock(log) { log.AppendLine(e.Data); if(log.Length>24000) log.Remove(0,log.Length-24000); } };
        process.ErrorDataReceived+=append; process.OutputDataReceived+=append;
        try {
            process.Start(); job=new ProcessJob(process); process.BeginErrorReadLine(); process.BeginOutputReadLine();
            client=new HttpClient(new HttpClientHandler { UseProxy=false }); client.BaseAddress=new Uri("http://127.0.0.1:"+port+"/");
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token); client.Timeout=Timeout.InfiniteTimeSpan;
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed<TimeSpan.FromMinutes(Memory==MemoryMode.Disk?30:5)) {
                cancel.ThrowIfCancellationRequested();
                if(!Alive) throw new IOException("The model runtime exited. It may need more memory or a compatible model.\r\n"+Diagnostics);
                using(var ping=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                    ping.CancelAfter(1500);
                    try { using(var response=await client.GetAsync("health",ping.Token)) { if(response.IsSuccessStatusCode) { cancel.ThrowIfCancellationRequested(); VisionEnabled=vision; Ready=true; return; } } }
                    catch(HttpRequestException) {} catch(OperationCanceledException) { cancel.ThrowIfCancellationRequested(); }
                }
                await Task.Delay(250,cancel);
            }
            throw new TimeoutException("The model did not load within "+(Memory==MemoryMode.Disk?30:5)+" minutes. Try a smaller model or more available RAM.");
        } catch { Dispose(); throw; }
    }
    static void CheckModel(string path) {
        if(!File.Exists(path)) throw new FileNotFoundException("Choose an existing GGUF model or projector file.");
        using(var file=File.OpenRead(path)) { var b=new byte[4]; if(file.Read(b,0,4)!=4 || Encoding.ASCII.GetString(b)!="GGUF") throw new IOException("This is not a GGUF file: "+Path.GetFileName(path)); }
    }
    public async Task<string> Chat(List<Dictionary<string,object>> messages,int maxTokens,Action<string> chunk,CancellationToken cancel,object responseFormat=null) {
        if(!Ready || !Alive) throw new IOException("Load a model first.");
        maxTokens=Math.Max(1,Math.Min(maxTokens,ActiveContext/2));
        var payload=new Dictionary<string,object> { {"messages",HostContext.Apply(messages)},{"stream",true},{"max_tokens",maxTokens},{"temperature",responseFormat==null?0.6:0.2},{"chat_template_kwargs",new Dictionary<string,object>{{"enable_thinking",false}}} };
        if(responseFormat!=null) payload.Add("response_format",responseFormat);
        using(var request=new HttpRequestMessage(HttpMethod.Post,"v1/chat/completions")) {
            request.Content=new StringContent(Json.Serialize(payload),Encoding.UTF8,"application/json");
            using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel)) {
                if(!response.IsSuccessStatusCode) { string error=await response.Content.ReadAsStringAsync(); throw new IOException("Model request failed ("+(int)response.StatusCode+"). If the conversation is too long, start a new chat.\r\n"+error); }
                using(var stream=await response.Content.ReadAsStreamAsync())
                using(var registration=cancel.Register(delegate { stream.Dispose(); }))
                using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                    var answer=new StringBuilder(); bool done=false;
                    try { string line; while((line=await reader.ReadLineAsync())!=null) {
                        cancel.ThrowIfCancellationRequested();
                        if(!line.StartsWith("data: ",StringComparison.Ordinal)) continue;
                        string data=line.Substring(6); if(data=="[DONE]") { done=true; break; }
                        var obj=Json.DeserializeObject(data) as Dictionary<string,object>;
                        if(obj==null) continue;
                        if(obj.ContainsKey("error")) throw new IOException(Json.Serialize(obj["error"]));
                        object raw; if(!obj.TryGetValue("choices",out raw)) continue;
                        var choices=raw as object[]; if(choices==null || choices.Length==0) continue;
                        var choice=choices[0] as Dictionary<string,object>; if(choice==null || !choice.TryGetValue("delta",out raw)) continue;
                        var delta=raw as Dictionary<string,object>; if(delta!=null && delta.TryGetValue("content",out raw) && raw is string) { string text=(string)raw; answer.Append(text); chunk(text); }
                    } } catch { cancel.ThrowIfCancellationRequested(); throw; }
                    cancel.ThrowIfCancellationRequested();
                    if(!done) throw new IOException("The model connection ended before the reply finished.");
                    return answer.ToString();
                }
            }
        }
    }
    async Task<Dictionary<string,object>> PostJson(string route,object payload,CancellationToken cancel) {
        using(var body=new StringContent(Json.Serialize(payload),Encoding.UTF8,"application/json"))using(var response=await client.PostAsync(route,body,cancel)) {
            string raw=await response.Content.ReadAsStringAsync();cancel.ThrowIfCancellationRequested();
            if(!response.IsSuccessStatusCode)throw new IOException("Local runtime could not measure coding context ("+route+"): "+(int)response.StatusCode);
            return JsonValue.Object(Json.DeserializeObject(raw));
        }
    }
    public async Task<int> CountTextTokens(string text,CancellationToken cancel) {
        var result=await PostJson("tokenize",new {content=text,add_special=false,parse_special=false},cancel);
        return JsonValue.Array(result["tokens"]).Length;
    }
    public async Task<int> PromptTokens(List<Dictionary<string,object>> messages,CancellationToken cancel) {
        var template=await PostJson("apply-template",new {messages=HostContext.Apply(messages),add_generation_prompt=true,chat_template_kwargs=new {enable_thinking=false}},cancel);
        string prompt=JsonValue.Text(template,"prompt");if(prompt.Length==0)throw new IOException("The model returned no chat template for context measurement.");
        var result=await PostJson("tokenize",new {content=prompt,add_special=true,parse_special=true},cancel);
        return JsonValue.Array(result["tokens"]).Length;
    }
    public void Dispose() {
        Ready=false; VisionEnabled=false;
        if(client!=null) { client.Dispose(); client=null; }
        if(process!=null) { try { if(!process.HasExited) { process.Kill(); process.WaitForExit(3000); } } catch {} }
        if(job!=null) { job.Dispose(); job=null; }
        if(process!=null) { process.Dispose(); process=null; }
    }
}

sealed class ImageAttachment {
    public string Name,DataUrl;
    public byte[] Jpeg;
    public static ImageAttachment Load(string path) {
        if(new FileInfo(path).Length>20*1024*1024) throw new IOException("Choose an image smaller than 20 MB.");
        using(var stream=File.OpenRead(path))
        using(var original=Image.FromStream(stream,false,true)) {
            if((long)original.Width*original.Height>40000000) throw new IOException("Choose an image smaller than 40 megapixels.");
            try { if(Array.IndexOf(original.PropertyIdList,0x112)>=0) { int orientation=BitConverter.ToUInt16(original.GetPropertyItem(0x112).Value,0); RotateFlipType[] rotations={RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipNone,RotateFlipType.RotateNoneFlipX,RotateFlipType.Rotate180FlipNone,RotateFlipType.Rotate180FlipX,RotateFlipType.Rotate90FlipX,RotateFlipType.Rotate90FlipNone,RotateFlipType.Rotate270FlipX,RotateFlipType.Rotate270FlipNone}; if(orientation>=1 && orientation<=8) original.RotateFlip(rotations[orientation]); } } catch(ArgumentException) {}
            double scale=Math.Min(1.0,1280.0/Math.Max(original.Width,original.Height));
            using(var resized=new Bitmap(Math.Max(1,(int)(original.Width*scale)),Math.Max(1,(int)(original.Height*scale)))) {
                using(var g=Graphics.FromImage(resized)) { g.Clear(Color.White); g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(original,new Rectangle(Point.Empty,resized.Size)); }
                using(var data=new MemoryStream()) { resized.Save(data,System.Drawing.Imaging.ImageFormat.Jpeg); byte[] jpeg=data.ToArray(); return new ImageAttachment {Name=Path.GetFileName(path),Jpeg=jpeg,DataUrl="data:image/jpeg;base64,"+Convert.ToBase64String(jpeg)}; }
            }
        }
    }
    public Dictionary<string,object> Message(string text) {
        var textPart=new Dictionary<string,object>{{"type","text"},{"text",text}};
        var imagePart=new Dictionary<string,object>{{"type","image_url"},{"image_url",new Dictionary<string,object>{{"url",DataUrl}}}};
        return new Dictionary<string,object>{{"role","user"},{"content",new object[]{textPart,imagePart}}};
    }
    public static string FindProjector(string model) {
        string stem=Regex.Replace(Path.GetFileNameWithoutExtension(model),"-(?:Q[0-9].*|IQ[0-9].*|F16|F32|BF16)$","",RegexOptions.IgnoreCase);
        string folder=Path.GetDirectoryName(model); if(!Directory.Exists(folder)) return "";
        var matches=new List<string>(); foreach(string file in Directory.GetFiles(folder,"*.gguf")) if(Path.GetFileName(file).StartsWith("mmproj-"+stem+"-",StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).Equals("mmproj-"+stem+".gguf",StringComparison.OrdinalIgnoreCase)) matches.Add(file);
        return matches.Count==1 ? matches[0] : "";
    }
}

sealed class ChatWindow : Form {
    readonly Engine engine=new Engine();
    readonly AccelerationPane acceleration=new AccelerationPane();
    readonly TextBox model=new TextBox { ReadOnly=true,Dock=DockStyle.Fill,AccessibleName="GGUF model path" };
    readonly TextBox projector=new TextBox { ReadOnly=true,Dock=DockStyle.Fill,AccessibleName="Optional vision projector path" };
    readonly PictureBox thumbnail=new PictureBox { Size=new Size(64,58),SizeMode=PictureBoxSizeMode.Zoom,AccessibleName="Attached image preview" };
    readonly Label attachmentInfo=new Label { Text="Pictures use your locked Read images slot.",AutoSize=true,Margin=new Padding(8,15,0,0) };
    readonly RichTextBox transcript=new RichTextBox { ReadOnly=true,Dock=DockStyle.Fill,BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle,DetectUrls=false,AccessibleName="Conversation" };
    readonly TextBox input=new TextBox { Multiline=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,AccessibleName="Your message" };
    readonly TextBox system=new TextBox { Dock=DockStyle.Fill,Text="You are a helpful assistant. Answer clearly in the language the user uses.",AccessibleName="System instruction" };
    readonly NumericUpDown threads=new NumericUpDown { Minimum=1,Maximum=64,Width=58,AccessibleName="CPU threads" };
    readonly ComboBox context=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=85,AccessibleName="Context tokens" };
    readonly NumericUpDown output=new NumericUpDown { Minimum=64,Maximum=4096,Increment=128,Value=1024,Width=75,AccessibleName="Maximum reply tokens" };
    readonly Label status=new Label { AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Text="Lock a Chat model on the left, then write and Send. Pictures use Read images." };
    readonly Button browse=Button("Choose model"), load=Button("Load"), unload=Button("Unload"), send=Button("Send"), stop=Button("Stop"), clear=Button("New chat"), save=Button("Save chat"), copy=Button("Copy chat"), diagnostics=Button("Memory / errors"),lowMemory=Button("Low RAM");
    readonly Button browseProjector=Button("Vision projector"),clearProjector=Button("Text only"),attach=Button("Attach image"),removeImage=Button("Remove image");
    readonly List<Dictionary<string,object>> history=new List<Dictionary<string,object>>();
    CancellationTokenSource active; bool closing,busy;
    ImageAttachment pendingImage;
    int completedImages;
    readonly ThemePane themes;
    readonly SkillsPane skills;
    readonly GenerationPane generation;
    readonly CodePane code;
    bool closeApproved;
    bool codeActive;
    bool generating;
    readonly ModelSlots slots;
    readonly ModelSlotsPane modelSlotsPane;
    ComboBox workChoice;
    string loadedRoleKey="";
    public void PreviewTab(int index) {
        string workspace=index==6?"Work on code":index==1?"Create pictures":"Chat / read a picture";
        if(workChoice.Items.Contains(workspace)) workChoice.SelectedItem=workspace;
    }
    string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BoltWorks","LlamaLite","settings.json"); } }
    static Button Button(string text) { return new Button { Text=text,AutoSize=true,Height=34,Padding=new Padding(6,2,6,2),FlatStyle=FlatStyle.System }; }
    static Label LabelFor(string text) { return new Label { Text=text,AutoSize=true,Margin=new Padding(10,9,3,0) }; }
    public ChatWindow() {
        Text="BoltWorks Local AI 0.6.19 — Your local AI tools"; ClientSize=new Size(1240,850); MinimumSize=new Size(1120,740); StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",11); BackColor=Color.FromArgb(242,245,241); AutoScaleMode=AutoScaleMode.Dpi;
        threads.Value=Math.Max(1,Math.Min(8,Environment.ProcessorCount/2)); context.Items.AddRange(new object[]{"1024","2048","4096","8192","16384"}); context.SelectedIndex=1; output.Value=256;
        context.SelectedIndexChanged+=delegate { output.Maximum=Math.Min(4096,int.Parse(context.Text)/2); };
        ReadSettings(); slots=new ModelSlots(Path.Combine(LocalFiles.Root,"model-slots.json"));slots.Seed("chat",model.Text,"");if(File.Exists(projector.Text))slots.Seed("vision",model.Text,projector.Text);
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=11 };
        foreach(int height in new int[]{46,44,44,44,34}) root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,68)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,86)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
        var title=new Label { Text="BoltWorks Local AI",Font=new Font("Segoe UI",18,FontStyle.Bold),Dock=DockStyle.Fill };
        root.Controls.Add(title,0,0);
        var picker=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=4,RowCount=1 };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); for(int i=0;i<3;i++) picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        picker.Controls.Add(model,0,0); picker.Controls.Add(browse,1,0); picker.Controls.Add(load,2,0); picker.Controls.Add(unload,3,0); root.Controls.Add(picker,0,1);
        var visionPicker=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1}; visionPicker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); visionPicker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); visionPicker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); visionPicker.Controls.Add(projector,0,0); visionPicker.Controls.Add(browseProjector,1,0); visionPicker.Controls.Add(clearProjector,2,0); root.Controls.Add(visionPicker,0,2);
        var settings=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false }; settings.Controls.AddRange(new Control[]{LabelFor("CPU threads"),threads,LabelFor("Context"),context,LabelFor("Reply limit"),output,lowMemory,diagnostics}); root.Controls.Add(settings,0,3);        var checkComputer=Button("Check this computer");

        checkComputer.Click+=delegate {
            if(busy||generating||codeActive){MessageBox.Show(this,"Finish or stop the active task before running the computer check.");return;}
            engine.Dispose();loadedRoleKey="";UpdateControls();
            using(var window=new ComputerCheckWindow(model.Text))window.ShowDialog(this);
            status.Text="Computer check finished. Lock your preferred model in the Chat slot.";
        };
        var instruction=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2 }; instruction.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); instruction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); instruction.Controls.Add(LabelFor("Instruction"),0,0); instruction.Controls.Add(system,1,0); root.Controls.Add(instruction,0,4);
        transcript.Margin=new Padding(3,8,3,8); root.Controls.Add(transcript,0,5);
        Button openText=Button("Open text"),pictureText=Button("Use text for picture");
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill }; actions.Controls.AddRange(new Control[]{clear,save,copy,openText,pictureText}); root.Controls.Add(actions,0,6);
        var images=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false}; images.Controls.AddRange(new Control[]{attach,removeImage,thumbnail,attachmentInfo}); root.Controls.Add(images,0,7);
        root.Controls.Add(input,0,8);
        var bottom=new FlowLayoutPanel { Dock=DockStyle.Fill }; bottom.Controls.AddRange(new Control[]{send,stop,new Label { Text="Offline · CPU default · CUDA optional · No account",AutoSize=true,Margin=new Padding(16,9,0,0) }}); root.Controls.Add(bottom,0,9);
        root.Controls.Add(status,0,10);
        themes=new ThemePane(); skills=new SkillsPane();
        generation=new GenerationPane(themes,delegate {if(busy || generating) {ShowError(new Exception("Stop or finish the current chat request before generating a picture.")); return false;} engine.Dispose(); status.Text="Chat model unloaded to make room for picture generation."; UpdateControls(); return true;},delegate(bool value) {generating=value; if(!closing) {root.Enabled=!value; themes.Enabled=!value; UpdateControls();}},async delegate(string prompt,CancellationToken cancel) {return await RunModelTask(new ModelTask {System="Rewrite the user's request as one concise English image-generation prompt. Preserve EVERY requested color, subject, number, body type and relationship. A sedan is a passenger car with a separate engine hood, enclosed cabin and separate trunk. Do not change blue to red, do not add a starting image, and do not claim an image was made. Return only the prompt.",Prompt=prompt,MaxTokens=256},cancel);});
        code=new CodePane(async delegate(List<Dictionary<string,object>> messages,CancellationToken cancel) {int count=await EnsureRole("chat",cancel,acceleration.Memory==MemoryMode.Normal?16384:4096);int reply=Math.Min(3072,count/4);var fitted=await CodeContext.Fit(messages,count,reply,engine.PromptTokens,cancel);return await engine.Chat(fitted,reply,s=>{},cancel,CodeAgent.Format);},delegate {if(busy||generating||codeActive)return false;try{slots.Require("chat");}catch(Exception e){ShowError(e);return false;}codeActive=true;root.Enabled=generation.Enabled=themes.Enabled=skills.Enabled=false;UpdateControls();return true;},delegate {engine.Dispose();codeActive=false;if(!closing){root.Enabled=generation.Enabled=themes.Enabled=skills.Enabled=true;UpdateControls();}},delegate {engine.Dispose();UpdateControls();});
        slots.Seed("pictures",generation.CurrentModel,"");modelSlotsPane=new ModelSlotsPane(slots,acceleration,checkComputer);
        acceleration.Changed+=delegate {engine.Dispose();loadedRoleKey="";engine.Backend=acceleration.Backend;engine.Memory=acceleration.Memory;engine.GpuLayers=acceleration.GpuLayers;status.Text="Processing: "+acceleration.Backend+". Memory mode: "+acceleration.Memory+". Changes apply to the next model load.";UpdateControls();};
        engine.Memory=acceleration.Memory;
        generation.UseAssignedModel(delegate {return slots.Require("pictures").File;},slots.Items["pictures"].File);
        modelSlotsPane.Changed+=delegate {generation.RefreshAssignedModel(slots.Items["pictures"].File);UpdateControls();};
        picker.Visible=visionPicker.Visible=settings.Visible=instruction.Visible=false;for(int i=1;i<=4;i++)root.RowStyles[i].Height=0;
        title.Text="Chat — write a message or attach a picture";
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,340));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.Controls.Add(modelSlotsPane,0,0);
        var workspace=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,48));workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.Controls.Add(workspace,1,0);
        var navigation=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(8,5,0,0)};workChoice=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=185};
        workChoice.Items.AddRange(new object[]{"Chat / read a picture","Create pictures","Work on code"});
        Button themeButton=Button("Themes"),skillButton=Button("Skills"),optionsButton=Button("Chat options");navigation.Controls.AddRange(new Control[]{workChoice,themeButton,skillButton,optionsButton});workspace.Controls.Add(navigation,0,0);
        var tasks=new Panel {Dock=DockStyle.Fill};tasks.Controls.AddRange(new Control[]{root,generation,code});workspace.Controls.Add(tasks,0,1);
        workChoice.SelectedIndexChanged+=delegate {string selection=workChoice.SelectedItem as string;root.Visible=selection=="Chat / read a picture";generation.Visible=selection=="Create pictures";code.Visible=selection=="Work on code";};workChoice.SelectedIndex=0;Controls.Add(layout);
        themeButton.Click+=delegate {ShowOptions("Saved themes",themes);};skillButton.Click+=delegate {ShowOptions("Task skills",skills);};
        optionsButton.Click+=delegate {var panel=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};panel.RowStyles.Add(new RowStyle(SizeType.Absolute,65));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,55));root.Controls.Remove(settings);root.Controls.Remove(instruction);settings.Visible=instruction.Visible=true;panel.Controls.Add(settings,0,0);panel.Controls.Add(instruction,0,1);ShowOptions("Chat options",panel);panel.Controls.Remove(settings);panel.Controls.Remove(instruction);};
        openText.Click+=delegate {using(var dialog=new OpenFileDialog {Filter="Text files (*.txt)|*.txt"}) if(dialog.ShowDialog(this)==DialogResult.OK) try {if(new FileInfo(dialog.FileName).Length>128*1024) throw new IOException("Choose a text file smaller than 128 KB."); string text=File.ReadAllText(dialog.FileName,new UTF8Encoding(false,true)); if(text.Length>32000 || text.IndexOf('\0')>=0) throw new IOException("Choose plain text up to 32,000 characters."); input.Text=text;} catch(Exception e) {ShowError(e);} };
        pictureText.Click+=delegate {string text=input.Text.Trim(); if(text.Length==0 && history.Count>0) text=history[history.Count-1]["content"] as string; if(string.IsNullOrWhiteSpace(text)) {ShowError(new Exception("Type a picture description or get a chat reply first.")); return;} generation.CurrentText=text; workChoice.SelectedIndex=1;};
        browse.Click+=delegate { using(var dialog=new OpenFileDialog { Filter="GGUF models (*.gguf)|*.gguf",Title="Choose a local model" }) if(dialog.ShowDialog(this)==DialogResult.OK) { model.Text=dialog.FileName; projector.Text=ImageAttachment.FindProjector(model.Text); SaveSettings(); } };
        browseProjector.Click+=delegate { using(var dialog=new OpenFileDialog {Filter="GGUF projectors (*.gguf)|*.gguf",Title="Choose the matching mmproj file"}) if(dialog.ShowDialog(this)==DialogResult.OK) {projector.Text=dialog.FileName; SaveSettings();} };
        clearProjector.Click+=delegate {projector.Clear(); SetImage(null); SaveSettings();};
        attach.Click+=delegate { using(var dialog=new OpenFileDialog {Filter="Pictures (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",Title="Attach a local picture for this message"}) if(dialog.ShowDialog(this)==DialogResult.OK) try {SetImage(ImageAttachment.Load(dialog.FileName));} catch(Exception e) {ShowError(e);} };
        removeImage.Click+=delegate {SetImage(null);};
        load.Click+=async delegate { await LoadModel(); };
        unload.Click+=delegate { engine.Dispose(); status.Text="Model unloaded. Memory released."; UpdateControls(); };
        send.Click+=async delegate { await SendMessage(); };
        stop.Click+=delegate { if(active!=null) active.Cancel(); engine.Dispose(); status.Text="Stopped. Model unloaded to release memory."; };
        clear.Click+=delegate { history.Clear(); completedImages=0; SetImage(null); transcript.Clear(); status.Text="New conversation. Text uses Chat; attached pictures use Read images. Models load automatically."; };
        save.Click+=delegate { using(var dialog=new SaveFileDialog { Filter="Text file (*.txt)|*.txt",FileName="chat.txt" }) if(dialog.ShowDialog(this)==DialogResult.OK) try { File.WriteAllText(dialog.FileName,transcript.Text,Encoding.UTF8); } catch(Exception e) { ShowError(e); } };
        copy.Click+=delegate { try { if(transcript.TextLength>0) Clipboard.SetText(transcript.Text); } catch(Exception e) { ShowError(e); } };
        diagnostics.Click+=delegate {MemoryReport.Show(this,"Model: "+MemoryReport.Model(model.Text)+"\r\nProjector: "+MemoryReport.Model(projector.Text)+"\r\nContext: "+context.Text+"; batch 128; micro-batch 64",engine.Diagnostics);};
        lowMemory.Click+=delegate {if(busy || engine.Alive) {ShowError(new Exception("Stop or unload the model first, then choose Low RAM.")); return;} context.SelectedItem="2048"; output.Value=256; SaveSettings(); status.Text="Low RAM settings: context 2048, reply 256; processing batches are reduced for every model.";};
        input.KeyDown+=async delegate(object sender,KeyEventArgs e) { if(e.Control && e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; if(!busy) await SendMessage(); } };
        FormClosing+=async delegate(object sender,FormClosingEventArgs e) {if(closeApproved) return; e.Cancel=true; if(closing) return; closing=true; if(active!=null) active.Cancel(); engine.Dispose(); generation.Shutdown(); try {await code.Shutdown();} catch {} if(thumbnail.Image!=null) thumbnail.Image.Dispose(); SaveSettings(); closeApproved=true; Close();};
        UpdateControls();
    }
    void ShowOptions(string title,Control pane) {using(var window=new Form {Text=title,Size=new Size(800,580),StartPosition=FormStartPosition.CenterParent,Font=Font}) {window.Controls.Add(pane);window.ShowDialog(this);window.Controls.Remove(pane);}}
    void ReadSettings() { try { if(!File.Exists(SettingsPath)) return; var settings=Engine.Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(SettingsPath)); string v; if(settings.TryGetValue("model",out v)) model.Text=v; if(settings.TryGetValue("projector",out v)) projector.Text=v; if(settings.TryGetValue("threads",out v)) threads.Value=Math.Max(1,Math.Min(64,int.Parse(v))); if(settings.TryGetValue("context",out v) && context.Items.Contains(v)) context.SelectedItem=v; } catch {} }
    void SaveSettings() { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)); File.WriteAllText(SettingsPath,Engine.Json.Serialize(new Dictionary<string,string>{{"model",model.Text},{"projector",projector.Text},{"threads",threads.Value.ToString()},{"context",context.Text}})); } catch {} }
    void SetImage(ImageAttachment value) {
        pendingImage=value;
        if(thumbnail.Image!=null) {thumbnail.Image.Dispose(); thumbnail.Image=null;}
        if(value!=null) {using(var stream=new MemoryStream(value.Jpeg)) using(var picture=Image.FromStream(stream)) thumbnail.Image=new Bitmap(picture);}
        attachmentInfo.Text=value==null ? "Pictures use your locked Read images slot." : value.Name+" · Sent only when you press Send";
        UpdateControls();
    }
    void UpdateControls() { if(closing) return; acceleration.Enabled=!busy&&!generating&&!codeActive; browse.Enabled=browseProjector.Enabled=clearProjector.Enabled=load.Enabled=!busy && !generating && !engine.Alive; unload.Enabled=!busy && engine.Alive; send.Enabled=!busy && !generating && !codeActive; attach.Enabled=!busy && !generating && !codeActive && completedImages<4; removeImage.Enabled=!busy && pendingImage!=null; stop.Enabled=busy; clear.Enabled=!busy; system.Enabled=!busy; threads.Enabled=context.Enabled=!busy && !engine.Alive; output.Enabled=!busy;if(modelSlotsPane!=null) {modelSlotsPane.Enabled=!busy&&!generating&&!codeActive;if(generating)modelSlotsPane.SetRunning("pictures");else if(!engine.Alive)modelSlotsPane.SetRunning(null);}if(workChoice!=null)workChoice.Parent.Enabled=!busy&&!generating&&!codeActive; }
    void ShowError(Exception e) {var assigned=new StringBuilder();if(slots!=null) foreach(string role in ModelSlots.Roles) {var item=slots.Items[role];assigned.AppendLine(ModelSlots.Title(role)+": "+MemoryReport.Model(item.File)+"; locked="+item.Locked);if(role=="vision")assigned.AppendLine("Picture-reading file: "+MemoryReport.Model(item.Projector));} MemoryReport.Record("Chat / model operation",e,assigned+"\r\nRuntime log:\r\n"+engine.Diagnostics); if(!closing) MessageBox.Show(this,e.Message+"\r\n\r\nFor technical details: Chat options → Memory / errors → Copy report.","BoltWorks Local AI",MessageBoxButtons.OK,MessageBoxIcon.Information); }
    async Task<string> RunModelTask(ModelTask task,CancellationToken cancel) {
        if(busy || generating || codeActive) throw new IOException("Finish or stop the current job first.");
        busy=true; active=CancellationTokenSource.CreateLinkedTokenSource(cancel); UpdateControls();
        try {
            int count=await EnsureRole("chat",active.Token);
            return await engine.Chat(task.Messages(),Math.Min(task.MaxTokens,count/2),s=>{},active.Token);
        } finally {busy=false; active.Dispose(); active=null; UpdateControls();}
    }
    async Task<int> EnsureRole(string role,CancellationToken cancel,int minimum=0) {
        var choice=slots.Require(role);string vision=role=="vision"?choice.Projector:"";
        int count=ModelMemory.Context(acceleration.Memory,Math.Max(minimum,role=="chat"?int.Parse(context.Text):Math.Max(4096,int.Parse(context.Text))));
        string key=choice.File+"\n"+vision+"\n"+count;
        if(engine.Ready && loadedRoleKey==key) {modelSlotsPane.SetRunning(role);return count;}
        engine.Dispose();loadedRoleKey="";status.Text="Loading "+ModelSlots.Title(role)+" model: "+Path.GetFileName(choice.File)+"…";
        modelSlotsPane.SetRunning(role);
        await engine.Start(choice.File,vision,(int)threads.Value,count,cancel);
        loadedRoleKey=key;return count;
    }
    async Task LoadModel() {
        if(busy || generating) return;
        if(string.IsNullOrWhiteSpace(projector.Text) && (completedImages>0 || pendingImage!=null)) {ShowError(new Exception("This chat contains pictures. Save it if needed, then choose New chat before loading a text-only model.")); return;}
        busy=true; active=new CancellationTokenSource(); UpdateControls(); status.Text="Loading model locally… Stop cancels loading.";
        try { await engine.Start(model.Text,projector.Text,(int)threads.Value,int.Parse(context.Text),active.Token); status.Text="Ready · "+(engine.VisionEnabled?"Chat + vision · ":"Text only · ")+Path.GetFileName(model.Text); SaveSettings(); }
        catch(OperationCanceledException) { status.Text="Stopped. Model unloaded."; }
        catch(Exception e) { status.Text="Could not load the model."; ShowError(e); }
        finally { busy=false; active.Dispose(); active=null; UpdateControls(); }
    }
    async Task SendMessage() {
        string text=input.Text.Trim(); if(busy || generating || codeActive || text.Length==0) return;
        string role=pendingImage!=null||completedImages>0?"vision":"chat";
        try {slots.Require(role);} catch(Exception e) {ShowError(e);return;}
        if(text.Length>32000) { ShowError(new Exception("Please shorten your message to 32,000 characters or less.")); return; }
        busy=true; active=new CancellationTokenSource(); UpdateControls(); input.Clear();
        var sentImage=pendingImage;
        transcript.AppendText("You\r\n"+text+(sentImage==null?"":"\r\n[Attached image: "+sentImage.Name+"]")+"\r\n\r\nAssistant\r\n");
        var userMessage=sentImage==null ? Message("user",text) : sentImage.Message(text);
        var messages=new List<Dictionary<string,object>>(); messages.Add(Message("system",themes.ChatInstruction(skills.Apply(system.Text)))); messages.AddRange(history); messages.Add(userMessage);
        var watch=Stopwatch.StartNew(); status.Text="Thinking locally…";
        try {
            await EnsureRole(role,active.Token);
            string answer=await engine.Chat(messages,(int)output.Value,delegate(string part) { if(!closing) { transcript.AppendText(part); transcript.SelectionStart=transcript.TextLength; transcript.ScrollToCaret(); } },active.Token);
            history.Add(userMessage); history.Add(Message("assistant",answer));
            if(sentImage!=null) {completedImages++; SetImage(null);}
            status.Text="Ready · "+watch.Elapsed.TotalSeconds.ToString("0.0")+" seconds";
        } catch(Exception e) { if(!closing) { transcript.AppendText(active.IsCancellationRequested?"\r\n[Stopped; partial reply was not added to model history.]":"\r\n[Reply failed; this exchange was not added to model history.]"); if(!active.IsCancellationRequested) { input.Text=text; status.Text="Reply failed. Your message is restored below."; ShowError(e); } } }
        finally { if(!closing) transcript.AppendText("\r\n\r\n"); busy=false; active.Dispose(); active=null; UpdateControls(); }
    }
    internal static Dictionary<string,object> Message(string role,string text) { return new Dictionary<string,object>{{"role",role},{"content",text}}; }
}

static class Smoke {
    public static string ProgressPath;
    static void Progress(string text) { if(ProgressPath!=null) File.AppendAllText(ProgressPath,"\r\n"+text); }
    public static async Task Run(string model) {
        int pid;
        string invalid=Path.Combine(Path.GetTempPath(),"LlamaLite-invalid-"+Guid.NewGuid().ToString("N")+".gguf");
        try {
            File.WriteAllText(invalid,"This is not a model"); bool rejected=false;
            using(var engine=new Engine()) { try { await engine.Start(invalid,2,2048,CancellationToken.None); } catch(IOException) { rejected=true; } if(engine.Alive) throw new Exception("Invalid model launched a runtime."); }
            if(!rejected) throw new Exception("Invalid model was accepted.");
        } finally { File.Delete(invalid); }
        using(var engine=new Engine()) {
            using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(3))) {
                Progress("Loading model..."); await engine.Start(model,2,2048,cancel.Token); pid=engine.ProcessId; Progress("Model ready. Testing chat...");
                var messages=new List<Dictionary<string,object>> {ChatWindow.Message("system","Be concise."),ChatWindow.Message("user","Säg hej på svenska. Use the word hej.")};
                var streamed=new StringBuilder(); string result=await engine.Chat(messages,80,s=>streamed.Append(s),cancel.Token);
                if(string.IsNullOrWhiteSpace(result) || streamed.ToString()!=result) throw new Exception("Streaming reply mismatch.");
                Progress("Streamed: "+result);
                Console.WriteLine(result);
                using(var stopped=new CancellationTokenSource()) { stopped.Cancel(); bool cancelled=false; try { await engine.Chat(messages,80,s=>{},stopped.Token); } catch(OperationCanceledException) { cancelled=true; } if(!cancelled) throw new Exception("Cancellation not respected."); }
                using(var stopped=new CancellationTokenSource(TimeSpan.FromSeconds(60))) {
                    bool received=false,cancelled=false;
                    try { await engine.Chat(new List<Dictionary<string,object>> {ChatWindow.Message("user","Write a detailed story about a forest, at least 500 words.")},1024,s=> { received=true; stopped.Cancel(); },stopped.Token); } catch(OperationCanceledException) { cancelled=true; }
                    if(!received || !cancelled) throw new Exception("Cancellation during streaming did not work.");
                }
            }
        }
        try { using(var p=Process.GetProcessById(pid)) { if(!p.HasExited) throw new Exception("Runtime remained alive after unload."); } } catch(ArgumentException) {}
    }
}

static class VisionSmoke {
    public static async Task Run(string model,string projector,string image,string report) {
        using(var engine=new Engine())
        using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(5))) {
            var picture=ImageAttachment.Load(image);
            if(ImageAttachment.FindProjector(model)!=projector) throw new Exception("Matching Swift projector was not discovered.");
            try {
                await engine.Start(model,projector,8,2048,cancel.Token);
                File.AppendAllText(report,"\r\nModel and projector ready. Asking about synthetic test image...");
                if(!engine.VisionEnabled) throw new Exception("Vision was not enabled.");
                var messages=new List<Dictionary<string,object>> {ChatWindow.Message("system","Describe images accurately. Be concise."),picture.Message("What shape and color are in the center, and what color is the background? Answer in one short English sentence.")};
                string answer=await engine.Chat(messages,80,s=>{},cancel.Token);
                File.AppendAllText(report,"\r\nReply: "+answer);
                string lower=answer.ToLowerInvariant();
                if(!lower.Contains("red") || !lower.Contains("circle") || !lower.Contains("blue")) throw new Exception("The model did not identify the synthetic image correctly.");
                File.AppendAllText(report,"\r\nPASS: exact local Swift model + matching projector read a synthetic image with streaming output; runtime unloaded.");
            } catch { File.AppendAllText(report,"\r\nRuntime diagnostics:\r\n"+engine.Diagnostics); throw; }
        }
    }
}
}
