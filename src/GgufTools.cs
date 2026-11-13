using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace BoltWorks.LlamaLite {
static class GgufTools {
    public static async Task<GgufSet> Split(string model,string folder,int megabytes,CancellationToken cancel) {
        if(megabytes<64||megabytes>65536)throw new IOException("Split size must be 64–65536 MB.");
        var source=GgufSet.Inspect(model);if(source.Parts.Length!=1)throw new IOException("Choose an unsplit GGUF as input. Existing split sets already load directly.");
        folder=Path.GetFullPath(folder);if(Directory.Exists(folder)&&Directory.GetFileSystemEntries(folder).Length>0)throw new IOException("Use a new or empty output folder. Existing files are preserved.");
        string tool=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","llama-gguf-split.exe");
        if(!File.Exists(tool))throw new FileNotFoundException("The matching llama-gguf-split.exe is missing from runtime. Extract the complete updated package.");
        if(!Environment.Is64BitProcess) RuntimePrerequisites.Check(tool);
        Directory.CreateDirectory(folder);string prefix=Path.Combine(folder,Path.GetFileNameWithoutExtension(source.First));
        var start=new ProcessStartInfo(tool,"--split --split-max-size "+megabytes+"M "+LocalFiles.Quote(source.First)+" "+LocalFiles.Quote(prefix)) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=folder};
        using(var process=new Process {StartInfo=start}) {
            cancel.ThrowIfCancellationRequested();process.Start();ProcessJob job=null;
            try {
                job=new ProcessJob(process);var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                while(!process.HasExited)await Task.Delay(100,cancel);await Task.WhenAll(output,error);
                File.WriteAllText(Path.Combine(folder,"split-log.txt"),output.Result+error.Result);
                if(process.ExitCode!=0)throw new IOException("GGUF splitting failed (exit "+process.ExitCode+"). See split-log.txt. A tensor may exceed the chosen part size; try a larger size in a new folder.");
            }finally{if(job!=null)job.Dispose();try{if(!process.HasExited)process.Kill();}catch{}}
        }
        cancel.ThrowIfCancellationRequested();string[] first=Directory.GetFiles(folder,Path.GetFileName(prefix)+"-00001-of-*.gguf");
        if(first.Length!=1)throw new IOException("Splitter did not produce a recognizable GGUF set. See split-log.txt.");
        var result=GgufSet.Inspect(first[0]);LocalFiles.SaveJson(Path.Combine(folder,"split-info.json"),new {source=source.First,first=result.First,parts=result.Parts,totalBytes=result.Bytes,requestedPartMB=megabytes,reducesWorkingRam=false});return result;
    }
}
sealed class SplitModelWindow:Form {
    public SplitModelWindow() {
        Text="Create GGUF split files";Size=new Size(740,420);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
        var input=new TextBox {Dock=DockStyle.Fill,ReadOnly=true};var folder=new TextBox {Dock=DockStyle.Fill,ReadOnly=true};
        var size=new NumericUpDown {Minimum=64,Maximum=65536,Value=512,Width=100};var run=new Button {Text="Create split files",AutoSize=true};var stop=new Button {Text="Stop",AutoSize=true,Enabled=false};
        var status=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(12)};foreach(int height in new[]{65,42,42,42})grid.RowStyles.Add(new RowStyle(SizeType.Absolute,height));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(grid);
        grid.Controls.Add(new Label {Dock=DockStyle.Fill,Text="Creates new GGUF shards without changing the model weights or your source file. Splitting alone does not save RAM. Use Disk-backed CPU mode for experimental paging."},0,0);
        Action<TextBox,string,int,Action> picker=delegate(TextBox text,string label,int row,Action action){var panel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));var button=new Button {Text=label,AutoSize=true};button.Click+=delegate{action();};panel.Controls.Add(text,0,0);panel.Controls.Add(button,1,0);grid.Controls.Add(panel,0,row);};
        picker(input,"Choose GGUF",1,delegate {using(var dialog=new OpenFileDialog {Filter="Unsplit GGUF|*.gguf"})if(dialog.ShowDialog(this)==DialogResult.OK)input.Text=dialog.FileName;});
        picker(folder,"Output folder",2,delegate {using(var dialog=new FolderBrowserDialog())if(dialog.ShowDialog(this)==DialogResult.OK)folder.Text=dialog.SelectedPath;});
        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};actions.Controls.AddRange(new Control[]{new Label {Text="Target part size (MB)",AutoSize=true},size,run,stop});grid.Controls.Add(actions,0,3);grid.Controls.Add(status,0,4);
        CancellationTokenSource active=null;bool wantClose=false;
        stop.Click+=delegate{if(active!=null)active.Cancel();};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(active!=null){e.Cancel=true;wantClose=true;active.Cancel();}};
        run.Click+=async delegate {
            if(active!=null)return;if(!File.Exists(input.Text)||!Directory.Exists(folder.Text)){status.Text="Choose an existing GGUF and destination folder.";return;}
            string destination=Path.Combine(folder.Text,"gguf-splits-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
            active=new CancellationTokenSource();grid.GetControlFromPosition(0,1).Enabled=grid.GetControlFromPosition(0,2).Enabled=run.Enabled=size.Enabled=false;stop.Enabled=true;status.Text="Writing new split files to "+destination;
            try{var set=await GgufTools.Split(input.Text,destination,(int)size.Value,active.Token);status.Text=set.Describe()+"\r\nChoose any part in a text-model slot; all parts must stay together.";}
            catch(OperationCanceledException){status.Text="Stopped. The original model is unchanged. Partial output remains in "+destination;}
            catch(Exception e){status.Text=e.Message+"\r\nOutput: "+destination;}
            finally{active.Dispose();active=null;grid.GetControlFromPosition(0,1).Enabled=grid.GetControlFromPosition(0,2).Enabled=run.Enabled=size.Enabled=true;stop.Enabled=false;if(wantClose)Close();}
        };
    }
}
}
