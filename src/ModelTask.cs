using System;
using System.Collections.Generic;
using System.IO;

namespace BoltWorks.LlamaLite {
sealed class ModelTask {
    public string System,Prompt;
    public int MaxTokens;
    public List<Dictionary<string,object>> Messages() {
        return new List<Dictionary<string,object>> {ChatWindow.Message("system",System),ChatWindow.Message("user",Prompt)};
    }
}
static class JsonValue {
    public static Dictionary<string,object> Object(object value) {var obj=value as Dictionary<string,object>; if(obj==null) throw new InvalidDataException("Expected a JSON object."); return obj;}
    public static object[] Array(object value) {var array=value as object[]; if(array==null) throw new InvalidDataException("Expected a JSON array."); return array;}
    public static string Text(Dictionary<string,object> obj,string key,string fallback="") {object value; return obj.TryGetValue(key,out value) && value is string ? (string)value : fallback;}
}
}
