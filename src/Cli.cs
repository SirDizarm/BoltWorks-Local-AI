using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BoltWorks.LlamaLite {
static class Cli {
    sealed class UsageException : Exception {public UsageException(string message):base(message) {}}
    static readonly HashSet<string> Flags=new HashSet<string>(new string[]{"json","stream","help"},StringComparer.Ordinal);
    static int Main(string[] args) {
        Console.OutputEncoding=new UTF8Encoding(false); Console.InputEncoding=new UTF8Encoding(false,true);
        bool json=Array.IndexOf(args,"--json")>=0;
        using(var cancel=new CancellationTokenSource()) {
            ConsoleCancelEventHandler handler=delegate(object sender,ConsoleCancelEventArgs e) {e.Cancel=true; cancel.Cancel();};
            Console.CancelKeyPress+=handler;
            try {return Run(args,cancel).GetAwaiter().GetResult();}
            catch(OperationCanceledException) {Error(json,"cancelled","Request cancelled or timed out."); return 130;}
            catch(UsageException e) {Error(json,"usage",e.Message); return 2;}
            catch(Exception e) {Error(json,"runtime",e.Message); return 1;}
            finally {Console.CancelKeyPress-=handler;}
        }
    }
    static void Error(bool json,string code,string message) {Console.Error.WriteLine(json ? Engine.Json.Serialize(new {version=1,error=new {code=code,message=message}}) : "Error: "+message);}
    static Dictionary<string,string> Parse(string[] args) {
        var options=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=1;i<args.Length;i++) {
            if(!args[i].StartsWith("--",StringComparison.Ordinal) || args[i].Length<3) throw new UsageException("Expected an option beginning with --, got: "+args[i]);
            string key=args[i].Substring(2); if(options.ContainsKey(key)) throw new UsageException("Duplicate option: --"+key);
            if(Flags.Contains(key)) options.Add(key,"true");
            else {if(i+1>=args.Length || args[i+1].StartsWith("--",StringComparison.Ordinal)) throw new UsageException("Missing value for --"+key); options.Add(key,args[++i]);}
        }
        return options;
    }
    static void Allow(Dictionary<string,string> options,params string[] names) {
        var allowed=new HashSet<string>(names,StringComparer.Ordinal); allowed.Add("help"); allowed.Add("json");
        foreach(string key in options.Keys) if(!allowed.Contains(key)) throw new UsageException("Unknown option for this command: --"+key);
    }
    static string Get(Dictionary<string,string> options,string key,string fallback) {string value; return options.TryGetValue(key,out value)?value:fallback;}
    static string Required(Dictionary<string,string> options,string key) {string value=Get(options,key,null); if(string.IsNullOrWhiteSpace(value)) throw new UsageException("Missing required --"+key); return value;}
    static int Number(Dictionary<string,string> options,string key,int fallback,int min,int max) {
        string value; int number=fallback; if(options.TryGetValue(key,out value) && !int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out number)) throw new UsageException("--"+key+" must be an integer.");
        if(number<min || number>max) throw new UsageException("--"+key+" must be between "+min+" and "+max+"."); return number;
    }
    static string ReadText(string path,int maxChars) {
        if(!File.Exists(path)) throw new UsageException("Input file does not exist: "+path);
        if(new FileInfo(path).Length>maxChars*4L+3) throw new UsageException("Input file is too large.");
        string text=File.ReadAllText(path,new UTF8Encoding(false,true));
        if(text.Length>maxChars || text.IndexOf('\0')>=0) throw new UsageException("Expected plain UTF-8 text up to "+maxChars+" characters."); return text;
    }
    static async Task<string> Prompt(Dictionary<string,string> options,CancellationToken cancel) {
        if(options.ContainsKey("prompt") && options.ContainsKey("input")) throw new UsageException("Use either --prompt or --input, not both.");
        string prompt;
        if(options.ContainsKey("prompt")) prompt=options["prompt"];
        else if(options.ContainsKey("input")) prompt=ReadText(options["input"],32000);
        else if(Console.IsInputRedirected) {
            var text=new StringBuilder(); var buffer=new char[1024]; int count;
            var interrupted=Task.Delay(Timeout.Infinite,cancel);
            while(true) {
                // Console.In's synchronized reader can block even in ReadAsync on .NET Framework.
                var read=Task.Run(delegate {return Console.In.Read(buffer,0,buffer.Length);});
                if(await Task.WhenAny(read,interrupted)!=read) cancel.ThrowIfCancellationRequested();
                count=await read; if(count==0) break;
                text.Append(buffer,0,count); if(text.Length>32000) throw new UsageException("Standard input exceeds 32,000 characters.");
            }
            prompt=text.ToString();
        } else throw new UsageException("Provide --prompt, --input, or pipe text on standard input.");
        if(string.IsNullOrWhiteSpace(prompt) || prompt.Length>32000 || prompt.IndexOf('\0')>=0) throw new UsageException("Provide nonempty plain text up to 32,000 characters."); return prompt.Trim();
    }
    static ThemeData Themes() {
        string path=Path.Combine(LocalFiles.Root,"themes.json"); if(!File.Exists(path)) return new ThemeData {Version=1,Profiles=new List<ThemeProfile>()};
        var data=Engine.Json.Deserialize<ThemeData>(ReadText(path,250000)); if(data==null || data.Version!=1 || data.Profiles==null) throw new UsageException("Unsupported themes.json. Existing file was not changed."); return data;
    }
    static SkillData Skills() {
        string path=Path.Combine(LocalFiles.Root,"skills.json"); if(!File.Exists(path)) return new SkillData {Version=1,Skills=new List<TaskSkill>()};
        var data=Engine.Json.Deserialize<SkillData>(ReadText(path,500000)); if(data==null || data.Version!=1 || data.Skills==null) throw new UsageException("Unsupported skills.json. Existing file was not changed."); return data;
    }
    static ThemeProfile Theme(Dictionary<string,string> options) {
        string name=Get(options,"theme",null); if(name==null) return null;
        var theme=Themes().Profiles.Find(p=>p!=null && string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase));
        if(theme==null) throw new UsageException("Saved theme not found: "+name+". Save it in the desktop app first, then use the themes command to list it."); return theme;
    }
    static TaskSkill Skill(Dictionary<string,string> options) {
        if(options.ContainsKey("skill") && options.ContainsKey("skill-file")) throw new UsageException("Use either --skill or --skill-file.");
        if(options.ContainsKey("skill-file")) return new TaskSkill {Name=Path.GetFileName(options["skill-file"]),Instruction=ReadText(options["skill-file"],12000)};
        string name=Get(options,"skill",null); if(name==null) return null;
        var skill=Skills().Skills.Find(p=>p!=null && string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase));
        if(skill==null) throw new UsageException("Saved skill not found: "+name+". Save it in the desktop app first, or use --skill-file."); return skill;
    }
    static async Task<int> Run(string[] args,CancellationTokenSource cancel) {
        if(args.Length==0 || args[0]=="help" || args[0]=="--help") {Console.WriteLine(Help); return 0;}
        if(args[0]=="--version") {Console.WriteLine("BoltWorks Local AI CLI 0.6.19"); return 0;}
        string command=args[0]; var options=Parse(args); bool json=options.ContainsKey("json");
        if(command=="firmware-scan") {Allow(options,"file");var report=MiniRea.Scan(Required(options,"file"),cancel.Token);Console.WriteLine(json?Engine.Json.Serialize(report):report.ToString());return 0;}
        if(command=="import-legacy") {Allow(options,"file","output-parent");string folder=LegacyImport.Import(Required(options,"file"),Required(options,"output-parent"),cancel.Token);Console.WriteLine(json?Engine.Json.Serialize(new {folder=folder,executedTarget=false,decompiled=false}):folder);return 0;}
        if(command=="split-model") {Allow(options,"model","output-dir","part-mb");var split=await GgufTools.Split(Required(options,"model"),Required(options,"output-dir"),Number(options,"part-mb",512,64,65536),cancel.Token);Console.WriteLine(json?Engine.Json.Serialize(new {first=split.First,parts=split.Parts,totalBytes=split.Bytes,reducesWorkingRam=false}):split.Describe());return 0;}
        if(command=="model-info") {Allow(options,"model");var set=GgufSet.Inspect(Required(options,"model"));Console.WriteLine(json?Engine.Json.Serialize(new {first=set.First,parts=set.Parts,totalBytes=set.Bytes,checks="filenames, existence and GGUF magic; runtime validates tensor metadata",fixedRamLimit=false}):set.Describe());return 0;}
        if(command=="themes") {Allow(options); var data=Themes(); if(json) Console.WriteLine(Engine.Json.Serialize(new {version=1,themes=data.Profiles})); else foreach(var theme in data.Profiles) Console.WriteLine(theme.Name); return 0;}
        if(command=="skills") {Allow(options); var data=Skills(); if(json) Console.WriteLine(Engine.Json.Serialize(new {version=1,skills=data.Skills})); else foreach(var skill in data.Skills) Console.WriteLine(skill.Name); return 0;}
        if(command=="hardware") {Allow(options); int processors=Environment.ProcessorCount,threads=Math.Max(1,Math.Min(8,processors/2)); if(json) Console.WriteLine(Engine.Json.Serialize(new {version=1,logicalProcessors=processors,defaultThreads=threads,is64Bit=Environment.Is64BitProcess,memory=MemoryReport.Summary()})); else Console.WriteLine("Logical processors: "+processors+"\nDefault threads: "+threads+"\n64-bit: "+Environment.Is64BitProcess+"\n"+MemoryReport.Summary()); return 0;}
        if(command!="chat" && command!="read" && command!="generate") throw new UsageException("Unknown command: "+command+". Use --help.");
        if(command=="generate") Allow(options,"model","prompt","input","theme","backend","preset","size","steps","seed","threads","output-dir","reference","timeout");
        else Allow(options,"model","prompt","input","projector","image","theme","skill","skill-file","system","threads","context","max-tokens","backend","gpu-layers","memory","stream","timeout");
        if(options.ContainsKey("help")) {Console.WriteLine(Help); return 0;}
        if(json && options.ContainsKey("stream")) throw new UsageException("--json and --stream cannot be combined. JSON returns one complete result.");
        int timeout=Number(options,"timeout",600,1,86400); cancel.CancelAfter(TimeSpan.FromSeconds(timeout));
        string model=Path.GetFullPath(Required(options,"model"));
        if(!File.Exists(model)) throw new UsageException("Model file does not exist: "+model);
        string prompt=await Prompt(options,cancel.Token); var themeSelected=Theme(options);
        int threadsSelected=Number(options,"threads",Math.Max(1,Math.Min(8,Environment.ProcessorCount/2)),1,64);
        if(command=="generate") {
            string preset; try {preset=GenerationPresets.Resolve(model,Get(options,"preset","auto"));} catch(InvalidDataException e) {throw new UsageException(e.Message);}
            string backend=Get(options,"backend","cpu"); if(backend!="cpu" && backend!="vulkan") throw new UsageException("--backend must be cpu or vulkan.");
            bool turbo=preset=="sdxl-turbo";
            var request=new GenerationRequest {Model=model,Preset=preset,Prompt=ThemeRules.Picture(themeSelected,prompt),Negative=turbo?"":(themeSelected==null?"":themeSelected.Avoid),Backend=backend=="cpu"?"CPU":"diffusion=Vulkan0,te=CPU,vae=CPU",Steps=Number(options,"steps",turbo?4:20,1,50),Guidance=turbo?1:7,Size=Number(options,"size",512,256,768),Seed=Number(options,"seed",42,-1,int.MaxValue),Threads=threadsSelected,OutputFolder=Path.GetFullPath(Get(options,"output-dir",Path.Combine(Environment.CurrentDirectory,"generated")))};
            if(request.Size%64!=0) throw new UsageException("--size must be a multiple of 64.");
            if(request.Prompt.Length>12000) throw new UsageException("Picture prompt plus theme exceeds 12,000 characters.");
            if(options.ContainsKey("reference")) request.ReferenceData=Convert.ToBase64String(ImageAttachment.Load(options["reference"]).Jpeg);
            if(request.ReferenceData!=null && request.Steps*request.Strength<1) throw new UsageException("Use at least two steps with --reference.");
            using(var engine=new PictureEngine()) {
                string image=await engine.Generate(request,null,cancel.Token);
                Console.WriteLine(json ? Engine.Json.Serialize(new {version=1,task="generate",image=image,metadata=Path.ChangeExtension(image,".json"),model=model,theme=themeSelected==null?null:themeSelected.Name}) : image);
            }
        } else {
            string image=Get(options,"image",null); if(command=="read" && image==null) throw new UsageException("The read command requires --image.");
            string projector=Get(options,"projector",null);
            if(projector==null && (image!=null)) projector=ImageAttachment.FindProjector(model);
            if((image!=null) && string.IsNullOrWhiteSpace(projector)) throw new UsageException("Image input requires --projector with its matching vision GGUF.");
            if(projector!=null && projector.Length>0) projector=Path.GetFullPath(projector);
            var attachment=image==null?null:ImageAttachment.Load(image);
            var skillSelected=Skill(options);
            string instruction=ThemeRules.Chat(themeSelected,SkillRules.Apply(skillSelected,Get(options,"system","You are a helpful assistant. Answer clearly in the language the user uses.")));
            MemoryMode memory=ModelMemory.Parse(Get(options,"memory","normal"));
            int context=ModelMemory.Context(memory,Number(options,"context",2048,512,16384)),tokens=Number(options,"max-tokens",256,1,4096);
            if(tokens>context/2) throw new UsageException("--max-tokens must be at most half the effective context ("+context+") after the memory-mode cap.");
            using(var engine=new Engine()) {
                engine.Memory=memory;
                string compute=Get(options,"backend","cpu");
                if(compute!="cpu"&&compute!="cuda")throw new UsageException("--backend must be cpu or cuda for text and vision.");
                engine.Backend=compute=="cuda"?ComputeBackend.Cuda:ComputeBackend.Cpu;
                engine.GpuLayers=Number(options,"gpu-layers",16,1,128);
                await engine.Start(model,projector??"",threadsSelected,context,cancel.Token);
                var messages=new List<Dictionary<string,object>> {ChatWindow.Message("system",instruction),attachment==null?ChatWindow.Message("user",prompt):attachment.Message(prompt)};
                bool stream=options.ContainsKey("stream");
                string text=await engine.Chat(messages,tokens,delegate(string part) {if(stream) {Console.Out.Write(part); Console.Out.Flush();}},cancel.Token);
                if(json) Console.WriteLine(Engine.Json.Serialize(new {version=1,task=command,text=text,model=model,theme=themeSelected==null?null:themeSelected.Name,skill=skillSelected==null?null:skillSelected.Name}));
                else if(stream) Console.WriteLine(); else Console.WriteLine(text);
            }
        }
        return 0;
    }
    const string Help=@"BoltWorks Local AI CLI 0.6.19 — local inference, no desktop window

