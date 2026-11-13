using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace BoltWorks.LlamaLite {
static class HostContext {
    public static string Instruction(DateTimeOffset now,string zone) {
        return "\nAPPLICATION FACTS (supplied fresh by the local app, not from model training):\n"+
            "The computer's current local date/time is "+now.ToString("yyyy-MM-dd dddd HH:mm:ss zzz",CultureInfo.InvariantCulture)+". Time zone: "+zone+". Use this when asked today's date, day or year.\n"+
            "You are a software model running locally through BoltWorks Local AI on the user's computer. You have no physical location, nationality or personal residence. The user's physical country/location is unknown unless they tell you. A time zone or language does not establish their location.\n"+
            "You can process supplied text and, only with a compatible vision model, explicitly attached images. Do not claim live internet, file, or desktop actions unless the application provides a real tool result. State uncertainty instead of inventing current facts or capabilities.\n";
    }
    public static List<Dictionary<string,object>> Apply(List<Dictionary<string,object>> source) {
        var result=new List<Dictionary<string,object>>(); bool added=false;
        foreach(var item in source) {var copy=new Dictionary<string,object>(item); object role,content; if(!added && copy.TryGetValue("role",out role) && (string)role=="system" && copy.TryGetValue("content",out content) && content is string) {copy["content"]=(string)content+Instruction(DateTimeOffset.Now,TimeZoneInfo.Local.Id); added=true;} result.Add(copy);}
        if(!added) result.Insert(0,ChatWindow.Message("system",Instruction(DateTimeOffset.Now,TimeZoneInfo.Local.Id)));
        return result;
    }
}
static class GenerationPresets {
    public static string Infer(string path) {
        string name=Path.GetFileName(path).ToLowerInvariant();
        bool xl=name.Contains("sdxl") || name.Contains("sd_xl") || name.Contains("sd-xl") || name.Contains("stable-diffusion-xl");
        if(xl) return name.Contains("turbo")?"sdxl-turbo":"sdxl";
        if(Regex.IsMatch(name,@"(?:stable[-_]diffusion[-_]v?|sd[-_]v?|\bv)1[-_.][45](?:[-_.]|$)")) return "sd1";
        return null;
    }
    public static int Index(string preset) {return preset=="sdxl-turbo"?0:preset=="sd1"?1:preset=="sdxl"?2:-1;}
    public static string At(int index) {return index==0?"sdxl-turbo":index==1?"sd1":index==2?"sdxl":null;}
    public static string Resolve(string model,string requested) {
        string detected=Infer(model);
        if(requested==null || requested=="auto") {if(detected==null) throw new InvalidDataException("Cannot identify this image checkpoint from its filename. Select its correct preset explicitly: SD 1.x, SDXL or SDXL Turbo."); return detected;}
        if(Index(requested)<0) throw new InvalidDataException("Unsupported image preset.");
        if(detected!=null && requested!=detected) throw new InvalidDataException("This model filename identifies "+detected+", but "+requested+" was selected. Use the matching preset; Turbo settings can produce unusable output with regular SD 1.5.");
        return requested;
    }
}
}
