using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BoltWorks.LlamaLite {
enum MemoryMode { Normal, Low, Disk }
static class ModelMemory {
    public static MemoryMode Parse(string value) {
        switch(value) {case "normal":return MemoryMode.Normal;case "low":return MemoryMode.Low;case "disk":return MemoryMode.Disk;default:throw new IOException("Memory mode must be normal, low or disk.");}
    }
    public static int Context(MemoryMode mode,int requested) {return mode==MemoryMode.Normal?requested:Math.Min(requested,4096);}
    public static string Arguments(MemoryMode mode) {
        if(mode==MemoryMode.Normal)return " -b 128 -ub 64 --mtmd-batch-max-tokens 128";
        if(mode==MemoryMode.Low)return " --load-mode mmap -b 64 -ub 32 --mtmd-batch-max-tokens 32";
        return " --load-mode mmap --lazy-mode on --no-warmup -b 32 -ub 16 --mtmd-batch-max-tokens 16";
    }
    public static void CheckHost() {
        if(Environment.OSVersion.Platform==PlatformID.Win32NT&&Environment.OSVersion.Version.Major<6)throw new IOException("Windows XP and earlier are not supported by this build. Disk-backed loading does not change operating-system requirements.");
    }
}
sealed class GgufSet {
    public string First;
    public string[] Parts;
    public long Bytes;
    public static GgufSet Inspect(string selected) {
        string path=Path.GetFullPath(selected);var match=Regex.Match(Path.GetFileName(path),@"^(.*)-(\d{5})-of-(\d{5})\.gguf$",RegexOptions.IgnoreCase);
        var paths=new List<string>();
        if(match.Success) {
            int part=int.Parse(match.Groups[2].Value),count=int.Parse(match.Groups[3].Value);
            if(count<1||count>4096||part<1||part>count)throw new IOException("Invalid GGUF split numbering.");
            for(int i=1;i<=count;i++)paths.Add(Path.Combine(Path.GetDirectoryName(path),match.Groups[1].Value+"-"+i.ToString("D5")+"-of-"+count.ToString("D5")+".gguf"));
        } else paths.Add(path);
        long bytes=0;
        foreach(string item in paths) {
            if(!File.Exists(item))throw new FileNotFoundException("Missing GGUF part: "+Path.GetFileName(item)+". Keep the complete set together with its original names.",item);
            using(var stream=File.OpenRead(item)){byte[] magic=new byte[4];if(stream.Read(magic,0,4)!=4||Encoding.ASCII.GetString(magic)!="GGUF")throw new IOException("Not a GGUF file: "+Path.GetFileName(item));bytes=checked(bytes+stream.Length);}
        }
        return new GgufSet {First=paths[0],Parts=paths.ToArray(),Bytes=bytes};
    }
    public string Describe() {return "Entry file: "+First+"\r\nParts: "+Parts.Length+"\r\nTotal model files: "+(Bytes/1073741824.0).ToString("0.00")+" GiB\r\n\r\nAll named parts exist and have GGUF headers. The runtime validates tensor/metadata compatibility when loading.\r\nSplit files remain one complete model. Splitting alone does not reduce working RAM. Disk-backed mode uses OS paging, not a fixed RAM limit.";}
}
}
