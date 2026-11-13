using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BoltWorks.LlamaLite {
static class CodeContext {
    public static async Task<List<Dictionary<string,object>>> Fit(List<Dictionary<string,object>> source,int context,int reply,Func<List<Dictionary<string,object>>,CancellationToken,Task<int>> measure,CancellationToken cancel) {
        var messages=new List<Dictionary<string,object>>();foreach(var item in source)messages.Add(new Dictionary<string,object>(item));
        int available=context-reply-128;
        for(int attempt=0;attempt<32;attempt++) {
            cancel.ThrowIfCancellationRequested();if(await measure(messages,cancel)<=available)return messages;
            if(messages.Count>4){messages.RemoveAt(2);messages.RemoveAt(2);continue;}
            if(messages.Count>2) {
                string output=messages[messages.Count-1]["content"] as string;
                if(output!=null&&output.Length>500) {messages[messages.Count-1]["content"]=output.Substring(0,output.Length/2)+"\n[Tool result shortened to fit context. Read a smaller page using start if needed.]";continue;}
                string action=messages[2]["content"] as string;
                if(action!=null&&action.Length>300){messages[2]["content"]="[Previous action is recorded in the job log; its result follows.]";continue;}
            }
            // The system includes a rolling activity record. Keep the contract, discard only that record.
            string system=messages[0]["content"] as string;int record=system==null?-1:system.IndexOf("\nExecuted action record",StringComparison.Ordinal);
            if(record>=0){messages[0]["content"]=system.Substring(0,record);continue;}
            throw new IOException("The coding task and instructions exceed this memory mode's context. Shorten the task or select Normal mode. No task instructions were silently removed.");
        }
        throw new IOException("Could not fit this coding request into the selected context.");
    }
}
}
