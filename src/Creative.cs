using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
static class LocalFiles {
    public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BoltWorks","LlamaLite"); } }
    public static void SaveJson(string path,object value) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); string pending=path+".new";
        File.WriteAllText(pending,Engine.Json.Serialize(value),new UTF8Encoding(false));
        if(File.Exists(path)) File.Replace(pending,path,path+".bak"); else File.Move(pending,path);
    }
    public static string Quote(string value) {
        // Windows CommandLineToArgvW quoting, including quotes and terminal backslashes.
        var result=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) { if(c=='\\') {slashes++; continue;} if(c=='"') {result.Append('\\',slashes*2+1); result.Append(c);} else {result.Append('\\',slashes); result.Append(c);} slashes=0; }
        result.Append('\\',slashes*2); return result.Append('"').ToString();
    }
}
public sealed class ThemeProfile {
    public string Name {get;set;}
    public string Style {get;set;}
    public string Avoid {get;set;}
    public override string ToString() {return Name;}
}
public sealed class ThemeData {
    public int Version {get;set;}
    public bool Enabled {get;set;}
    public string Selected {get;set;}
    public List<ThemeProfile> Profiles {get;set;}
}
static class ThemeRules {
    public static string Chat(ThemeProfile theme,string instruction) {return theme==null ? instruction : instruction+"\nSaved creative theme preferences (apply when relevant):\n"+theme.Style+"\nThings to avoid: "+theme.Avoid;}
    public static string Picture(ThemeProfile theme,string prompt) {return theme==null || string.IsNullOrWhiteSpace(theme.Style) ? prompt : prompt+", "+theme.Style;}
}
sealed class ThemePane : UserControl {
    readonly ComboBox saved=new ComboBox {Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Saved themes"};
    readonly TextBox name=new TextBox {Dock=DockStyle.Fill,AccessibleName="Theme name"};
    readonly TextBox style=new TextBox {Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,AccessibleName="Theme style and characters"};
    readonly TextBox avoid=new TextBox {Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,AccessibleName="Things to avoid"};
    readonly CheckBox enabled=new CheckBox {Text="Apply saved theme to chat and pictures",AutoSize=true,Checked=true};
    readonly Label info=new Label {Dock=DockStyle.Fill,AutoSize=false};
    readonly Button save=new Button {Text="Save theme",AutoSize=true};
    ThemeData data; bool loading,writable=true;
    string FilePath {get {return Path.Combine(LocalFiles.Root,"themes.json");}}
    public ThemePane() {
        Dock=DockStyle.Fill; Padding=new Padding(18);
        var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=10};
        foreach(int h in new int[]{36,36,34,32}) grid.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent,70)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,32)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,30)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,48)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,85)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        grid.Controls.Add(new Label {Text="Saved theme memory",Font=new Font("Segoe UI",17,FontStyle.Bold),Dock=DockStyle.Fill},0,0);
        grid.Controls.Add(saved,0,1); grid.Controls.Add(name,0,2); grid.Controls.Add(new Label {Text="Style, colors, recurring characters, clothing and setting",Dock=DockStyle.Fill},0,3); grid.Controls.Add(style,0,4); grid.Controls.Add(new Label {Text="Things to avoid (Turbo ignores negative prompts)",Dock=DockStyle.Fill},0,5); grid.Controls.Add(avoid,0,6);
        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill}; var fresh=new Button {Text="New theme",AutoSize=true}; actions.Controls.AddRange(new Control[]{save,fresh,enabled}); grid.Controls.Add(actions,0,7);
        grid.Controls.Add(new Label {Text="Save a theme before using it. Selecting a saved theme applies its saved version. Themes stay on this computer after closing the app; they are prompt notes, not model training. Image-to-image can help reuse a picture, but neither it nor a fixed seed guarantees identical characters.",Dock=DockStyle.Fill},0,8); grid.Controls.Add(info,0,9); Controls.Add(grid);
        data=new ThemeData {Version=1,Enabled=true,Profiles=new List<ThemeProfile>()};
        try {if(File.Exists(FilePath)) {data=Engine.Json.Deserialize<ThemeData>(File.ReadAllText(FilePath)); if(data==null || data.Version!=1 || data.Profiles==null) throw new IOException("Unsupported theme file.");}} catch(Exception e) {writable=false; info.Text="Could not read themes. Existing file preserved: "+e.Message; data=new ThemeData {Version=1,Profiles=new List<ThemeProfile>()};}
        loading=true; foreach(var profile in data.Profiles) saved.Items.Add(profile); enabled.Checked=data.Enabled; foreach(ThemeProfile profile in saved.Items) if(profile.Name==data.Selected) saved.SelectedItem=profile; loading=false; Display(); save.Enabled=writable;
        saved.SelectedIndexChanged+=delegate {Display(); if(!loading) {data.Selected=saved.SelectedItem==null?null:((ThemeProfile)saved.SelectedItem).Name; Persist();}};
        enabled.CheckedChanged+=delegate {if(!loading) {data.Enabled=enabled.Checked; Persist();}};
        fresh.Click+=delegate {saved.SelectedIndex=-1; name.Clear(); style.Clear(); avoid.Clear(); name.Focus();};
        save.Click+=delegate {
            string title=name.Text.Trim(); if(title.Length==0 || title.Length>80 || style.Text.Length>5000 || avoid.Text.Length>2000) {info.Text="Use a name (1–80 characters), style up to 5,000, and avoid list up to 2,000."; return;}
            var profile=new ThemeProfile {Name=title,Style=style.Text.Trim(),Avoid=avoid.Text.Trim()};
            int index=data.Profiles.FindIndex(p=>string.Equals(p.Name,title,StringComparison.OrdinalIgnoreCase));
            if(index>=0) data.Profiles[index]=profile; else data.Profiles.Add(profile);
            data.Selected=title; data.Enabled=enabled.Checked;
            if(Persist()) {loading=true; saved.Items.Clear(); foreach(var p in data.Profiles) saved.Items.Add(p); saved.SelectedItem=profile; loading=false; info.Text="Saved locally. Active theme: "+title;}
        };
    }
    void Display() {var p=saved.SelectedItem as ThemeProfile; if(p!=null) {name.Text=p.Name; style.Text=p.Style; avoid.Text=p.Avoid;} }
    bool Persist() {if(!writable) return false; try {LocalFiles.SaveJson(FilePath,data); return true;} catch(Exception e) {info.Text="Could not save: "+e.Message; return false;} }
    public ThemeProfile Active { get {return data.Enabled ? data.Profiles.Find(p=>p.Name==data.Selected) : null;} }
    public string ChatInstruction(string instruction) {return ThemeRules.Chat(Active,instruction);}
    public string ImagePrompt(string prompt) {return ThemeRules.Picture(Active,prompt);}
}

