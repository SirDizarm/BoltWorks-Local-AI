using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BoltWorks.LlamaLite {
sealed partial class PeInspector {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr BeginUpdateResource(string name,bool deleteExisting);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool UpdateResource(IntPtr update,IntPtr type,IntPtr name,ushort language,byte[] data,uint count);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool EndUpdateResource(IntPtr update,bool discard);
    static IntPtr NativeKey(Key key){if(key.name!=null){if(key.name.Length==0||key.name.IndexOf('\0')>=0||key.name[0]=='#')throw new IOException("Unsupported empty, NUL or # resource name.");return Marshal.StringToHGlobalUni(key.name);}if(key.id>65535)throw new IOException("Resource identifier exceeds Win32 range.");return new IntPtr((int)key.id);}
    static bool SameKey(Key a,Key b){return a.name==b.name&&(a.name!=null||a.id==b.id);}
    public static string ReplaceResource(string input,int index,string replacement,string output,CancellationToken cancel){
        output=Path.GetFullPath(output);if(File.Exists(output)||Directory.Exists(output))throw new IOException("Choose a NEW output executable; existing paths are never overwritten.");
        using(var source=new PeInspector(input,cancel)) {
            source.Run();if(source.incomplete||index<0||index>=source.resources.Count)throw new IOException("Resource index missing or inspection incomplete; replacement refused.");
            if(source.sizes[4]!=0||source.overlayBytes!=0)throw new IOException("This first resource editor does not handle certificate tables or trailing overlay data. Original is unchanged.");
            var resource=source.resources[index];if(resource.keys.Count!=3||resource.keys[2].name!=null||resource.keys[2].id>65535)throw new IOException("Resource must have a standard type/name/numeric-language path.");
            byte[] data;using(var payload=new FileStream(replacement,FileMode.Open,FileAccess.Read,FileShare.Read)){if(payload.Length==0||payload.Length>16*1024*1024)throw new IOException("Replacement must contain 1 byte to 16 MiB of raw resource data.");data=new byte[(int)payload.Length];int done=0;while(done<data.Length){cancel.ThrowIfCancellationRequested();int n=payload.Read(data,done,data.Length-done);if(n==0)throw new EndOfStreamException();done+=n;}}
            string temporary=Path.Combine(Path.GetDirectoryName(output),".bwl-resource-"+Guid.NewGuid().ToString("N")+".tmp");bool ownTemp=false;
            IntPtr type=IntPtr.Zero,name=IntPtr.Zero,update=IntPtr.Zero;
            try {
                type=NativeKey(resource.keys[0]);name=NativeKey(resource.keys[1]);cancel.ThrowIfCancellationRequested();
                using(var copy=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){ownTemp=true;source.file.Position=0;var buffer=new byte[65536];int n;while((n=source.file.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();copy.Write(buffer,0,n);}}
                update=BeginUpdateResource(temporary,false);if(update==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
                if(!UpdateResource(update,type,name,(ushort)resource.keys[2].id,data,(uint)data.Length))throw new Win32Exception(Marshal.GetLastWin32Error());
                cancel.ThrowIfCancellationRequested();bool committed=EndUpdateResource(update,false);update=IntPtr.Zero;if(!committed)throw new Win32Exception(Marshal.GetLastWin32Error());
                using(var check=new PeInspector(temporary,cancel)) {
                    check.Run();if(check.incomplete)throw new IOException("Edited image failed structural inspection.");
                    Resource found=null;foreach(var item in check.resources)if(item.keys.Count==3&&SameKey(item.keys[0],resource.keys[0])&&SameKey(item.keys[1],resource.keys[1])&&SameKey(item.keys[2],resource.keys[2])){if(found!=null)throw new IOException("Ambiguous resource after update.");found=item;}
                    if(found==null||found.size!=data.Length)throw new IOException("Edited resource failed verification.");
                    check.file.Position=found.offset;for(int i=0;i<data.Length;i++){if((i&65535)==0)cancel.ThrowIfCancellationRequested();if(check.file.ReadByte()!=data[i])throw new IOException("Edited resource bytes differ.");}
                    if(check.resources.Count!=source.resources.Count)throw new IOException("Resource count changed unexpectedly.");
                    foreach(var original in source.resources){
                        if(object.ReferenceEquals(original,resource))continue;Resource preserved=null;
                        foreach(var candidate in check.resources)if(candidate.keys.Count==original.keys.Count){bool match=true;for(int k=0;k<original.keys.Count;k++)if(!SameKey(candidate.keys[k],original.keys[k]))match=false;if(match){if(preserved!=null)throw new IOException("Ambiguous preserved resource.");preserved=candidate;}}
                        if(preserved==null||preserved.size!=original.size)throw new IOException("An unselected resource changed.");
                        source.file.Position=original.offset;check.file.Position=preserved.offset;
                        for(uint k=0;k<original.size;k++){if((k&65535)==0)cancel.ThrowIfCancellationRequested();if(source.file.ReadByte()!=check.file.ReadByte())throw new IOException("An unselected resource changed bytes.");}
                    }
                }
                cancel.ThrowIfCancellationRequested();File.Move(temporary,output);ownTemp=false;
                return "Replaced "+resource.path+" with "+data.Length+" bytes in NEW copy: "+output+". Original unchanged. Structural/byte checks passed; runtime behaviour and signatures are not verified.";
            }finally{
                if(update!=IntPtr.Zero)EndUpdateResource(update,true);
                if(resource.keys[0].name!=null&&type!=IntPtr.Zero)Marshal.FreeHGlobal(type);
                if(resource.keys[1].name!=null&&name!=IntPtr.Zero)Marshal.FreeHGlobal(name);
                if(ownTemp)try{File.Delete(temporary);}catch{}
            }
        }
    }
}
}
