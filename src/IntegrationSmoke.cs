using System;
using System.IO;
using System.Collections.Generic;

namespace BoltWorks.LlamaLite {
static class IntegrationSmoke {
    static void Require(bool condition,string message) {if(!condition) throw new Exception(message);}
    public static void Run(string report) {
        string context=HostContext.Instruction(new DateTimeOffset(2026,10,29,12,0,0,TimeSpan.FromHours(1)),"Europe/Stockholm");
        Require(context.Contains("2026-10-29 Thursday")&&context.Contains("physical country/location is unknown"),"Date/location context is wrong.");
        var original=new List<Dictionary<string,object>> {ChatWindow.Message("system","Original")}; var enriched=HostContext.Apply(original);
        Require((string)original[0]["content"]=="Original" && ((string)enriched[0]["content"]).Contains("APPLICATION FACTS"),"Host context mutated caller data or was not added.");
        Require(GenerationPresets.Infer("stable-diffusion-v1-5-pruned-emaonly-Q4_0.gguf")=="sd1","SD 1.5 detection failed.");
        Require(GenerationPresets.Infer("sd_xl_turbo_1.0_fp16.safetensors")=="sdxl-turbo","SDXL Turbo detection failed.");
        bool rejected=false;try{GenerationPresets.Resolve("v1-5-pruned_Q4_0.gguf","sdxl-turbo");}catch(InvalidDataException){rejected=true;}Require(rejected,"Wrong Turbo preset was accepted.");
        File.WriteAllText(report,"PASS: host context and image preset checks.");
    }
}
}
