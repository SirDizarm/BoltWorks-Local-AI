using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
sealed class ResourceBrowser:UserControl {
    readonly ListView list=new ListView {Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false,VirtualMode=true,HideSelection=false};
    readonly PictureBox picture=new PictureBox {Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(45,45,45)};
    readonly TextBox text=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false};
    readonly TextBox hex=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false};
    readonly Label status=new Label {Dock=DockStyle.Top,Height=48,Text="Select an embedded resource. Images and readable text are previewed; nothing is executed."};
    readonly Button saveImage=new Button {Text="Save image as PNG",AutoSize=true,Enabled=false},saveText=new Button {Text="Save text / script",AutoSize=true,Enabled=false};
    byte[] selectedData;int selectedResource;
    readonly Action<int> selected;
    List<PeInspector.ResourceInfo> resources=new List<PeInspector.ResourceInfo>();
    CancellationTokenSource work=new CancellationTokenSource();string input;int generation;
    public ResourceBrowser(Action<int> onSelected){
        selected=onSelected;Dock=DockStyle.Fill;
        list.Columns.Add("#",55);list.Columns.Add("Embedded resource",360);list.Columns.Add("Bytes",100);
        list.RetrieveVirtualItem+=delegate(object sender,RetrieveVirtualItemEventArgs e){var r=resources[e.ItemIndex];e.Item=new ListViewItem(new[]{r.Index.ToString(),r.Path,r.Size.ToString()});};
        var split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Horizontal};
        var tabs=new TabControl {Dock=DockStyle.Fill};
        var imagePage=new TabPage("Image");imagePage.Controls.Add(picture);tabs.TabPages.Add(imagePage);
        var textPage=new TabPage("Text / scripts");textPage.Controls.Add(text);tabs.TabPages.Add(textPage);
        var hexPage=new TabPage("Hex bytes");hexPage.Controls.Add(hex);tabs.TabPages.Add(hexPage);
        split.Panel1.Controls.Add(list);split.Panel2.Controls.Add(tabs);Controls.Add(split);var saves=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=38};saves.Controls.AddRange(new Control[]{saveImage,saveText});Controls.Add(saves);Controls.Add(status);
        saveImage.Click+=delegate{SaveSelected(true);};saveText.Click+=delegate{SaveSelected(false);};
        list.SelectedIndexChanged+=async delegate {if(list.SelectedIndices.Count==0)return;int index=list.SelectedIndices[0];if(index>=resources.Count)return;selected(resources[index].Index);await Preview(resources[index]);};
    }
    void ResetExport(){selectedData=null;saveImage.Enabled=saveText.Enabled=false;}
    void ClearImage(){var old=picture.Image;picture.Image=null;if(old!=null)old.Dispose();}
    public async Task LoadFile(string path,CancellationToken external){
        work.Cancel();work.Dispose();work=CancellationTokenSource.CreateLinkedTokenSource(external);var token=work.Token;int current=++generation;
        input=path;list.VirtualListSize=0;resources.Clear();ResetExport();ClearImage();text.Clear();hex.Clear();status.Text="Reading embedded Windows resources...";
        try{
            var found=await Task.Run(()=>PeInspector.ListResources(path,token));
            if(IsDisposed||current!=generation)return;resources=found;list.VirtualListSize=resources.Count;
            status.Text=resources.Count+" embedded resources. Select one to preview or extract. Assets in external archives/files are not listed here.";
        }catch(OperationCanceledException){}catch(Exception e){if(!IsDisposed&&current==generation)status.Text="Resource list: "+e.Message;}
    }
    async Task Preview(PeInspector.ResourceInfo resource){
        int current=++generation;var token=work.Token;string path=input;ResetExport();ClearImage();text.Clear();hex.Clear();status.Text="Reading "+resource.Path;
        try{
            byte[] data=await Task.Run(()=>PeInspector.PreviewResource(path,resource,token));
            if(IsDisposed||current!=generation)return;
            selectedData=data;selectedResource=resource.Index;saveText.Enabled=DecodeText(data)!=null;
            hex.Text=Hex(data);text.Text=Readable(data,resource.Type);
            string imageStatus="No supported image detected. Check Text / scripts or Hex bytes.";
            try{picture.Image=DecodeImage(data,resource.Type);saveImage.Enabled=true;imageStatus="Image "+picture.Image.Width+" x "+picture.Image.Height+". Use Extract resource to save the original bytes.";}catch(Exception e){if(!(e is ArgumentException)&&!(e is IOException)&&!(e is System.Runtime.InteropServices.ExternalException))throw;}
            status.Text=resource.Path+" — "+resource.Size+" bytes. "+imageStatus;
        }catch(OperationCanceledException){}catch(Exception e){if(!IsDisposed&&current==generation)status.Text="Preview: "+e.Message;}
    }
    void SaveSelected(bool image){
        if(image?picture.Image==null:selectedData==null)return;
        using(var picker=new SaveFileDialog {Title=image?"Save displayed image as a NEW PNG":"Save complete text resource to a NEW file",FileName="resource-"+selectedResource,DefaultExt=image?"png":"txt",AddExtension=true,OverwritePrompt=false,Filter=image?"PNG image|*.png":"Text|*.txt|JavaScript|*.js|Java source|*.java|JSON|*.json|XML|*.xml|HTML|*.html|CSS|*.css|Lua|*.lua|C# source|*.cs|All files|*.*"}){
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            try{if(image)SavePng(picture.Image,picker.FileName);else SaveScript(selectedData,picker.FileName);status.Text="Saved: "+picker.FileName+(image?" (displayed image as PNG)":" (complete resource, original encoding and bytes)");}
            catch(Exception e){status.Text="Save failed: "+e.Message;}
        }
    }
    internal static void SavePng(Image image,string destination){
        using(var encoded=new MemoryStream()){image.Save(encoded,System.Drawing.Imaging.ImageFormat.Png);using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){encoded.Position=0;encoded.CopyTo(output);}}
    }
    internal static void SaveScript(byte[] data,string destination){
        if(DecodeText(data)==null)throw new IOException("This resource is not recognized as readable text. Use Extract resource for raw binary data.");
        using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(data,0,data.Length);}
    }
    static string DecodeText(byte[] data){
        if(data.Length==0)return null;
        string value;try{
            if(data.Length>=2&&data[0]==254&&data[1]==255)value=new UnicodeEncoding(true,true,true).GetString(data);
            else if(data.Length>=2&&((data[0]==255&&data[1]==254)||(data[1]==0&&data[0]!=0)))value=new UnicodeEncoding(false,true,true).GetString(data);
            else value=new UTF8Encoding(false,true).GetString(data);
        }catch(DecoderFallbackException){return null;}
        int controls=0;foreach(char c in value)if(char.IsControl(c)&&c!='\r'&&c!='\n'&&c!='\t')controls++;
        return controls>Math.Max(2,value.Length/50)?null:value;
    }
    static uint U32(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
    internal static Bitmap DecodeImage(byte[] data,uint type){
        byte[] wrapped=data;
        bool png=data.Length>=8&&data[0]==137&&data[1]==80&&data[2]==78&&data[3]==71;
        if(type==3&&!png){
            if(data.Length<40||U32(data,0)<40)throw new IOException("Unsupported icon bitmap.");
            int width=BitConverter.ToInt32(data,4),height=BitConverter.ToInt32(data,8)/2;
            if(width<1||height<1||width>256||height>256)throw new IOException("Unsupported icon dimensions.");
            using(var m=new MemoryStream())using(var w=new BinaryWriter(m)){
                w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)1);w.Write((byte)(width==256?0:width));w.Write((byte)(height==256?0:height));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write(BitConverter.ToUInt16(data,14));w.Write(data.Length);w.Write(22);w.Write(data);wrapped=m.ToArray();
            }
            using(var m=new MemoryStream(wrapped))using(var icon=new Icon(m)){return icon.ToBitmap();}
        }
        if(type==2){
            if(data.Length<40)throw new IOException("Short bitmap resource.");uint header=U32(data,0);if(header<40||header>data.Length)throw new IOException("Unsupported bitmap header.");
            int bits=BitConverter.ToUInt16(data,14);uint colors=U32(data,32);if(colors==0&&bits<=8)colors=(uint)(1<<bits);
            long pixels=14L+header+colors*4L;if(header==40&&(U32(data,16)==3||U32(data,16)==6))pixels+=U32(data,16)==6?16:12;
            if(pixels>14L+data.Length)throw new IOException("Invalid bitmap palette.");
            using(var m=new MemoryStream())using(var w=new BinaryWriter(m)){w.Write((ushort)0x4d42);w.Write(data.Length+14);w.Write(0);w.Write((uint)pixels);w.Write(data);wrapped=m.ToArray();}
        }
        bool recognized=wrapped.Length>=4&&(png||(wrapped[0]==255&&wrapped[1]==216)||(wrapped[0]==71&&wrapped[1]==73&&wrapped[2]==70)||(wrapped[0]==66&&wrapped[1]==77)||(wrapped[0]==0&&wrapped[1]==0&&wrapped[2]==1&&wrapped[3]==0)||(wrapped[0]==73&&wrapped[1]==73)||(wrapped[0]==77&&wrapped[1]==77));
        if(!recognized)throw new IOException("Not a supported image.");
        using(var m=new MemoryStream(wrapped))using(var image=Image.FromStream(m,false,true)){
            if((long)image.Width*image.Height>16000000)throw new IOException("Image preview exceeds 16 million pixels.");return new Bitmap(image);
        }
    }
    static string Readable(byte[] data,uint type){
        string value=DecodeText(data);if(value==null)return "Binary or unrecognized text encoding. Use Hex bytes or extract the original resource.";
        int length=Math.Min(value.Length,131072);if(length<value.Length&&length>0&&char.IsHighSurrogate(value[length-1]))length--;
        return value.Substring(0,length).Replace("\0","␀")+(length<value.Length?"\r\n[Text preview shortened; Save text / script saves the complete resource.]":"");
    }
    static string Hex(byte[] data){var b=new StringBuilder();int length=Math.Min(data.Length,16384);for(int i=0;i<length;i+=16){b.Append(i.ToString("X8")).Append("  ");for(int j=i;j<Math.Min(i+16,length);j++)b.Append(data[j].ToString("X2")).Append(' ');b.AppendLine();}if(length<data.Length)b.AppendLine("[Hex preview shortened; extract for all bytes.]");return b.ToString();}
    protected override void Dispose(bool disposing){if(disposing){++generation;work.Cancel();work.Dispose();ClearImage();}base.Dispose(disposing);}
}
}
