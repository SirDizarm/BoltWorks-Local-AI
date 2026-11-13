using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
sealed class PeInspectorWindow:Form {
    readonly TextBox path=new TextBox {Dock=DockStyle.Fill,ReadOnly=true};
    readonly TextBox report=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false};
    readonly Button choose=new Button {Text="Choose EXE / DLL",AutoSize=true},extract=new Button {Text="Extract resource",AutoSize=true,Enabled=false},stop=new Button {Text="Stop",AutoSize=true,Enabled=false};
    readonly Button replace=new Button {Text="Replace in copy",AutoSize=true,Enabled=false},decompile=new Button {Text="Decompile .NET",AutoSize=true,Enabled=false};
    readonly NumericUpDown index=new NumericUpDown {Minimum=0,Maximum=999999,Width=65};
    readonly Button save=new Button {Text="Save full report",AutoSize=true,Enabled=false};
    readonly TextBox search=new TextBox {Width=200};
    readonly Button next=new Button {Text="Next",AutoSize=true,Enabled=false},previous=new Button {Text="Previous",AutoSize=true,Enabled=false};
    readonly CheckBox matchCase=new CheckBox {Text="Match case",AutoSize=true};
    readonly Label matches=new Label {AutoSize=true,Text="Search displayed report",Margin=new Padding(4,7,0,0)};
    readonly List<int> hits=new List<int>();int currentHit=-1;
    readonly ResourceBrowser browser;readonly BinaryStringsBrowser binarySearch=new BinaryStringsBrowser();
    CancellationTokenSource active;bool closing;string inspected,fingerprint;
    public PeInspectorWindow(){
        Text="Mini REA - EXE / DLL deep inspector";Size=new Size(960,680);MinimumSize=new Size(800,520);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;
        var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12)};grid.RowStyles.Add(new RowStyle(SizeType.Absolute,48));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,60));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,80));Controls.Add(grid);
        var row=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));row.Controls.Add(path,0,0);row.Controls.Add(choose,1,0);grid.Controls.Add(row,0,0);
        grid.Controls.Add(new Label {Dock=DockStyle.Fill,Text="Inspect without running the target. Extract raw Windows resources or replace one in a NEW copy. Decompile .NET produces reconstructed C# in a new folder; native machine-code decompilation is not included."},0,1);var tabs=new TabControl {Dock=DockStyle.Fill};var details=new TabPage("Inspection report");details.Controls.Add(report);var searchRow=new FlowLayoutPanel {Dock=DockStyle.Top,Height=66};searchRow.Controls.AddRange(new Control[]{new Label {Text="Find",AutoSize=true,Margin=new Padding(0,7,0,0)},search,previous,next,matchCase,matches});details.Controls.Add(searchRow);tabs.TabPages.Add(details);
        report.HideSelection=false;search.TextChanged+=delegate{FindMatches();};matchCase.CheckedChanged+=delegate{FindMatches();};report.TextChanged+=delegate{FindMatches();};
        next.Click+=delegate{MoveMatch(1);};previous.Click+=delegate{MoveMatch(-1);};
        search.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){MoveMatch(e.Shift?-1:1);e.SuppressKeyPress=true;}};
        KeyPreview=true;KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.F){tabs.SelectedTab=details;search.Focus();search.SelectAll();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.F3){tabs.SelectedTab=details;MoveMatch(e.Shift?-1:1);e.SuppressKeyPress=true;}};browser=new ResourceBrowser(delegate(int value){index.Value=value;});var assets=new TabPage("Resources / images / scripts");assets.Controls.Add(browser);tabs.TabPages.Add(assets);var stringsPage=new TabPage("Binary text / URLs / paths");stringsPage.Controls.Add(binarySearch);tabs.TabPages.Add(stringsPage);grid.Controls.Add(tabs,0,2);
        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};actions.Controls.AddRange(new Control[]{new Label {Text="Resource #",AutoSize=true,Margin=new Padding(0,6,0,0)},index,extract,replace,decompile,save,stop});grid.Controls.Add(actions,0,3);
        report.Text="Choose a file to inspect. To discuss the report with your local model, use Work on code > Inspect binary / firmware to create an analysis project.\r\nResource indexes and byte offsets appear below after inspection. Extracted icon/bitmap data may require reconstruction before an image editor can open it.";
        choose.Click+=async delegate {using(var picker=new OpenFileDialog {Filter="Windows programs|*.exe;*.dll;*.sys;*.ocx;*.scr|All files|*.*",Title="Choose a binary to inspect without executing it"})if(picker.ShowDialog(this)==DialogResult.OK){path.Text=picker.FileName;binarySearch.SetFile(picker.FileName);inspected=null;await Work(false,null);}};
        extract.Click+=async delegate {if(inspected==null)return;using(var picker=new SaveFileDialog {Title="Save raw resource to a NEW file",FileName="resource-"+index.Value+".bin",Filter="Raw resource|*.bin|All files|*.*",OverwritePrompt=false})if(picker.ShowDialog(this)==DialogResult.OK)await Work(true,picker.FileName);};
        replace.Click+=async delegate {
            if(inspected==null)return;string payload;
            using(var picker=new OpenFileDialog {Title="Choose replacement RAW resource data (not a complete ICO/BMP container)",Filter="All files|*.*"}){if(picker.ShowDialog(this)!=DialogResult.OK)return;payload=picker.FileName;}
            using(var picker=new SaveFileDialog {Title="Save a NEW executable copy",FileName=Path.GetFileNameWithoutExtension(path.Text)+"-edited"+Path.GetExtension(path.Text),Filter="All files|*.*",OverwritePrompt=false})if(picker.ShowDialog(this)==DialogResult.OK)await WorkAction(2,picker.FileName,payload);
        };
        decompile.Click+=async delegate {if(inspected==null)return;using(var picker=new FolderBrowserDialog {Description="Choose a parent folder for a NEW C# decompilation project"})if(picker.ShowDialog(this)==DialogResult.OK)await WorkAction(3,picker.SelectedPath,null);};
        save.Click+=async delegate {if(inspected==null)return;using(var picker=new SaveFileDialog {Title="Save complete inspection report to a NEW file",FileName=Path.GetFileName(path.Text)+"-inspection.txt",Filter="Text report|*.txt",OverwritePrompt=false})if(picker.ShowDialog(this)==DialogResult.OK)await WorkAction(4,picker.FileName,null);};
        stop.Click+=delegate{if(active!=null)active.Cancel();};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(active!=null){closing=true;active.Cancel();e.Cancel=true;}};
    }
    void FindMatches(){
        hits.Clear();currentHit=-1;string query=search.Text,body=report.Text;
        if(query.Length>0){var comparison=matchCase.Checked?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;int at=0;while(at<=body.Length-query.Length){int found=body.IndexOf(query,at,comparison);if(found<0)break;hits.Add(found);at=found+query.Length;}}
        next.Enabled=previous.Enabled=hits.Count>0;
        if(hits.Count>0){currentHit=0;ShowMatch();}else{report.Select(0,0);matches.Text=query.Length==0?"Search displayed report":"No matches in displayed report";}
    }
    void MoveMatch(int direction){if(hits.Count==0)return;currentHit=(currentHit+direction+hits.Count)%hits.Count;ShowMatch();}
    void ShowMatch(){report.Select(hits[currentHit],search.Text.Length);report.ScrollToCaret();matches.Text=(currentHit+1)+" of "+hits.Count+" (displayed report)";}
    Task Work(bool exporting,string output){return WorkAction(exporting?1:0,output,null);}
    async Task WorkAction(int action,string output,string payload){
        if(active!=null)return;bool exporting=action!=0;string input=path.Text;int selected=(int)index.Value;active=new CancellationTokenSource();choose.Enabled=extract.Enabled=replace.Enabled=decompile.Enabled=save.Enabled=index.Enabled=false;stop.Enabled=true;
        try {
            string hash=null;
            string result=await Task.Run(()=>{
                using(var guard=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    using(var sha=SHA256.Create()) {var buffer=new byte[65536];int count;while((count=guard.Read(buffer,0,buffer.Length))>0){active.Token.ThrowIfCancellationRequested();sha.TransformBlock(buffer,0,count,buffer,0);}sha.TransformFinalBlock(buffer,0,0);hash=BitConverter.ToString(sha.Hash);}
                    if(exporting&&hash!=fingerprint)throw new IOException("The input changed since inspection. Choose and inspect it again before extracting a resource.");
                    if(action==4)return PeInspector.SaveReport(input,output,active.Token);
                    if(action==1)return PeInspector.Extract(input,selected,output,active.Token);
                    if(action==2)return PeInspector.ReplaceResource(input,selected,payload,output,active.Token);
                    if(action==3)return "C# decompilation saved to: "+DecompilerRunner.Run(input,output,active.Token)+"\r\nChoose that folder in Work on code to review/edit it. Original source and a working rebuild are not guaranteed.";
                    return PeInspector.Inspect(input,active.Token);
                }
            });
            if(exporting)report.AppendText("\r\n"+result+"\r\n");else{report.Text=result;inspected=input;fingerprint=hash;await browser.LoadFile(input,active.Token);}
        }catch(OperationCanceledException){report.AppendText("\r\nCancelled. A partial extraction or report may remain; retry with a new filename.\r\n");}
        catch(Exception e){report.AppendText("\r\nError: "+e.Message+"\r\n");}
        finally{active.Dispose();active=null;choose.Enabled=index.Enabled=true;extract.Enabled=replace.Enabled=decompile.Enabled=save.Enabled=inspected!=null;stop.Enabled=false;if(closing)Close();}
    }
}
}
