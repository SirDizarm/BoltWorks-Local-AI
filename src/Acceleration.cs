using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace BoltWorks.LlamaLite {
enum ComputeBackend { Cpu, Cuda }
static class Acceleration {
    public static string Runtime(string root,ComputeBackend backend) {
        string folder=backend==ComputeBackend.Cuda?"runtime-cuda":"runtime";
        string exe=Path.Combine(root,folder,"llama-server.exe");
        if(!File.Exists(exe)) throw new FileNotFoundException(backend==ComputeBackend.Cuda?
            "The optional NVIDIA CUDA add-on is not installed. Extract the CUDA add-on ZIP beside BoltWorks Local AI so runtime-cuda sits next to runtime. Or choose CPU (default). No Ollama or CUDA Toolkit is needed.":
            "The CPU runtime folder is missing. Extract the entire BoltWorks Local AI ZIP.");
        return exe;
    }
    public static string Arguments(ComputeBackend backend,int layers) {
        if(backend==ComputeBackend.Cpu)return " -ngl 0";
        if(layers<1||layers>128)throw new ArgumentOutOfRangeException("layers","GPU layers must be between 1 and 128.");
        return " --device CUDA0 -ngl "+layers;
    }
}
sealed class AccelerationPane:FlowLayoutPanel {
    readonly ComboBox choice=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=286,AccessibleName="AI processing mode"};
    readonly ComboBox memory=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=286,AccessibleName="Model memory mode"};
    readonly Label memoryInfo=new Label {Size=new Size(286,100)};
    readonly NumericUpDown layers=new NumericUpDown {Minimum=1,Maximum=128,Value=16,Width=65,AccessibleName="CUDA GPU layers"};
    readonly FlowLayoutPanel details=new FlowLayoutPanel {Width=286,Height=35,WrapContents=false};
    public event Action Changed;
    public ComputeBackend Backend {get {return Memory==MemoryMode.Disk?ComputeBackend.Cpu:choice.SelectedIndex==1?ComputeBackend.Cuda:ComputeBackend.Cpu;}}
    public MemoryMode Memory {get {return (MemoryMode)Math.Max(0,memory.SelectedIndex);}}
    public int GpuLayers {get {return (int)layers.Value;}}
    public AccelerationPane() {
        Width=300;AutoSize=true;FlowDirection=FlowDirection.TopDown;WrapContents=false;Margin=new Padding(0,4,0,10);
        Controls.Add(new Label {Text="AI processing",AutoSize=true,Font=new Font("Segoe UI",10,FontStyle.Bold)});
        choice.Items.AddRange(new object[]{"CPU (default — works on older PCs)","NVIDIA CUDA (optional add-on)"});choice.SelectedIndex=0;Controls.Add(choice);
        details.Controls.Add(new Label {Text="GPU layers",AutoSize=true,Margin=new Padding(0,5,6,0)});details.Controls.Add(layers);details.Visible=false;Controls.Add(details);
        Controls.Add(new Label {Text="Starts in CPU mode every time.\nCUDA also applies to coding tasks.\nPicture generation has its own setting.",Size=new Size(286,62)});
        Controls.Add(new Label {Text="Model memory",AutoSize=true});memory.Items.AddRange(new object[]{"Normal","Low RAM (up to 4K context)","Disk-backed CPU (experimental)"});memory.SelectedIndex=0;
        string saved=Path.Combine(LocalFiles.Root,"memory-mode.txt");try {if(File.Exists(saved))memory.SelectedIndex=(int)ModelMemory.Parse(File.ReadAllText(saved).Trim());}catch{}
        Controls.Add(memory);Controls.Add(memoryInfo);
        var split=new Button {Text="Create GGUF split files…",AutoSize=true};Controls.Add(split);split.Click+=delegate {Notify();using(var window=new SplitModelWindow())window.ShowDialog(this);};
        var inspect=new Button {Text="Inspect GGUF / split files",AutoSize=true};Controls.Add(inspect);inspect.Click+=delegate {using(var picker=new OpenFileDialog {Filter="GGUF model or shard|*.gguf"})if(picker.ShowDialog(this)==DialogResult.OK)try {MessageBox.Show(this,GgufSet.Inspect(picker.FileName).Describe(),"Model files");}catch(Exception e){MessageBox.Show(this,e.Message,"Model files");}};
        Action updateMemory=delegate {choice.Enabled=Memory!=MemoryMode.Disk;details.Visible=Backend==ComputeBackend.Cuda;memoryInfo.Text=Memory==MemoryMode.Normal?"Normal context and processing batches. Split GGUF sets load as one model.":Memory==MemoryMode.Low?"Smaller processing batches. Context is capped at 4096 tokens, including coding. No model quality guarantee.":"CPU only. Maps files from disk and skips warmup. RAM is not hard-limited. Can be very slow or still run out of memory.";};
        memory.SelectedIndexChanged+=delegate {updateMemory();try {Directory.CreateDirectory(LocalFiles.Root);File.WriteAllText(saved,Memory.ToString().ToLowerInvariant());}catch{}Notify();};updateMemory();
        choice.SelectedIndexChanged+=delegate {details.Visible=Backend==ComputeBackend.Cuda;Notify();};layers.ValueChanged+=delegate {Notify();};
    }
    void Notify(){if(Changed!=null)Changed();}
}
}
