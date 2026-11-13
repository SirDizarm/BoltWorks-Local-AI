using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
public sealed class ModelSlot {
    public string File {get;set;}
    public string Projector {get;set;}
    public bool Locked {get;set;}
    public ModelSlot() {File="";Projector="";}
}
sealed class ModelSlots {
    public static readonly string[] Roles={"chat","vision","pictures"};
    public readonly Dictionary<string,ModelSlot> Items=new Dictionary<string,ModelSlot>();
    readonly string file;
    public ModelSlots(string file) {
        this.file=file;foreach(string role in Roles) Items[role]=new ModelSlot();
        try {if(System.IO.File.Exists(file)) {var saved=Engine.Json.Deserialize<Dictionary<string,ModelSlot>>(System.IO.File.ReadAllText(file));foreach(string role in Roles) if(saved.ContainsKey(role)&&saved[role]!=null) Items[role]=saved[role];}} catch {}
    }
    public static string Title(string role) {return role=="vision"?"Read images":role=="pictures"?"Create pictures":"Chat";}
    public void Save() {LocalFiles.SaveJson(file,Items);}
    public string Missing(string role) {
        var slot=Items[role];
        if(!slot.Locked) return "Choose a model in '"+Title(role)+"' on the left, then press Lock.";
        if(!System.IO.File.Exists(slot.File)) return "The '"+Title(role)+"' model file is missing. Unlock that slot and choose its file on this computer.";
        if(role=="vision" && !System.IO.File.Exists(slot.Projector)) return "The Read images slot needs its matching picture-reading file (mmproj). Unlock the slot to choose it.";
        return null;
    }
    public ModelSlot Require(string role) {string missing=Missing(role);if(missing!=null) throw new IOException(missing);return Items[role];}
    public void Seed(string role,string path,string projector) {if(string.IsNullOrEmpty(Items[role].File)&&System.IO.File.Exists(path)) {Items[role].File=path;Items[role].Projector=projector??"";}}
}
sealed class ModelSlotsPane:UserControl {
    readonly ModelSlots data;
    readonly Label activity=new Label {Size=new Size(286,40),Text="No AI running.",ForeColor=Color.DarkGreen};
    public void SetRunning(string role) {activity.Text=role==null?"No AI running.":"Active: "+ModelSlots.Title(role)+"\nOther models are not running.";}
    public event Action Changed;
    public ModelSlotsPane(ModelSlots data,Control acceleration=null,Control computerCheck=null) {
        this.data=data;Dock=DockStyle.Fill;AutoScroll=true;Padding=new Padding(8);BackColor=Color.FromArgb(230,237,230);
        var stack=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};Controls.Add(stack);
        stack.Controls.Add(new Label {Text="Your AI models",Font=new Font("Segoe UI",16,FontStyle.Bold),Size=new Size(286,35)});
        stack.Controls.Add(new Label {Text="Choose files, then Lock.\nLock saves your choices.\nAI loads only when needed.",Size=new Size(286,65)});
        stack.Controls.Add(activity);
        if(computerCheck!=null)stack.Controls.Add(computerCheck);
        if(acceleration!=null)stack.Controls.Add(acceleration);
        foreach(string role in ModelSlots.Roles) {
            AddSlot(stack,role);
        }
    }
    void AddSlot(FlowLayoutPanel stack,string role) {
        var slot=data.Items[role];bool vision=role=="vision";
        var box=new GroupBox {Text=ModelSlots.Title(role),Size=new Size(286,vision?218:160),Padding=new Padding(9)};stack.Controls.Add(box);
        var name=new Label {Location=new Point(10,24),Size=new Size(265,37),AutoEllipsis=true};
        var choose=new Button {Text="Choose model",Location=new Point(10,64),Size=new Size(151,30)};
        var toggle=new Button {Location=new Point(171,64),Size=new Size(102,30)};
        var picture=new Button {Text="Choose picture-reading file",Location=new Point(10,101),Size=new Size(263,30),Visible=vision};
        var pictureName=new Label {Location=new Point(10,133),Size=new Size(263,32),AutoEllipsis=true,Visible=vision};
        var message=new Label {Location=new Point(10,vision?168:102),Size=new Size(263,48)};
        Action refresh=delegate {name.Text=string.IsNullOrEmpty(slot.File)?"No model chosen":Path.GetFileName(slot.File);pictureName.Text=string.IsNullOrEmpty(slot.Projector)?"No matching mmproj selected":Path.GetFileName(slot.Projector);choose.Enabled=picture.Enabled=!slot.Locked;toggle.Text=slot.Locked?"Unlock":"Lock";message.Text=slot.Locked?(data.Missing(role)??"Locked. Loads automatically for this job."):vision?"Use a vision GGUF and its matching mmproj file.":role=="pictures"?"Choose an SD 1.x or SDXL checkpoint.":"Choose a text GGUF for conversation.";};
        choose.Click+=delegate {using(var picker=new OpenFileDialog {Filter=role=="pictures"?"Image models (*.gguf;*.safetensors)|*.gguf;*.safetensors":"GGUF model (*.gguf)|*.gguf",Title="Choose "+ModelSlots.Title(role)+" model"}) if(picker.ShowDialog(this)==DialogResult.OK) {if(role!="pictures" && Path.GetFileName(picker.FileName).StartsWith("mmproj",StringComparison.OrdinalIgnoreCase)) {message.Text="Choose the main model first. mmproj is its separate picture-reading file.";return;} slot.File=picker.FileName;if(vision) slot.Projector=ImageAttachment.FindProjector(slot.File);data.Save();refresh();Notify();}};
        picture.Click+=delegate {using(var picker=new OpenFileDialog {Filter="Picture-reading file (mmproj*.gguf)|mmproj*.gguf|GGUF files|*.gguf",Title="Choose the mmproj made for this vision model"}) if(picker.ShowDialog(this)==DialogResult.OK) {slot.Projector=picker.FileName;data.Save();refresh();Notify();}};
        toggle.Click+=delegate {if(slot.Locked) slot.Locked=false;else {if(!File.Exists(slot.File)) {message.Text="Choose an existing model file first.";return;}if(vision && (!File.Exists(slot.Projector)||string.Equals(slot.File,slot.Projector,StringComparison.OrdinalIgnoreCase))) {message.Text="Choose the separate matching mmproj file before locking.";return;}slot.Locked=true;}data.Save();refresh();Notify();};
        box.Controls.AddRange(new Control[]{name,choose,toggle,picture,pictureName,message});refresh();
    }
    void Notify() {if(Changed!=null) Changed();}
}
}
