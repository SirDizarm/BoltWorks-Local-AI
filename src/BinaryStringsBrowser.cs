using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace BoltWorks.LlamaLite {
sealed class BinaryStringsBrowser:UserControl {
    readonly TextBox query=new TextBox {Width=230,MaxLength=256};
    readonly ComboBox category=new ComboBox {Width=95,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly CheckBox matchCase=new CheckBox {Text="Match case",AutoSize=true};
    readonly Button scan=new Button {Text="Search binary",AutoSize=true,Enabled=false},export=new Button {Text="Export all matches",AutoSize=true,Enabled=false},stop=new Button {Text="Stop",AutoSize=true,Enabled=false};
    readonly Label status=new Label {Dock=DockStyle.Bottom,Height=48,Text="Choose a binary, then search. Blank search lists all readable strings. URLs and paths are candidates; nothing is opened or executed."};
    readonly ListView list=new ListView {Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,VirtualMode=true,HideSelection=false};
    readonly TextBox detail=new TextBox {Dock=DockStyle.Bottom,Height=90,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false};
    List<BinaryStrings.Hit> hits=new List<BinaryStrings.Hit>();CancellationTokenSource active;string path;int revision;
    internal BinaryStringsBrowser(){
        Dock=DockStyle.Fill;category.Items.AddRange(new object[]{"All","URLs","Paths"});category.SelectedIndex=0;
        var row=new FlowLayoutPanel {Dock=DockStyle.Top,Height=74};row.Controls.AddRange(new Control[]{new Label {Text="Find",AutoSize=true,Margin=new Padding(0,7,0,0)},query,category,matchCase,scan,export,stop});
        list.Columns.Add("File offset",110);list.Columns.Add("Encoding",165);list.Columns.Add("Kind",115);list.Columns.Add("Embedded text",700);
        list.RetrieveVirtualItem+=delegate(object sender,RetrieveVirtualItemEventArgs e){var h=hits[e.ItemIndex];e.Item=new ListViewItem(new[]{"0x"+h.Offset.ToString("X8"),h.Encoding,h.Kind,h.Text});};
        list.SelectedIndexChanged+=delegate{if(list.SelectedIndices.Count>0)detail.Text=BinaryStrings.Row(hits[list.SelectedIndices[0]]);};
        Controls.Add(list);Controls.Add(detail);Controls.Add(status);Controls.Add(row);
        scan.Click+=async delegate{await Search(null);};export.Click+=async delegate{using(var picker=new SaveFileDialog {Title="Export current search to a NEW text file",Filter="Text report|*.txt",FileName="binary-search.txt",OverwritePrompt=false})if(picker.ShowDialog(this)==DialogResult.OK)await Search(picker.FileName);};
        stop.Click+=delegate{if(active!=null)active.Cancel();};query.KeyDown+=async delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter&&active==null){e.SuppressKeyPress=true;await Search(null);}};
    }
    internal void SetFile(string file){revision++;if(active!=null)active.Cancel();path=file;list.VirtualListSize=0;hits.Clear();detail.Clear();scan.Enabled=export.Enabled=file!=null&&active==null;status.Text="Search the whole binary, including data outside PE resources. Minimum string length: 4 characters. Empty query lists all strings.";}
    async Task Search(string output){
        if(active!=null||path==null)return;var source=new CancellationTokenSource();active=source;int version=revision;string input=path,needle=query.Text,filter=(string)category.SelectedItem;bool exact=matchCase.Checked;var found=new List<BinaryStrings.Hit>();
        scan.Enabled=export.Enabled=query.Enabled=category.Enabled=matchCase.Enabled=false;stop.Enabled=true;status.Text="Scanning...";if(output==null){list.VirtualListSize=0;hits.Clear();detail.Clear();}
        var progress=new Progress<long>(bytes=>{if(!IsDisposed&&revision==version&&active==source)status.Text="Scanned "+(bytes/1048576)+" MiB...";});
        try{
            long count=await Task.Run(()=>output==null?BinaryStrings.Scan(input,needle,exact,filter,h=>{if(found.Count<2000)found.Add(h);},source.Token,b=>((IProgress<long>)progress).Report(b)):BinaryStrings.Export(input,output,needle,exact,filter,source.Token,b=>((IProgress<long>)progress).Report(b)));
            if(IsDisposed||revision!=version)return;
            if(output==null){list.VirtualListSize=0;hits=found;list.VirtualListSize=hits.Count;detail.Clear();status.Text=count+" matching string fragments. Showing "+hits.Count+". Export all matches rescans the full file without a result-count cap. Long strings overlap.";}
            else status.Text="Exported "+count+" matching fragments to "+output;
        }catch(OperationCanceledException){if(!IsDisposed&&revision==version)status.Text="Cancelled. An exported file may be partial; only reports ending with COMPLETE finished.";}
        catch(Exception e){if(!IsDisposed&&revision==version)status.Text="Search failed: "+e.Message;}
        finally{active=null;source.Dispose();if(!IsDisposed){scan.Enabled=export.Enabled=path!=null;query.Enabled=category.Enabled=matchCase.Enabled=true;stop.Enabled=false;}}
    }
    protected override void Dispose(bool disposing){if(disposing){revision++;if(active!=null)active.Cancel();}base.Dispose(disposing);}
}
}