Commands:
  firmware-scan --file FILE [--json]  Mini REA static signature scan; no Node required.
  import-legacy --file FILE --output-parent EXISTING_FOLDER [--json]
  split-model --model FILE --output-dir NEW_FOLDER [--part-mb 512] [--json]
  model-info --model FILE [--json]  Inspect a GGUF or complete split set without loading it.
  chat       --model FILE --prompt TEXT [options]
  read       --model FILE --projector FILE --image FILE --prompt TEXT [options]
  generate   --model FILE --prompt TEXT [options]
  themes     [--json]          List themes saved in the desktop app
  skills     [--json]          List skills saved in the desktop app
  hardware   [--json]          Show detected CPU/default thread count

Input: choose --prompt TEXT, --input UTF8.txt, or pipe UTF-8 text to stdin.
Common: --json, --theme NAME, --threads N, --timeout SECONDS (default 600).
Text/vision: --backend cpu (default) or cuda; --gpu-layers N (default 16).
CUDA requires the separate optional runtime-cuda add-on. CPU never needs it.
Chat/read: --skill NAME or --skill-file FILE, --system TEXT, --context N
           (default 2048), --max-tokens N (default 256), --stream (plain text).
           --memory normal|low|disk (default normal). Low/disk cap context at 4096.
           Disk uses CPU mmap/lazy reads, not a hard RAM limit; --backend cuda is rejected.
Chat also accepts --image FILE and --projector FILE.
Read can discover a single matching projector beside the model automatically.
Generate: --preset auto|sdxl-turbo|sd1|sdxl (default auto; unknown names require an explicit preset),
          --backend cpu|vulkan (default cpu), --steps N, --size N (default 512),
          --seed N (default 42; -1=random), --reference FILE,
          --output-dir DIRECTORY (default ./generated).

stdout contains only the result: text, image path, or one JSON object.
Errors go to stderr. JSON errors are objects when --json is supplied.
Exit codes: 0=success, 1=runtime failure, 2=invalid arguments, 130=cancel/timeout.
Ctrl+C cancels. Each invocation loads/unloads its model; no service is installed.
Use --help or --version. Keep runtime and image-runtime beside this EXE.
Models are local files. Saved themes/skills are applied only when explicitly named.
";
}
}
