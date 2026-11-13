using System;
using System.IO;

namespace BoltWorks.LlamaLite {
static class ModelSlotsSmoke {
    static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    public static void Run(string report) {
        string folder=Path.Combine(Path.GetDirectoryName(report),"slots-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,"slots.json"),model=Path.Combine(folder,"text.gguf"),projector=Path.Combine(folder,"mmproj.gguf");File.WriteAllText(model,"");File.WriteAllText(projector,"");
        var slots=new ModelSlots(file);slots.Seed("chat",model,"");Check(slots.Missing("chat")!=null,"Choosing a file silently locked it.");slots.Items["chat"].Locked=true;slots.Save();
        slots=new ModelSlots(file);Check(slots.Require("chat").File==model,"Locked assignment was not persisted.");Check(slots.Missing("pictures")!=null,"Chat assignment leaked into Create pictures.");
        slots.Items["vision"].File=model;slots.Items["vision"].Locked=true;Check(slots.Missing("vision")!=null,"Vision was accepted without its picture-reading file.");slots.Items["vision"].Projector=projector;Check(slots.Missing("vision")==null,"Complete vision assignment rejected.");
        File.Delete(model);Check(slots.Missing("chat")!=null,"Missing model file produced a silent fallback.");
        File.WriteAllText(report,"PASS: persisted model locks, independent roles, and required vision projector.");
    }
}
}
