using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BoltWorks.LlamaLite {
static class MemorySmoke {
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static async Task Run(string folder,string model) {
        Directory.CreateDirectory(folder);
        Check(ModelMemory.Context(MemoryMode.Normal,16384)==16384,"Normal mode changed context");
        Check(ModelMemory.Context(MemoryMode.Low,16384)==4096&&ModelMemory.Context(MemoryMode.Disk,2048)==2048,"Context cap incorrect");
        Check(ModelMemory.Arguments(MemoryMode.Disk).Contains("--load-mode mmap")&&ModelMemory.Arguments(MemoryMode.Disk).Contains("--no-warmup"),"Disk arguments missing");
        Check(Acceleration.Arguments(ComputeBackend.Cpu,16)==" -ngl 0","CPU default changed");Check(Acceleration.Arguments(ComputeBackend.Cuda,16).Contains("CUDA0"),"CUDA option changed");
        string a=Path.Combine(folder,"test-00001-of-00002.gguf"),b=Path.Combine(folder,"test-00002-of-00002.gguf");File.WriteAllText(a,"GGUFfirst");File.WriteAllText(b,"GGUFsecond");
        var set=GgufSet.Inspect(b);Check(set.First==a&&set.Parts.Length==2&&set.Bytes==19,"Split set normalization failed");
        File.Delete(b);bool rejected=false;try{GgufSet.Inspect(a);}catch(FileNotFoundException){rejected=true;}Check(rejected,"Missing split part accepted");
        File.WriteAllText(b,"bad!");rejected=false;try{GgufSet.Inspect(a);}catch(IOException){rejected=true;}Check(rejected,"Invalid shard header accepted");
        var history=new List<Dictionary<string,object>> {ChatWindow.Message("system","contract"),ChatWindow.Message("user","task"),ChatWindow.Message("assistant",new string('x',1000)),ChatWindow.Message("user",new string('y',4000)),ChatWindow.Message("assistant","read"),ChatWindow.Message("user",new string('z',4000))};
        Func<List<Dictionary<string,object>>,CancellationToken,Task<int>> measure=(messages,cancel)=>Task.FromResult(Engine.Json.Serialize(messages).Length);
        var fitted=await CodeContext.Fit(history,2048,512,measure,CancellationToken.None);Check(await measure(fitted,CancellationToken.None)<=1408&&history.Count==6&&(string)fitted[1]["content"]=="task","Context fitting mutated source or lost task");
        rejected=false;try{await CodeContext.Fit(new List<Dictionary<string,object>> {ChatWindow.Message("system","contract"),ChatWindow.Message("user",new string('x',5000))},2048,512,measure,CancellationToken.None);}catch(IOException){rejected=true;}Check(rejected,"Oversized task silently dropped");
        File.WriteAllText(Path.Combine(folder,"unit.txt"),"PASS: unchanged normal/CPU/CUDA routing, context caps, mmap flags, split-set normalization/size, missing/corrupt shards rejected, context budget and task preservation.");
        if(string.IsNullOrEmpty(model))return;
        foreach(MemoryMode mode in new[]{MemoryMode.Low,MemoryMode.Disk})using(var engine=new Engine {Memory=mode})using(var budget=new CancellationTokenSource(TimeSpan.FromMinutes(3))) {
            var started=DateTime.UtcNow;await engine.Start(model,2,16384,budget.Token);Check(engine.ActiveContext==4096,"Native context not capped");
            var messages=new List<Dictionary<string,object>> {ChatWindow.Message("system","Answer briefly."),ChatWindow.Message("user","Say hello in one short sentence.")};
            int tokens=await engine.PromptTokens(messages,budget.Token);Check(tokens>0&&tokens<4096,"Tokenizer failed");
            var large=new List<Dictionary<string,object>> {ChatWindow.Message("system",CodeAgent.Instruction),ChatWindow.Message("user","Inspect the source and explain it."),ChatWindow.Message("assistant","read source"),ChatWindow.Message("user",new string('x',30000))};
            var bounded=await CodeContext.Fit(large,4096,1024,engine.PromptTokens,budget.Token);Check(await engine.PromptTokens(bounded,budget.Token)<=2944,"Real tokenizer budget overflow");Check((string)large[3]["content"]==new string('x',30000),"Context fit mutated source");
            var request=await CodeContext.Fit(messages,engine.ActiveContext,128,engine.PromptTokens,budget.Token);
            string answer=await engine.Chat(request,128,s=>{},budget.Token);Check(!string.IsNullOrWhiteSpace(answer),"Empty native answer");
            File.WriteAllText(Path.Combine(folder,mode+"-output.txt"),answer);
            File.WriteAllText(Path.Combine(folder,mode+"-runtime.txt"),engine.Diagnostics);
            File.WriteAllText(Path.Combine(folder,mode+"-result.json"),Engine.Json.Serialize(new {mode=mode.ToString(),context=engine.ActiveContext,promptTokens=tokens,seconds=(DateTime.UtcNow-started).TotalSeconds,files=GgufSet.Inspect(model).Parts.Length,outputWritten=true,oversizedModelTest=false}));
        }
    }
}
}
