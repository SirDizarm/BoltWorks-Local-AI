using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
sealed class CodePane:UserControl {
    readonly TextBox project=new TextBox {Dock=DockStyle.Fill,ReadOnly=true,AccessibleName="Coding project folder"};
    readonly TextBox goal=new TextBox {Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,AccessibleName="Coding task"};
    readonly TextBox log=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,AccessibleName="Coding tool results"};
    readonly Label status=new Label {Dock=DockStyle.Fill,Text="Uses your locked Chat model. Choose a project and describe the change."};
    readonly Button choose=new Button {Text="Choose project",AutoSize=true},run=new Button {Text="Run coding task",AutoSize=true},stop=new Button {Text="Stop",AutoSize=true,Enabled=false},jobFiles=new Button {Text="Job files",AutoSize=true,Enabled=false};
    readonly NumericUpDown steps=new NumericUpDown {Minimum=1,Maximum=60,Value=25,Width=65};
    readonly ComboBox projectType=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=190,AccessibleName="Coding project type"};
    CancellationTokenSource active;TaskCompletionSource<bool> finished;bool closing;string lastJournal;
    public CodePane(Func<List<Dictionary<string,object>>,CancellationToken,Task<string>> model,Func<bool> enter,Action exit,Action releaseModel) {
        Dock=DockStyle.Fill;Padding=new Padding(16);
        var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7};
        foreach(int height in new[]{44,48,55,110,48})grid.RowStyles.Add(new RowStyle(SizeType.Absolute,height));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,65));Controls.Add(grid);
        var heading=new FlowLayoutPanel {Dock=DockStyle.Fill};var network=new Button {Text="Ethernet inspection",AutoSize=true,Margin=new Padding(30,3,0,0)};heading.Controls.Add(new Label {Text="Work on code",Font=new Font("Segoe UI",18,FontStyle.Bold),AutoSize=true});heading.Controls.Add(network);grid.Controls.Add(heading,0,0);
        network.Click+=delegate {
            if(active!=null||!enter())return;
            try {using(var dialog=new NetworkInspectorWindow())if(dialog.ShowDialog(this)==DialogResult.OK&&dialog.ProjectFolder!=null){project.Text=dialog.ProjectFolder;projectType.SelectedIndex=3;goal.Text=File.ReadAllText(Path.Combine(project.Text,"TASK.txt"));log.Text=File.ReadAllText(Path.Combine(project.Text,"network-report.txt"));status.Text="Network report saved. Your local model can analyse it.";}}
            finally{exit();}
        };
        var pe=new Button {Text="EXE / DLL inspector",AutoSize=true,Margin=new Padding(12,3,0,0)};heading.Controls.Add(pe);
        pe.Click+=delegate {if(active!=null||!enter())return;try{using(var dialog=new PeInspectorWindow())dialog.ShowDialog(this);}finally{exit();}};
        var import=new Button {Text="Inspect binary / firmware",AutoSize=true};
        var folder=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3};folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));folder.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));folder.Controls.Add(project,0,0);folder.Controls.Add(choose,1,0);folder.Controls.Add(import,2,0);grid.Controls.Add(folder,0,1);
        grid.Controls.Add(new Label {Text="The AI can browse, read and edit UTF-8 files in this folder. Edits are saved immediately with backups. Build/test commands ask for approval and can access the rest of your computer.",Dock=DockStyle.Fill},0,2);
        projectType.Items.AddRange(new object[]{"Any language","C# / .NET","Minecraft mod (Java)","Legacy code / assembly"});projectType.SelectedIndex=0;
        grid.Controls.Add(goal,0,3);var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};actions.Controls.AddRange(new Control[]{projectType,run,stop,new Label {Text="Max steps",AutoSize=true,Margin=new Padding(12,7,0,0)},steps,jobFiles});grid.Controls.Add(actions,0,4);grid.Controls.Add(log,0,5);grid.Controls.Add(status,0,6);
        log.Text="Choose and lock a text/code GGUF in the Chat slot.\r\nChoose the source folder, then request a specific change.\r\nTool results show what actually happened.\r\nEach run starts fresh and saves its log and original files in a local job folder.\r\nCoding quality depends on the model; a code-trained model is preferable.";
        string settings=Path.Combine(LocalFiles.Root,"code-project.json");
        try{if(File.Exists(settings)){var saved=JsonValue.Object(Engine.Json.DeserializeObject(File.ReadAllText(settings)));string path=JsonValue.Text(saved,"project");if(Directory.Exists(path))project.Text=path;}}catch{}
        choose.Click+=delegate {using(var picker=new FolderBrowserDialog {Description="Choose the source project the AI may edit",SelectedPath=project.Text})if(picker.ShowDialog(this)==DialogResult.OK){project.Text=picker.SelectedPath;try{LocalFiles.SaveJson(settings,new {project=project.Text});}catch(Exception e){status.Text=e.Message;}}};
        import.Click+=async delegate {
            if(active!=null)return;string input,parent;
            using(var picker=new OpenFileDialog {Title="Select a binary, firmware image or exported copy (never executed)",Filter="All files|*.*"}){if(picker.ShowDialog(this)!=DialogResult.OK)return;input=picker.FileName;}
            using(var picker=new FolderBrowserDialog {Description="Choose where to create a NEW inspection project"}){if(picker.ShowDialog(this)!=DialogResult.OK)return;parent=picker.SelectedPath;}
            if(!enter())return;
            active=new CancellationTokenSource();finished=new TaskCompletionSource<bool>();choose.Enabled=import.Enabled=run.Enabled=false;stop.Enabled=true;status.Text="Copying and inspecting file headers and strings locally...";
            try {
                string imported=await Task.Run(()=>LegacyImport.Import(input,parent,active.Token));
                if(!closing){project.Text=imported;projectType.SelectedIndex=3;goal.Text=File.ReadAllText(Path.Combine(imported,"TASK.txt"));log.Text=File.ReadAllText(Path.Combine(imported,"inspection.txt"))+"\r\n"+File.ReadAllText(Path.Combine(imported,"firmware.txt"));status.Text="Inspection complete. Lock a Chat model, then run the prepared analysis task.";LocalFiles.SaveJson(settings,new {project=imported});}
            }catch(OperationCanceledException){if(!closing)status.Text="Import cancelled. Incomplete output is marked and retained.";}
            catch(Exception e){if(!closing)status.Text="Import failed: "+e.Message;}
            finally{active.Dispose();active=null;exit();if(!closing){choose.Enabled=import.Enabled=run.Enabled=true;stop.Enabled=false;}finished.TrySetResult(true);}
        };
        stop.Click+=delegate {if(active!=null){active.Cancel();status.Text="Stopping; edits already saved remain available.";}};
        jobFiles.Click+=delegate {try{if(Directory.Exists(lastJournal))Process.Start("explorer.exe",LocalFiles.Quote(lastJournal));}catch(Exception e){status.Text=e.Message;}};
        run.Click+=async delegate {
            if(active!=null)return;
            if(!Directory.Exists(project.Text)||string.IsNullOrWhiteSpace(goal.Text)){status.Text="Choose a project folder and enter a coding task first.";return;}
            if(!enter())return;
            active=new CancellationTokenSource();finished=new TaskCompletionSource<bool>();choose.Enabled=import.Enabled=run.Enabled=goal.Enabled=steps.Enabled=projectType.Enabled=false;stop.Enabled=true;log.Clear();
            string journal=Path.Combine(LocalFiles.Root,"code-jobs",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
            try {
                var files=new CodeWorkspace(project.Text,journal);lastJournal=journal;jobFiles.Enabled=true;LocalFiles.SaveJson(Path.Combine(journal,"task.json"),new {project=files.Root,goal=goal.Text,preset=projectType.Text});
                Action<string> progress=delegate(string text){if(!closing){log.AppendText(text+"\r\n\r\n");status.Text=text.Split('\n')[0];}};
                var agent=new CodeAgent(model,Approve,progress,releaseModel);await agent.Run(files,goal.Text+"\nProject guidance: "+CodeGuidance.For(projectType.SelectedIndex),(int)steps.Value,active.Token);
                if(!closing)status.Text="Run ended. Review results above. Logs and backups: "+journal;
            }catch(OperationCanceledException){if(!closing)status.Text="Stopped. Existing edits retained. Logs and backups: "+journal;}
            catch(Exception e){if(!closing){log.AppendText("ERROR: "+e.Message);status.Text="Run failed: "+e.Message+". Logs: "+journal;}}
            finally {active.Dispose();active=null;exit();if(!closing){choose.Enabled=import.Enabled=run.Enabled=goal.Enabled=steps.Enabled=projectType.Enabled=true;stop.Enabled=false;}finished.TrySetResult(true);}
        };
    }
    Task<bool> Approve(string command,CancellationToken cancel) {
        cancel.ThrowIfCancellationRequested();
        using(var dialog=new Form {Text="Approve coding command",Size=new Size(760,430),StartPosition=FormStartPosition.CenterParent,Font=Font}) {
            var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};grid.RowStyles.Add(new RowStyle(SizeType.Absolute,70));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,45));dialog.Controls.Add(grid);
            grid.Controls.Add(new Label {Dock=DockStyle.Fill,Text="Run in: "+project.Text+"\nThis PowerShell command runs with your user permissions. Review it before allowing it."},0,0);
            grid.Controls.Add(new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Text=command},0,1);
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill};var deny=new Button {Text="Deny",DialogResult=DialogResult.Cancel,AutoSize=true};var allow=new Button {Text="Run command",DialogResult=DialogResult.OK,AutoSize=true};buttons.Controls.AddRange(new Control[]{deny,allow});grid.Controls.Add(buttons,0,2);dialog.CancelButton=deny;dialog.AcceptButton=deny;
            return Task.FromResult(dialog.ShowDialog(this)==DialogResult.OK);
        }
    }
    public async Task Shutdown(){closing=true;if(active!=null)active.Cancel();if(finished!=null)await finished.Task;}
}
static class CodeGuidance {
    public static string For(int preset) {
        const string offline="This project must work on an offline laptop. Use only installed local tools, dependencies and documentation. Do not download, install packages, browse the web or call cloud APIs. If a compiler, SDK, wrapper distribution or dependency cache is missing, explain exactly what must be prepared on a connected machine; do not claim a successful build. ";
        if(preset==1)return offline+"Inspect .sln/.csproj, global.json and repository instructions before changing C#. Preserve target framework, language version and existing project style. Prefer dotnet build --no-restore and dotnet test --no-restore for SDK projects with restored assets, or the existing local MSBuild/build script for .NET Framework. Do not change the target framework to solve missing SDK errors.";
        if(preset==2)return offline+"Inspect build.gradle or build.gradle.kts, gradle.properties, settings.gradle and mod metadata to identify exact Minecraft version, loader (Forge, NeoForge or Fabric), mappings and required Java version. Preserve those versions and use matching existing APIs and local examples. Never mix loader APIs or guess dependency versions. Prefer .\\gradlew.bat --offline --no-daemon build when its distribution and dependencies are already cached. The wrapper itself may need a preinstalled/cached distribution. Do not change dependency versions to bypass missing caches. For a new mod without version/loader information, finish by asking the user to specify them or provide a prepared starter project.";
        if(preset==3)return offline+"Work with legacy source code or assembly/disassembly exports in the project. Identify target CPU, operating system, compiler and calling convention from actual files; ask when unknown. Preserve the target's supported APIs and instruction set. Cite file paths and addresses for findings, separate recovered facts from hypotheses, and inspect one function at a time. Native executables cannot be read as ordinary text. If native analysis is needed, use an already installed local REA CLI/disassembler only through approved commands, or ask for a local disassembly export. Do not assume REA, Ghidra or a legacy compiler is bundled or installed. Do not run npx or install anything. Do not execute or modify the original binary as part of inspection. Save proposed source changes separately and test with the matching toolchain when available. Do not claim exact source recovery or compatibility without evidence.";
        return offline+"Inspect the existing build files and project instructions. Use its existing language, compiler and test conventions. For new software, create real source files and use available local build tools; report missing tools accurately.";
    }
}
}