sealed class GenerationRequest {
    public string Model,Prompt,Negative,Backend,OutputFolder,ReferenceData,Preset;
    public int Steps=4,Threads=4,Size=512,Seed=42;
    public double Guidance=1,Strength=0.65;
}
sealed class PictureEngine : IDisposable {
    Process process; ProcessJob job;
    public async Task<string> Generate(GenerationRequest request,IProgress<string> progress,CancellationToken cancel) {
        if(!File.Exists(request.Model)) throw new FileNotFoundException("Choose an existing SD 1.x or SDXL all-in-one checkpoint first.");
        if(request.Preset!=null) GenerationPresets.Resolve(request.Model,request.Preset);
        string extension=Path.GetExtension(request.Model); if(!extension.Equals(".safetensors",StringComparison.OrdinalIgnoreCase) && !extension.Equals(".gguf",StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a safetensors or compatible GGUF checkpoint. Python pickle .ckpt files are not accepted.");
        if(request.Size<256 || request.Size>768 || request.Size%64!=0 || request.Steps<1 || request.Steps>50 || request.Prompt.Length>12000) throw new IOException("Invalid generation settings.");
        string runtime=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"image-runtime","sd-cli.exe"); if(!File.Exists(runtime)) throw new FileNotFoundException("The image-runtime folder is missing. Extract the full application ZIP.");
        RuntimePrerequisites.Check(runtime);
        Directory.CreateDirectory(request.OutputFolder);
        string id=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
        string output=Path.Combine(request.OutputFolder,id+".png");
        string temp=Path.Combine(Path.GetTempPath(),"BoltWorks-LlamaLite-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try {
            string promptFile=Path.Combine(temp,"prompt.txt"),negativeFile=Path.Combine(temp,"negative.txt");
            File.WriteAllText(promptFile,request.Prompt,new UTF8Encoding(false)); File.WriteAllText(negativeFile,request.Negative??"",new UTF8Encoding(false));
            string args="-m "+LocalFiles.Quote(request.Model)+" --prompt-file "+LocalFiles.Quote(promptFile)+" --negative-prompt-file "+LocalFiles.Quote(negativeFile)+" -o "+LocalFiles.Quote(output)+" --backend "+LocalFiles.Quote(request.Backend)+" --steps "+request.Steps+" --cfg-scale "+request.Guidance.ToString(CultureInfo.InvariantCulture)+" --sampling-method euler_a -W "+request.Size+" -H "+request.Size+" -t "+request.Threads+" -s "+request.Seed+" --vae-tiling --vae-tile-size 128 --batched-cfg off --mmap";
            if(request.ReferenceData!=null) {string reference=Path.Combine(temp,"reference.jpg"); File.WriteAllBytes(reference,Convert.FromBase64String(request.ReferenceData)); args+=" --init-img "+LocalFiles.Quote(reference)+" --strength "+request.Strength.ToString(CultureInfo.InvariantCulture);}
            var log=new StringBuilder();
            var start=new ProcessStartInfo(runtime,args) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(runtime),RedirectStandardOutput=true,RedirectStandardError=true};
            var inherited=new List<string>(); foreach(string key in start.EnvironmentVariables.Keys) if(key.StartsWith("SD_",StringComparison.OrdinalIgnoreCase) || key.StartsWith("GGML_RPC",StringComparison.OrdinalIgnoreCase)) inherited.Add(key); foreach(string key in inherited) start.EnvironmentVariables.Remove(key);
            process=new Process {StartInfo=start};
            DataReceivedEventHandler receive=delegate(object sender,DataReceivedEventArgs e) {if(e.Data!=null) {lock(log) {log.AppendLine(e.Data); if(log.Length>16000) log.Remove(0,log.Length-16000);} if(progress!=null) progress.Report(e.Data);}};
            process.OutputDataReceived+=receive; process.ErrorDataReceived+=receive;
            cancel.ThrowIfCancellationRequested(); process.Start(); job=new ProcessJob(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using(cancel.Register(delegate {try {if(!process.HasExited) process.Kill();} catch {}})) {await Task.Run(delegate {process.WaitForExit();});}
            cancel.ThrowIfCancellationRequested();
            if(process.ExitCode!=0 || !File.Exists(output)) {string details; lock(log) details=log.ToString(); throw new IOException("Image generation failed. Try CPU if GPU memory is insufficient.\r\n"+details);}
            using(var test=Image.FromFile(output)) if(test.Width!=request.Size || test.Height!=request.Size) throw new IOException("The generator produced an unexpected image size.");
            LocalFiles.SaveJson(Path.ChangeExtension(output,".json"),new {version=1,prompt=request.Prompt,negative=request.Negative,model=request.Model,backend=request.Backend,steps=request.Steps,guidance=request.Guidance,seed=request.Seed,size=request.Size,referenceUsed=request.ReferenceData!=null});
            return output;
        } finally {Dispose(); try {Directory.Delete(temp,true);} catch {} }
    }
    public void Dispose() {if(process!=null) {try {if(!process.HasExited) {process.Kill(); process.WaitForExit(3000);}} catch {}} if(job!=null) {job.Dispose(); job=null;} if(process!=null) {process.Dispose(); process=null;} }
}

sealed class GenerationPane : UserControl {
    readonly ThemePane themes;
    readonly Func<bool> releaseChat;
    readonly Action<bool> setGenerating;
    readonly Func<string,CancellationToken,Task<string>> improvePrompt;
    readonly TextBox model=new TextBox {ReadOnly=true,Dock=DockStyle.Fill,AccessibleName="Image generation model"};
    readonly TextBox prompt=new TextBox {Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,AccessibleName="Picture description"};
    readonly TextBox effective=new TextBox {ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,AccessibleName="Prompt with saved theme"};
    readonly ComboBox preset=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=175};
    readonly ComboBox backend=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=180};
    readonly ComboBox size=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=80};
    readonly NumericUpDown seed=new NumericUpDown {Minimum=-1,Maximum=int.MaxValue,Value=42,Width=95};
    readonly PictureBox preview=new PictureBox {Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.White};
    readonly Label status=new Label {Dock=DockStyle.Fill,Text="Choose an image model. Chat unloads before generation."};
    readonly Button generate=new Button {Text="Generate picture",AutoSize=true},stop=new Button {Text="Stop",AutoSize=true,Enabled=false},save=new Button {Text="Save picture",AutoSize=true,Enabled=false};
    readonly List<Control> settings=new List<Control>();
    CancellationTokenSource active; PictureEngine engine; string result; ImageAttachment reference; bool closed;
    public string CurrentText {set {prompt.Text=value;}}
    Func<string> assignedModel;
    Button modelBrowse;
    public string CurrentModel {get {return model.Text;}}
    public void UseAssignedModel(Func<string> source,string path) {assignedModel=source;model.Text=path??"";if(modelBrowse!=null)modelBrowse.Visible=false;status.Text="Uses the locked Create pictures model on the left.";}
    public void RefreshAssignedModel(string path) {model.Text=path??"";}
    string SettingsPath {get {return Path.Combine(LocalFiles.Root,"generation.json");}}
    public GenerationPane(ThemePane themes,Func<bool> releaseChat,Action<bool> setGenerating,Func<string,CancellationToken,Task<string>> improvePrompt) {
        this.themes=themes; this.releaseChat=releaseChat; this.setGenerating=setGenerating; this.improvePrompt=improvePrompt; Dock=DockStyle.Fill; Padding=new Padding(16);
        var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=10}; foreach(int h in new int[]{38,42,42,32}) grid.RowStyles.Add(new RowStyle(SizeType.Absolute,h)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,85)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,58)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,46)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,42)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        grid.Controls.Add(new Label {Text="Create pictures locally",Font=new Font("Segoe UI",17,FontStyle.Bold),Dock=DockStyle.Fill},0,0);
        var picker=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2}; picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); var browse=new Button {Text="Choose image model",AutoSize=true}; modelBrowse=browse; picker.Controls.Add(model,0,0); picker.Controls.Add(browse,1,0); grid.Controls.Add(picker,0,1);
        preset.Items.AddRange(new object[]{"SDXL Turbo (4 steps)","SD 1.x (20 steps)","SDXL (20 steps)"}); preset.SelectedIndex=0; backend.Items.AddRange(new object[]{"CPU","GPU via Vulkan (first GPU)"}); backend.SelectedIndex=0; size.Items.AddRange(new object[]{"384","512","768"}); size.SelectedIndex=1;
        var options=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false}; options.Controls.AddRange(new Control[]{preset,backend,new Label {Text="Size",AutoSize=true},size,new Label {Text="Seed",AutoSize=true},seed}); grid.Controls.Add(options,0,2);
        grid.Controls.Add(new Label {Text="Describe the picture (English usually works best). Saved theme is added automatically.",Dock=DockStyle.Fill},0,3); grid.Controls.Add(prompt,0,4); grid.Controls.Add(effective,0,5);
        var referenceRow=new FlowLayoutPanel {Dock=DockStyle.Fill}; var chooseReference=new Button {Text="Starting image",AutoSize=true}; var removeReference=new Button {Text="Remove starting image",AutoSize=true}; var reuse=new Button {Text="Reuse result",AutoSize=true}; var refLabel=new Label {AutoSize=true,Text="Optional img2img",Margin=new Padding(8,8,0,0)}; referenceRow.Controls.AddRange(new Control[]{chooseReference,removeReference,reuse,refLabel}); grid.Controls.Add(referenceRow,0,6);
        grid.Controls.Add(preview,0,7); var actions=new FlowLayoutPanel {Dock=DockStyle.Fill}; var report=new Button {Text="Memory / errors",AutoSize=true}; var improve=new Button {Text="Improve prompt with chat",AutoSize=true}; report.Click+=delegate {MemoryReport.Show(this,"Image model: "+MemoryReport.Model(model.Text)+"\r\nBackend: "+backend.Text+"; size: "+size.Text+"; VAE tiles 128; sequential guidance","");}; actions.Controls.AddRange(new Control[]{generate,improve,stop,save,report}); grid.Controls.Add(actions,0,8); grid.Controls.Add(status,0,9); Controls.Add(grid);
        settings.AddRange(new Control[]{browse,prompt,preset,backend,size,seed,chooseReference,removeReference,reuse});
        string existing=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Comfy-Desktop","ComfyUI-Shared","models","checkpoints","sd_xl_turbo_1.0_fp16.safetensors"); if(File.Exists(existing)) model.Text=existing;
        try {if(File.Exists(SettingsPath)) {var stored=Engine.Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(SettingsPath)); string v; if(stored.TryGetValue("model",out v)) model.Text=v; if(stored.TryGetValue("preset",out v) && preset.Items.Contains(v)) preset.SelectedItem=v; if(stored.TryGetValue("backend",out v) && backend.Items.Contains(v)) backend.SelectedItem=v;}} catch {}
        model.TextChanged+=delegate {ChooseDetectedPreset();}; ChooseDetectedPreset();
        browse.Click+=delegate {using(var dialog=new OpenFileDialog {Filter="Image checkpoints (*.safetensors;*.gguf)|*.safetensors;*.gguf",Title="Choose an SD 1.x or SDXL all-in-one checkpoint"}) if(dialog.ShowDialog(this)==DialogResult.OK) {model.Text=dialog.FileName; ChooseDetectedPreset();}};
        improve.Click+=async delegate {if(active!=null || string.IsNullOrWhiteSpace(prompt.Text)) return; active=new CancellationTokenSource(); generate.Enabled=improve.Enabled=false; stop.Enabled=true; foreach(var control in settings) control.Enabled=false; try {string text=await this.improvePrompt(prompt.Text,active.Token); if(!closed) {prompt.Text=text.Trim(); status.Text="Chat drafted this prompt. Review it before generating; your original request may need corrections.";}} catch(OperationCanceledException) {if(!closed) status.Text="Prompt drafting stopped.";} catch(Exception e) {Error(e);} finally {active.Dispose(); active=null; if(!closed) {generate.Enabled=improve.Enabled=true; stop.Enabled=false; foreach(var control in settings) control.Enabled=true; preset.Enabled=GenerationPresets.Infer(model.Text)==null;}}};
        chooseReference.Click+=delegate {using(var dialog=new OpenFileDialog {Filter="Pictures (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"}) if(dialog.ShowDialog(this)==DialogResult.OK) try {reference=ImageAttachment.Load(dialog.FileName); refLabel.Text=reference.Name;} catch(Exception e) {Error(e);} };
        removeReference.Click+=delegate {reference=null; refLabel.Text="Optional img2img";}; reuse.Click+=delegate {if(result!=null) {reference=ImageAttachment.Load(result); refLabel.Text=reference.Name;}};
        generate.Click+=async delegate {await Generate();}; stop.Click+=delegate {if(active!=null) active.Cancel();};
        save.Click+=delegate {if(result==null) return; using(var dialog=new SaveFileDialog {Filter="PNG image (*.png)|*.png",FileName=Path.GetFileName(result)}) if(dialog.ShowDialog(this)==DialogResult.OK) try {if(!string.Equals(Path.GetFullPath(dialog.FileName),Path.GetFullPath(result),StringComparison.OrdinalIgnoreCase)) File.Copy(result,dialog.FileName,true);} catch(Exception e) {Error(e);} };
    }
    void ChooseDetectedPreset() {string detected=GenerationPresets.Infer(model.Text); preset.SelectedIndex=GenerationPresets.Index(detected); preset.Enabled=detected==null; status.Text=detected==null?"Select this model's correct preset before generating.":"Detected "+detected+" from the model filename. Matching preset selected automatically.";}
    void Error(Exception e) {MemoryReport.Record("Picture generation",e,"Model: "+MemoryReport.Model(model.Text)+"\r\nBackend: "+backend.Text+"; size: "+size.Text); if(!closed) MessageBox.Show(this,e.Message+"\r\n\r\nUse Memory / errors → Copy report for the full details.","Picture generation",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    async Task Generate() {
        if(assignedModel!=null) {try {model.Text=assignedModel();}catch(Exception e) {Error(e);return;}}
        if(active!=null || string.IsNullOrWhiteSpace(prompt.Text)) return;
        if(!File.Exists(model.Text)) {Error(new FileNotFoundException("Choose your image-generation model first. Swift is a vision/chat model, not an image generator.")); return;}
        try {GenerationPresets.Resolve(model.Text,GenerationPresets.At(preset.SelectedIndex));} catch(Exception e) {Error(e); return;}
        if(!releaseChat()) return;
        var theme=themes.Active; bool turbo=preset.SelectedIndex==0;
        var request=new GenerationRequest {Model=model.Text,Preset=GenerationPresets.At(preset.SelectedIndex),Prompt=themes.ImagePrompt(prompt.Text.Trim()),Negative=turbo?"":(theme==null?"":theme.Avoid),Backend=backend.SelectedIndex==0?"CPU":"diffusion=Vulkan0,te=CPU,vae=CPU",Steps=turbo?4:20,Guidance=turbo?1:7,Size=int.Parse(size.Text),Seed=(int)seed.Value,Threads=Math.Max(1,Math.Min(8,Environment.ProcessorCount/2)),ReferenceData=reference==null?null:Convert.ToBase64String(reference.Jpeg),OutputFolder=Path.Combine(LocalFiles.Root,"pictures")};
        effective.Text=request.Prompt+(turbo?"\r\n[Turbo preset: negative prompts are ignored.]":"\r\nAvoid: "+request.Negative);
        active=new CancellationTokenSource(); engine=new PictureEngine(); generate.Enabled=false; stop.Enabled=true; foreach(var control in settings) control.Enabled=false; setGenerating(true); status.Text="Generating locally. Models are loaded only for this job…";
        try {
            LocalFiles.SaveJson(SettingsPath,new Dictionary<string,string>{{"model",model.Text},{"preset",preset.Text},{"backend",backend.Text}});
            var progress=new Progress<string>(text=>{if(!closed && active!=null) status.Text=text.Length>160?text.Substring(0,160):text;});
            result=await engine.Generate(request,progress,active.Token);
            if(!closed) {if(preview.Image!=null) preview.Image.Dispose(); using(var image=Image.FromFile(result)) preview.Image=new Bitmap(image); save.Enabled=true; status.Text="Done. Saved locally; use Save picture to copy it elsewhere. Chat can be loaded again.";}
        } catch(OperationCanceledException) {if(!closed) status.Text="Stopped. Generation model released.";} catch(Exception e) {if(!closed) {status.Text="Generation failed. Try CPU or another compatible checkpoint."; Error(e);}}
        finally {engine.Dispose(); engine=null; active.Dispose(); active=null; setGenerating(false); if(!closed) {generate.Enabled=true; stop.Enabled=false; foreach(var control in settings) control.Enabled=true; preset.Enabled=GenerationPresets.Infer(model.Text)==null;}}
    }
    public void Shutdown() {closed=true; if(active!=null) active.Cancel(); if(engine!=null) engine.Dispose(); if(preview.Image!=null) preview.Image.Dispose();}
}
static class PictureSmoke {
    public static async Task Run(string model,string backend,string report) {
        var request=new GenerationRequest {Model=model,Backend=backend,Prompt="a small red wooden cottage beside a lake, watercolor illustration, soft blue and green palette",Negative="",Steps=2,Guidance=1,Threads=8,OutputFolder=Path.GetDirectoryName(report)};
        using(var engine=new PictureEngine())
        using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(5))) {
            string result=await engine.Generate(request,null,cancel.Token);
            File.AppendAllText(report,"\r\nPASS: generated and decoded 512x512 PNG with metadata using direct local image engine.\r\nImage: "+result);
        }
    }
}
static class CreativeSmoke {
    static void Require(bool condition,string message) {if(!condition) throw new Exception(message);}
    public static void Run(string report) {
        string temp=Path.Combine(Path.GetTempPath(),"LlamaLite-theme-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try {
            var theme=new ThemeProfile {Name="Svensk skog",Style="watercolor, gröna träd, recurring red cottage",Avoid="neon colors"};
            var data=new ThemeData {Version=1,Enabled=true,Selected=theme.Name,Profiles=new List<ThemeProfile>{theme}};
            string file=Path.Combine(temp,"themes.json"); LocalFiles.SaveJson(file,data);
            var loaded=Engine.Json.Deserialize<ThemeData>(File.ReadAllText(file));
            Require(loaded.Version==1 && loaded.Enabled && loaded.Selected==theme.Name && loaded.Profiles[0].Style==theme.Style,"Theme did not survive serialization with Swedish characters.");
            Require(ThemeRules.Chat(loaded.Profiles[0],"Be helpful").Contains(theme.Avoid),"Chat did not receive the saved avoid list.");
            Require(ThemeRules.Picture(loaded.Profiles[0],"a lake")=="a lake, "+theme.Style,"Picture prompt lost its theme.");
            Require(ThemeRules.Picture(null,"a lake")=="a lake" && ThemeRules.Chat(null,"hello")=="hello","Disabled theme changed the prompt.");
            var skill=new TaskSkill {Name="Recipe",Instruction="Preserve all amounts"};
            Require(SkillRules.Apply(skill,"Base").Contains(skill.Instruction) && SkillRules.Apply(null,"Base")=="Base","Skill application or disabled skill failed.");
            string skillFile=Path.Combine(temp,"skills.json"); LocalFiles.SaveJson(skillFile,new SkillData {Version=1,Enabled=true,Selected=skill.Name,Skills=new List<TaskSkill>{skill}});
            Require(Engine.Json.Deserialize<SkillData>(File.ReadAllText(skillFile)).Skills[0].Instruction==skill.Instruction,"Skill instructions did not persist.");
            data.Profiles.Add(new ThemeProfile {Name="Other",Style="ink drawing",Avoid=""}); LocalFiles.SaveJson(file,data);
            Require(File.Exists(file+".bak") && Engine.Json.Deserialize<ThemeData>(File.ReadAllText(file+".bak")).Profiles.Count==1,"Atomic update did not preserve its previous version.");
            string model=Path.Combine(temp,"Swift-Qwen3.8-27B-Q4_K_M.gguf"),projector=Path.Combine(temp,"mmproj-Swift-Qwen3.8-27B-F16.gguf"); File.WriteAllText(model,""); File.WriteAllText(projector,"");
            Require(ImageAttachment.FindProjector(model)==projector,"Projector discovery failed.");
            File.WriteAllText(Path.Combine(temp,"mmproj-Swift-Qwen3.8-27B-Q8_0.gguf"),""); Require(ImageAttachment.FindProjector(model)=="","Ambiguous projector was silently selected.");
            Require(LocalFiles.Quote("C:\\models\\")=="\"C:\\models\\\\\"","Windows terminal backslash was quoted incorrectly.");
            Require(MemoryReport.Summary().Contains("Physical RAM:"),"Windows RAM report was unavailable.");
            Require(MemoryReport.FormatFailure("Startup",new IOException("Synthetic startup failure"),"model.gguf").Contains("Synthetic startup failure"),"Pre-runtime failures were missing from diagnostics.");
            Require(MemoryReport.Build("Selected model","").Contains("No runtime log captured"),"Empty runtime logs should still produce a diagnostic report.");
            File.WriteAllText(report,"PASS: theme and skill persistence/application, Swedish text, disabled theme/skill, atomic backup, exact/ambiguous projector discovery and Windows argument quoting.");
        } finally {Directory.Delete(temp,true);}
    }
}
}
