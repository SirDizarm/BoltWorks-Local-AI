using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace BoltWorks.LlamaLite {
static class RuntimePrerequisites {
    public static void Check(string executable) {
        if(!Environment.Is64BitProcess) {
            // Our x86 runtime uses static MinGW libraries. Verify that this is
            // an x86 executable before letting Windows try to launch it.
            using(var binary=new BinaryReader(File.OpenRead(executable))) {
                var stream=binary.BaseStream;
                if(stream.Length>=64 && binary.ReadUInt16()==0x5a4d) {
                    stream.Position=0x3c;
                    int peOffset=binary.ReadInt32();
                    if(peOffset>=64 && peOffset<=stream.Length-6) {
                        stream.Position=peOffset;
                        if(binary.ReadUInt32()==0x4550 && binary.ReadUInt16()==0x14c) return;
                    }
                }
            }
            throw new IOException("The 32-bit app needs an x86 runtime. Extract the complete x86 package into a separate folder.");
        }
        string folder=Path.GetDirectoryName(executable);
        foreach(string name in new string[]{"msvcp140.dll","vcruntime140.dll","vcruntime140_1.dll"}) {
            if(!File.Exists(Path.Combine(folder,name)) && !File.Exists(Path.Combine(Environment.SystemDirectory,name)))
                throw new FileNotFoundException("Microsoft C++ runtime file "+name+" is missing. Extract the ENTIRE version 0.6.1 or later ZIP, including its runtime folders. Alternatively install Microsoft's free x64 C++ Redistributable: https://aka.ms/vc14/vc_redist.x64.exe . This dependency error is not an out-of-RAM error.");
        }
    }
}
static class MemoryReport {
    [StructLayout(LayoutKind.Sequential)] struct Status {public uint Length,Load; public ulong TotalPhysical,AvailablePhysical,TotalCommit,AvailableCommit,TotalVirtual,AvailableVirtual,Extended;}
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref Status value);
    static string lastError="No failure recorded in this process.";
    public static string FilePath {get {return Path.Combine(LocalFiles.Root,"last-error.txt");}}
    static string GiB(ulong bytes) {return (bytes/1073741824.0).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+" GiB";}
    public static string Summary() {
        var status=new Status(); status.Length=(uint)Marshal.SizeOf(status);
        if(!GlobalMemoryStatusEx(ref status)) return "Windows memory information unavailable.";
        return "Physical RAM: "+GiB(status.AvailablePhysical)+" available / "+GiB(status.TotalPhysical)+" total\r\nWindows commit allowance: "+GiB(status.AvailableCommit)+" available / "+GiB(status.TotalCommit)+" total (RAM plus paging-file backing, not GPU memory)";
    }
    public static string Model(string path) {if(string.IsNullOrWhiteSpace(path)) return "None"; try {return path+" ("+GiB((ulong)new FileInfo(path).Length)+" on disk; runtime needs extra working memory)";} catch {return path+" (file unavailable)";} }
    public static void Record(string operation,Exception error,string details) {
        lastError=FormatFailure(operation,error,details);
        try {Directory.CreateDirectory(LocalFiles.Root); File.WriteAllText(FilePath,lastError,new UTF8Encoding(false));} catch {}
    }
    public static string FormatFailure(string operation,Exception error,string details) {return "Time: "+DateTimeOffset.Now.ToString("o")+"\r\nOperation: "+operation+"\r\n"+Summary()+"\r\n"+details+"\r\n\r\n"+error;}
    public static string Build(string details,string runtimeLog) {
        string previous=lastError; if(previous=="No failure recorded in this process.") try {if(File.Exists(FilePath) && new FileInfo(FilePath).Length<250000) previous=File.ReadAllText(FilePath);} catch {}
        int windows=0,models=0,pictures=0; try {foreach(var process in Process.GetProcesses()) using(process) {try {if((process.ProcessName=="BoltWorks-LlamaLite"||process.ProcessName=="BoltWorks-Local-AI")) windows++; else if(process.ProcessName=="llama-server") models++; else if(process.ProcessName=="sd-cli") pictures++;} catch {}}} catch {}
        return "BoltWorks Local AI 0.6.19 memory / failure report\r\n"+Summary()+"\r\nMatching process names (including other installations): windows="+windows+", chat runtimes="+models+", picture runtimes="+pictures+"\r\n"+details+"\r\n\r\nLAST ERROR\r\n"+previous+"\r\n\r\nRUNTIME LOG\r\n"+(string.IsNullOrWhiteSpace(runtimeLog)?"No runtime log captured. A failure before process startup can still appear above.":runtimeLog)+"\r\n\r\nLocal report file: "+FilePath;
    }
    public static void Show(IWin32Window owner,string details,string log) {
        using(var view=new Form {Text="Memory and failure report — Copy report to share it",Size=new Size(900,600),StartPosition=FormStartPosition.CenterParent}) {
            var text=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Text=Build(details,log)};
            var copy=new Button {Text="Copy report",Dock=DockStyle.Bottom,Height=42}; copy.Click+=delegate {try {Clipboard.SetText(text.Text); copy.Text="Copied";} catch(Exception e) {MessageBox.Show(view,e.Message);}};
            view.Controls.Add(text); view.Controls.Add(copy); view.ShowDialog(owner);
        }
    }
}
}




