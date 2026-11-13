using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
namespace BoltWorks.LlamaLite {
static class BinaryStrings {
    internal sealed class Hit {public long Offset;public string Encoding,Kind,Text;}
    static readonly Regex PathHint=new Regex(@"(?:[A-Za-z]:[\\/]|\\\\[^\\\s]+\\|/(?:[A-Za-z0-9_.-]+/)+)",RegexOptions.Compiled);
    internal static string Kind(string text){if(text.IndexOf("http://",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("https://",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("ftp://",StringComparison.OrdinalIgnoreCase)>=0)return "URL candidate";return PathHint.IsMatch(text)?"Path candidate":"Text";}
    sealed class Run {
        readonly StringBuilder text=new StringBuilder();readonly int step;readonly string encoding;readonly Action<Hit> emit;long start;
        internal Run(int width,string name,Action<Hit> output){step=width;encoding=name;emit=output;}
        internal void Feed(int c,long offset){if(c>=32&&c<=126){if(text.Length==0)start=offset;text.Append((char)c);if(text.Length==4096){Flush();text.Remove(0,3840);start+=3840*step;}}else{Flush();text.Clear();}}
        internal void Flush(){if(text.Length>=4){string value=text.ToString();emit(new Hit{Offset=start,Encoding=encoding,Kind=Kind(value),Text=value});}}
    }
    internal static long Scan(string path,string query,bool matchCase,string category,Action<Hit> output,CancellationToken cancel,Action<long> progress){
        if(query==null)query="";if(query.Length>256)throw new ArgumentException("Search text must be at most 256 characters.");
        long hits=0;var comparison=matchCase?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;
        Action<Hit> emit=delegate(Hit h){cancel.ThrowIfCancellationRequested();if(h.Text.IndexOf(query,comparison)<0)return;if(category=="URLs"&&h.Kind!="URL candidate")return;if(category=="Paths"&&h.Kind!="Path candidate")return;hits++;output(h);};
        var ascii=new Run(1,"ASCII",emit);var le=new[]{new Run(2,"UTF-16 LE (ASCII range)",emit),new Run(2,"UTF-16 LE (ASCII range)",emit)};var be=new[]{new Run(2,"UTF-16 BE (ASCII range)",emit),new Run(2,"UTF-16 BE (ASCII range)",emit)};
        using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
            byte[] buffer=new byte[65536];long position=0,nextProgress=0;int previous=-1,count;
            while((count=file.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();for(int i=0;i<count;i++,position++){int b=buffer[i];ascii.Feed(b,position);if(previous>=0){int lane=(int)((position-1)&1);le[lane].Feed(b==0?previous:-1,position-1);be[lane].Feed(previous==0?b:-1,position-1);}previous=b;}
                if(progress!=null&&position>=nextProgress){progress(position);nextProgress=position+1048576;}
            }
            ascii.Flush();foreach(var run in le)run.Flush();foreach(var run in be)run.Flush();if(progress!=null)progress(position);
        }return hits;
    }
    internal static string Row(Hit hit){return "0x"+hit.Offset.ToString("X8")+"\t"+hit.Encoding+"\t"+hit.Kind+"\t"+hit.Text;}
    internal static long Export(string input,string destination,string query,bool matchCase,string category,CancellationToken cancel,Action<long> progress){
        using(var file=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.Read))using(var writer=new StreamWriter(file,new UTF8Encoding(false))){
            writer.WriteLine("Binary text search: "+Path.GetFullPath(input));writer.WriteLine("Query: "+query+"; match case: "+matchCase+"; filter: "+category);writer.WriteLine("Readable ASCII and ASCII-range UTF-16 LE/BE; minimum 4 characters. Long runs are overlapping fragments. Candidate strings are not proof of functionality. Nothing executed or contacted.");
            long count=Scan(input,query,matchCase,category,h=>writer.WriteLine(Row(h)),cancel,progress);writer.WriteLine("COMPLETE: "+count+" matching fragments.");return count;
        }
    }
}
}
