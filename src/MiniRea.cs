using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BoltWorks.LlamaLite {
// Deliberately independent of the app, Node, network libraries and external processes.
static class MiniRea {
    public const long MaxBytes=256L*1024*1024;
    public sealed class Finding {
        public long offset; public string format,detail; public long? declaredBytes;
    }
    public sealed class Report {
        public long fileBytes,scannedBytes;public string sha256;public bool hitLimit;
        public List<Finding> findings=new List<Finding>();
        public override string ToString(){var s=new StringBuilder("BoltWorks Mini REA 0.5 - offline static inspection\r\n");s.AppendLine("Bytes: "+fileBytes+"; scanned: "+scannedBytes+"; SHA-256: "+sha256);s.AppendLine("Signature candidates are not proof of valid files, flash partitions or device compatibility.");foreach(var f in findings)s.AppendLine("0x"+f.offset.ToString("X8")+"  "+f.format+"  "+f.detail+(f.declaredBytes.HasValue?"; declared bytes: "+f.declaredBytes.Value:""));if(hitLimit)s.AppendLine("STOPPED: 256 finding limit reached; scan is incomplete.");s.AppendLine("No target execution, network access, decompression, disassembly or flashing performed.");return s.ToString();}
    }
    static uint U32(byte[] b,int p,bool be){return be?(uint)(b[p]<<24|b[p+1]<<16|b[p+2]<<8|b[p+3]):(uint)(b[p]|b[p+1]<<8|b[p+2]<<16|b[p+3]<<24);}
    static int U16(byte[] b,int p,bool be){return be?b[p]<<8|b[p+1]:b[p]|b[p+1]<<8;}
    static ulong U64(byte[] b,int p){return U32(b,p,false)|((ulong)U32(b,p+4,false)<<32);}
    static bool Magic(byte[] b,int p,int n,params byte[] m){if(p>n-m.Length)return false;for(int i=0;i<m.Length;i++)if(b[p+i]!=m[i])return false;return true;}
    static string Machine(int value){switch(value){case 3:return "x86";case 8:return "MIPS";case 20:return "PowerPC";case 40:return "ARM";case 62:return "x64";case 183:return "AArch64";case 243:return "RISC-V";default:return "machine "+value;}}
    static Finding Detect(byte[] b,int p,int n,long offset,long length) {
        string kind=null,detail=null;long? size=null;int left=n-p;
        if(b[p]==0x7f&&Magic(b,p,n,0x7f,69,76,70)) {
            kind="ELF";detail="truncated/invalid identification header";
            if(left>=16&&(b[p+4]==1||b[p+4]==2)&&(b[p+5]==1||b[p+5]==2)&&b[p+6]==1) {
                bool be=b[p+5]==2;int h=b[p+4]==1?52:64;
                detail=left>=h?((h==52?"32":"64")+"-bit "+(be?"big":"little")+" endian; "+Machine(U16(b,p+18,be))+"; header only, executable extent not inferred"):"truncated ELF header";
            }
        }else if(b[p]==0x68&&Magic(b,p,n,0x68,0x73,0x71,0x73)) {
            kind="SquashFS";detail="magic only; truncated superblock";
            if(left>=96) {
                ulong used=U64(b,p+40);uint block=U32(b,p+12,false);int major=U16(b,p+28,false),minor=U16(b,p+30,false);
                bool plausible=major==4&&minor==0&&block>=4096&&block<=1048576&&(block&(block-1))==0&&used>=96&&used<=(ulong)(length-offset);
                detail="version "+major+"."+minor+"; compression ID "+U16(b,p+20,false)+"; "+(plausible?"basic header/range checks passed; filesystem not validated":"invalid/unsupported header or out-of-file extent");
                if(plausible)size=(long)used;
            }
        }else if(b[p]==0x27&&Magic(b,p,n,0x27,0x05,0x19,0x56)) {
            kind="U-Boot uImage";detail="magic only; truncated 64-byte header";
            if(left>=64) {
                long bytes=64L+U32(b,p+12,true);uint crc=0xffffffff;
                for(int i=0;i<64;i++){crc^=(uint)(i>=4&&i<8?0:b[p+i]);for(int j=0;j<8;j++)crc=(crc>>1)^((crc&1)!=0?0xedb88320U:0);}
                bool valid=(crc^0xffffffff)==U32(b,p+4,true);bool fits=bytes<=length-offset;
                detail="OS ID "+b[p+28]+"; arch ID "+b[p+29]+"; compression ID "+b[p+31]+"; header CRC "+(valid?"matches":"MISMATCH")+"; "+(fits?"extent in file":"extent OUTSIDE file")+"; payload CRC not checked";
                if(valid&&fits)size=bytes;
            }
        }else if(b[p]==0x1f&&Magic(b,p,n,0x1f,0x8b,8)){kind="gzip";detail=left>=10&&(b[p+3]&0xe0)==0?"basic header only; compressed extent unknown":"truncated/invalid flags";}
        else if(b[p]==0xfd&&Magic(b,p,n,0xfd,0x37,0x7a,0x58,0x5a,0)){kind="XZ";detail="magic only; extent unknown";}
        else if(b[p]==0x55&&Magic(b,p,n,0x55,0x42,0x49,0x23)){kind="UBI erase-count header";detail="magic only; erase-block layout not inferred";}
        else if(b[p]==0x50&&Magic(b,p,n,0x50,0x4b,3,4)){kind="ZIP local header";detail="magic only; archive extent unknown";}
        return kind==null?null:new Finding{offset=offset,format=kind,detail=detail,declaredBytes=size};
    }
    public static Report Scan(string path,CancellationToken cancel) {
        using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            if(file.Length>MaxBytes)throw new IOException("Mini REA limits inputs to 256 MiB.");
            var report=new Report{fileBytes=file.Length};
            using(var sha=SHA256.Create()) {
                var chunk=new byte[65536];int count;
                while((count=file.Read(chunk,0,chunk.Length))>0){cancel.ThrowIfCancellationRequested();sha.TransformBlock(chunk,0,count,chunk,0);}
                sha.TransformFinalBlock(chunk,0,0);report.sha256=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
            }
            file.Position=0;const int Block=65536,Overlap=128;var buffer=new byte[Block+Overlap];long position=0;
            while(position<file.Length) {
                cancel.ThrowIfCancellationRequested();file.Position=position;int n=0,read;
                while(n<buffer.Length&&(read=file.Read(buffer,n,buffer.Length-n))>0)n+=read;
                int process=(int)Math.Min(Block,file.Length-position);
                for(int p=0;p<process;p++) {
                    if((p&4095)==0)cancel.ThrowIfCancellationRequested();
                    var found=Detect(buffer,p,n,position+p,file.Length);report.scannedBytes=position+p+1;
                    if(found!=null){report.findings.Add(found);if(report.findings.Count==256){report.hitLimit=report.scannedBytes<file.Length;return report;}}
                }
                position+=process;
            }
            return report;
        }
    }
    public static string Carve(string input,long offset,long length,string output,CancellationToken cancel) {
        if(offset<0||length<=0||length>MaxBytes)throw new ArgumentException("Use a nonnegative offset and length 1..256 MiB (decimal bytes).");
        using(var source=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            if(source.Length>MaxBytes||offset>source.Length||length>source.Length-offset)throw new IOException("Requested range is outside input or input exceeds 256 MiB.");
            cancel.ThrowIfCancellationRequested();source.Position=offset;
            // New-file semantics also reject same-source paths and hard-link aliases.
            using(var destination=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                var buffer=new byte[65536];long remaining=length;
                while(remaining>0){cancel.ThrowIfCancellationRequested();int n=source.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(n==0)throw new EndOfStreamException();destination.Write(buffer,0,n);remaining-=n;}
            }
        }
        return Path.GetFullPath(output);
    }
}
// Standalone edition: compiled only with MiniRea.cs, without the inference/UI assemblies.
static class MiniReaCli {
    static int Main(string[] args) {
        using(var cancel=new CancellationTokenSource()) {
            Console.CancelKeyPress+=delegate(object sender,ConsoleCancelEventArgs e){e.Cancel=true;cancel.Cancel();};
            try {
                if(args.Length==3&&args[0]=="decompile"){Console.WriteLine(DecompilerRunner.Run(args[1],args[2],cancel.Token));return 0;}
                int replaceIndex;
                if(args.Length==5&&args[0]=="replace-resource"&&int.TryParse(args[2],out replaceIndex)){Console.WriteLine(PeInspector.ReplaceResource(args[1],replaceIndex,args[3],args[4],cancel.Token));return 0;}
                if(args.Length==2&&args[0]=="pe"){PeInspector.InspectTo(args[1],Console.Out,cancel.Token);return 0;}
                int resourceIndex;
                if(args.Length==4&&args[0]=="resource"&&int.TryParse(args[2],out resourceIndex)){Console.WriteLine(PeInspector.Extract(args[1],resourceIndex,args[3],cancel.Token));return 0;}
                if(args.Length==1&&args[0]=="network"){Console.Write(MiniReaNetwork.Inventory());return 0;}
                if((args.Length==2||args.Length==3)&&args[0]=="probe"){Console.Write(MiniReaNetwork.Probe(args[1],args.Length==3?args[2]:null,cancel.Token));return 0;}
                if(args.Length==2&&args[0]=="scan"){Console.Write(MiniRea.Scan(args[1],cancel.Token));return 0;}
                long offset,length;
                if(args.Length==5&&args[0]=="carve"&&long.TryParse(args[2],out offset)&&long.TryParse(args[3],out length)){Console.WriteLine(MiniRea.Carve(args[1],offset,length,args[4],cancel.Token));return 0;}
                Console.WriteLine("BoltWorks Mini REA 0.5\ndecompile FILE EXISTING_PARENT_FOLDER\nreplace-resource FILE INDEX RAW_DATA NEW_OUTPUT\npe FILE\nresource FILE INDEX NEW_OUTPUT\nnetwork\nprobe DEVICE_IPV4 [LAPTOP_IPV4]\nscan FILE\ncarve FILE OFFSET LENGTH NEW_OUTPUT\nOffsets/lengths: decimal bytes. Carving copies raw bytes; it does not decompress.\nOffline .NET Framework tool, no Node. PE inspection has no file-size cap; scan/carve inputs up to 256 MiB.\nFailed/cancelled carving may leave a partial output; retry with a NEW filename.");return args.Length==0?0:2;
            }catch(OperationCanceledException){Console.Error.WriteLine("Cancelled. Partial carve output may remain.");return 130;}
            catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
        }
    }
}
}
