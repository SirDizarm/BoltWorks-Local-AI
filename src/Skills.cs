using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
public sealed class TaskSkill {
    public string Name {get;set;}
    public string Instruction {get;set;}
    public override string ToString() {return Name;}
}
public sealed class SkillData {
    public int Version {get;set;}
    public bool Enabled {get;set;}
    public string Selected {get;set;}
    public List<TaskSkill> Skills {get;set;}
}
sealed class SkillsPane : UserControl {
    readonly ComboBox list=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,AccessibleName="Saved task skills"};
    readonly TextBox name=new TextBox {Dock=DockStyle.Fill,AccessibleName="Skill name"};
    readonly TextBox instruction=new TextBox {Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,AccessibleName="Skill instructions"};
    readonly CheckBox enabled=new CheckBox {Text="Use selected saved skill for chat",AutoSize=true};
    readonly Label status=new Label {Dock=DockStyle.Fill};
    SkillData data; bool loading,writable=true;
    string FilePath {get {return Path.Combine(LocalFiles.Root,"skills.json");}}
    public SkillsPane() {
        Dock=DockStyle.Fill; Padding=new Padding(18);
        var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7};
        foreach(int h in new int[]{40,40,40,32}) grid.RowStyles.Add(new RowStyle(SizeType.Absolute,h)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,48)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,100));
        grid.Controls.Add(new Label {Text="Reusable task skills",Font=new Font("Segoe UI",17,FontStyle.Bold),Dock=DockStyle.Fill},0,0); grid.Controls.Add(list,0,1); grid.Controls.Add(name,0,2); grid.Controls.Add(new Label {Text="Instructions for the selected task",Dock=DockStyle.Fill},0,3); grid.Controls.Add(instruction,0,4);
        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill}; var save=new Button {Text="Save skill",AutoSize=true}; var fresh=new Button {Text="New skill",AutoSize=true}; var import=new Button {Text="Import .md / .txt",AutoSize=true}; actions.Controls.AddRange(new Control[]{save,fresh,import,enabled}); grid.Controls.Add(actions,0,5); grid.Controls.Add(status,0,6); Controls.Add(grid);
        data=new SkillData {Version=1,Enabled=false,Skills=new List<TaskSkill> {
            new TaskSkill {Name="Organize a recipe",Instruction="Turn the supplied text or visible recipe into a clear title, ingredients with exact amounts, and numbered cooking steps. Preserve quantities and units. Do not invent missing ingredients, amounts, temperatures or times. Flag unclear or missing information. Use the language requested by the user."},
            new TaskSkill {Name="Translate to Swedish",Instruction="Translate the supplied content to clear Swedish. Preserve meaning, names, quantities, measurements, lists and cooking steps. Do not add information. Mark text you cannot read confidently."},
            new TaskSkill {Name="Read an attached image",Instruction="Read the text in the attached image carefully, preserving its order and headings. Mark unreadable sections and uncertain numbers. Do not pretend to see an image if none is attached; ask for it."},
            new TaskSkill {Name="Write a picture prompt",Instruction="Write one concise English image-generation prompt based on the user's description. Include the saved theme's style, palette, recurring character details and setting when relevant. Output only the prompt. Do not claim an image has been created."}
        }};
        try {if(File.Exists(FilePath)) {var loaded=Engine.Json.Deserialize<SkillData>(File.ReadAllText(FilePath)); if(loaded==null || loaded.Version!=1 || loaded.Skills==null) throw new IOException("Unsupported skills file."); data=loaded;}}
        catch(Exception e) {writable=false; status.Text="Could not load custom skills. Existing file preserved: "+e.Message;}
        loading=true; foreach(var skill in data.Skills) list.Items.Add(skill); foreach(TaskSkill skill in list.Items) if(skill.Name==data.Selected) list.SelectedItem=skill; enabled.Checked=data.Enabled; loading=false; Display(); save.Enabled=writable;
        if(writable) status.Text="Save edits before using them, then enable a saved skill and return to chat. Skills are reusable prompts; they do not run scripts or operate other software. Use text for picture transfers a description to the image generator. Model size and ability affect task reliability.";
        list.SelectedIndexChanged+=delegate {Display(); if(!loading) {data.Selected=list.SelectedItem==null?null:((TaskSkill)list.SelectedItem).Name; Persist();}};
        enabled.CheckedChanged+=delegate {if(!loading) {data.Enabled=enabled.Checked; Persist();}};
        fresh.Click+=delegate {list.SelectedIndex=-1; name.Clear(); instruction.Clear(); name.Focus();};
        import.Click+=delegate {using(var dialog=new OpenFileDialog {Filter="Skill instructions (*.md;*.txt)|*.md;*.txt"}) if(dialog.ShowDialog(this)==DialogResult.OK) try {if(new FileInfo(dialog.FileName).Length>64*1024) throw new IOException("Choose an instruction file smaller than 64 KB."); string text=File.ReadAllText(dialog.FileName,new UTF8Encoding(false,true)); if(text.Length>12000 || text.IndexOf('\0')>=0) throw new IOException("Use plain instruction text up to 12,000 characters."); list.SelectedIndex=-1; name.Text=Path.GetFileNameWithoutExtension(dialog.FileName); instruction.Text=text; status.Text="Imported into the editor. Review it, then Save skill to use it.";} catch(Exception e) {status.Text=e.Message;} };
        save.Click+=delegate {
            string title=name.Text.Trim(),text=instruction.Text.Trim(); if(title.Length==0 || title.Length>80 || text.Length==0 || text.Length>12000) {status.Text="Use a name (1–80 characters) and instructions (1–12,000 characters)."; return;}
            var previous=data;
            data=new SkillData {Version=1,Enabled=enabled.Checked,Selected=title,Skills=new List<TaskSkill>(previous.Skills)};
            var skill=new TaskSkill {Name=title,Instruction=text}; int index=data.Skills.FindIndex(s=>string.Equals(s.Name,title,StringComparison.OrdinalIgnoreCase)); if(index<0) data.Skills.Add(skill); else data.Skills[index]=skill;
            if(Persist()) {loading=true; list.Items.Clear(); foreach(var item in data.Skills) list.Items.Add(item); list.SelectedItem=skill; loading=false; status.Text="Saved locally: "+title;} else data=previous;
        };
    }
    void Display() {var skill=list.SelectedItem as TaskSkill; if(skill!=null) {name.Text=skill.Name; instruction.Text=skill.Instruction;}}
    bool Persist() {if(!writable) return false; try {LocalFiles.SaveJson(FilePath,data); return true;} catch(Exception e) {status.Text="Could not save skills: "+e.Message; return false;}}
    public string Apply(string system) {var skill=data.Enabled?data.Skills.Find(s=>s.Name==data.Selected):null; return SkillRules.Apply(skill,system);}
}
static class SkillRules {
    public static string Apply(TaskSkill skill,string instruction) {return skill==null ? instruction : instruction+"\nSelected task skill: "+skill.Name+"\n"+skill.Instruction;}
}
}
