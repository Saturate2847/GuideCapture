using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Windows.Automation;

namespace GuideCapture {
class Step { public string Text; public string Notes=""; public Bitmap Image; public Step(string text, Bitmap image) { Text=text; Image=image; } }
static class CaptureArea {
 public static Rectangle Bounds(Rectangle monitor, Point mouse, int width, int height, bool crop) {
  if(!crop)return monitor;
  width=Math.Min(width,monitor.Width);height=Math.Min(height,monitor.Height);
  int x=Math.Max(monitor.Left,Math.Min(mouse.X-width/2,monitor.Right-width));
  int y=Math.Max(monitor.Top,Math.Min(mouse.Y-height/2,monitor.Bottom-height));
  return new Rectangle(x,y,width,height);
 }
 public static void Test() {
  var m=new Rectangle(0,0,1920,1080);
  if(Bounds(m,new Point(960,540),900,600,true)!=new Rectangle(510,240,900,600))throw new Exception("Centered crop failed");
  if(Bounds(m,new Point(1919,1079),900,600,true)!=new Rectangle(1020,480,900,600))throw new Exception("Edge crop failed");
  if(Bounds(m,new Point(0,0),900,600,true)!=new Rectangle(0,0,900,600))throw new Exception("Top edge crop failed");
  var left=new Rectangle(-1280,-200,1280,720);
  if(Bounds(left,new Point(-1279,-199),900,600,true)!=new Rectangle(-1280,-200,900,600))throw new Exception("Negative monitor crop failed");
  if(Bounds(left,new Point(-10,100),4000,3000,true)!=left)throw new Exception("Small monitor crop failed");
  if(Bounds(m,new Point(50,50),900,600,false)!=m)throw new Exception("Full monitor mode failed");
 }
}
static class Native {
 public delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
 [StructLayout(LayoutKind.Sequential)] public struct Mouse { public Point point; public uint data, flags, time; public UIntPtr extra; }
 [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
 [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr msg,IntPtr data);
 [DllImport("kernel32.dll",CharSet=CharSet.Auto)] public static extern IntPtr GetModuleHandle(string name);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int max);
 [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
class MainForm : Form {
 readonly List<Step> steps=new List<Step>(); readonly ListBox list=new ListBox();
 readonly TextBox title=new TextBox(), instruction=new TextBox(), afterInstructions=new TextBox(); readonly PictureBox preview=new PictureBox();
 readonly Label status=new Label(); readonly Button record=new Button(), pause=new Button();
 readonly CheckBox redact=new CheckBox(); readonly Native.HookProc hookProc;
 IntPtr hook; bool recording,paused,refreshing,dirty; int selected=-1; DateTime last=DateTime.MinValue; Point drag;
 System.Threading.Tasks.Task<string> labelTask;
 readonly NotifyIcon tray=new NotifyIcon();
 readonly ComboBox captureMode=new ComboBox(); readonly NumericUpDown cropWidth=new NumericUpDown(),cropHeight=new NumericUpDown();
 public MainForm() {
  Text="GuideCapture | Technical documentation"; Width=1240; Height=830; MinimumSize=new Size(920,650);
  BackColor=Color.FromArgb(245,247,250); Font=new Font("Segoe UI",10); StartPosition=FormStartPosition.CenterScreen;
  hookProc=MouseHook;
  tray.Icon=SystemIcons.Application;tray.Text="GuideCapture - ready";tray.Visible=true;var menu=new ContextMenuStrip();menu.Items.Add("Pause or resume",null,(s,e)=>TogglePause());menu.Items.Add("Stop and review",null,(s,e)=>Stop());tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>{WindowState=FormWindowState.Normal;Activate();};FormClosed+=(s,e)=>tray.Dispose();
  var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(22)};
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
  layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); Controls.Add(layout);
  layout.Controls.Add(new Label { Text="GuideCapture   /   Record once. Document clearly.",Font=new Font("Segoe UI",19,FontStyle.Bold),Dock=DockStyle.Fill},0,0);
  title.Text="Untitled technical guide"; title.Dock=DockStyle.Fill; title.TextChanged+=(s,e)=>dirty=true; layout.Controls.Add(title,0,1);
  var bar=new FlowLayoutPanel {Dock=DockStyle.Fill}; layout.Controls.Add(bar,0,2);
  Setup(record,"Start recording",(s,e)=>Start()); bar.Controls.Add(record);
  Setup(pause,"Pause",(s,e)=>TogglePause()); bar.Controls.Add(pause); pause.Enabled=false;
  AddButton(bar,"Stop & review",(s,e)=>Stop()); AddButton(bar,"New",(s,e)=>NewGuide());
  AddButton(bar,"Open project",(s,e)=>OpenProject()); AddButton(bar,"Save project",(s,e)=>SaveProject()); AddButton(bar,"Export Word",(s,e)=>Export());
  var captureSettings=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(captureSettings,0,3);
  captureSettings.Controls.Add(new Label {Text="Capture",AutoSize=true,Margin=new Padding(0,5,8,0)});
  captureMode.DropDownStyle=ComboBoxStyle.DropDownList;captureMode.Width=150;captureMode.Items.AddRange(new object[]{"Autocrop","Full monitor"});captureMode.SelectedIndex=0;captureSettings.Controls.Add(captureMode);
  captureSettings.Controls.Add(new Label {Text="Width",AutoSize=true,Margin=new Padding(12,5,4,0)});cropWidth.Minimum=240;cropWidth.Maximum=7680;cropWidth.Increment=100;cropWidth.Value=900;cropWidth.Width=80;captureSettings.Controls.Add(cropWidth);
  captureSettings.Controls.Add(new Label {Text="Height",AutoSize=true,Margin=new Padding(8,5,4,0)});cropHeight.Minimum=180;cropHeight.Maximum=4320;cropHeight.Increment=100;cropHeight.Value=600;cropHeight.Width=80;captureSettings.Controls.Add(cropHeight);
  captureSettings.Controls.Add(new Label {Text="pixels around each click",AutoSize=true,Margin=new Padding(6,5,0,0)});captureMode.SelectedIndexChanged+=(s,e)=>{cropWidth.Enabled=cropHeight.Enabled=captureMode.SelectedIndex==0;};
  var split=new SplitContainer {Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1}; layout.Controls.Add(split,0,4); Shown+=(s,e)=>{split.SplitterDistance=300;split.Panel1MinSize=260;};
  var left=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=2,ColumnCount=1}; left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,45));split.Panel1.Controls.Add(left);
  list.Dock=DockStyle.Fill; list.BorderStyle=BorderStyle.None; list.ItemHeight=44; list.DrawMode=DrawMode.OwnerDrawFixed;list.DrawItem+=(s,e)=>{e.DrawBackground();if(e.Index>=0)TextRenderer.DrawText(e.Graphics,list.Items[e.Index].ToString(),Font,new Rectangle(e.Bounds.X+8,e.Bounds.Y+8,e.Bounds.Width-16,e.Bounds.Height-8),e.ForeColor,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);e.DrawFocusRectangle();}; list.IntegralHeight=false; list.SelectedIndexChanged+=(s,e)=>SelectStep();left.Controls.Add(list,0,0);
  var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};left.Controls.Add(actions,0,1);
  AddButton(actions,"Up",(s,e)=>Move(-1));AddButton(actions,"Down",(s,e)=>Move(1));AddButton(actions,"Delete",(s,e)=>Delete());
  var right=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(16,0,0,0)};
  right.RowStyles.Add(new RowStyle(SizeType.Absolute,26));right.RowStyles.Add(new RowStyle(SizeType.Absolute,86));right.RowStyles.Add(new RowStyle(SizeType.Absolute,36));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,26));right.RowStyles.Add(new RowStyle(SizeType.Absolute,92));split.Panel2.Controls.Add(right);
  right.Controls.Add(new Label {Text="STEP INSTRUCTION",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(70,80,95)},0,0);
  instruction.Multiline=true;instruction.ScrollBars=ScrollBars.Vertical;instruction.Dock=DockStyle.Fill;instruction.TextChanged+=(s,e)=>{if(!refreshing&&selected>=0){steps[selected].Text=instruction.Text;dirty=true; UpdateListText();}};right.Controls.Add(instruction,0,1);
  redact.Text="Redact screenshot: drag a black rectangle (permanent)";redact.Dock=DockStyle.Fill;right.Controls.Add(redact,0,2);
  preview.Dock=DockStyle.Fill;preview.SizeMode=PictureBoxSizeMode.Zoom;preview.BackColor=Color.FromArgb(225,230,237);right.Controls.Add(preview,0,3);
  right.Controls.Add(new Label {Text="INSTRUCTIONS AFTER SCREENSHOT",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(70,80,95)},0,4);
  afterInstructions.Multiline=true;afterInstructions.AcceptsReturn=true;afterInstructions.ScrollBars=ScrollBars.Vertical;afterInstructions.Dock=DockStyle.Fill;afterInstructions.TextChanged+=(s,e)=>{if(!refreshing&&selected>=0){steps[selected].Notes=afterInstructions.Text;dirty=true;}};right.Controls.Add(afterInstructions,0,5);
  preview.MouseDown+=(s,e)=>drag=e.Location; preview.MouseUp+=(s,e)=>Redact(drag,e.Location);
  status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;layout.Controls.Add(status,0,5);SetStatus("Ready. Start recording, then work in any app. Screenshots stay on this PC.");
  FormClosing+=(s,e)=>{if(!ConfirmDiscard()){e.Cancel=true;return;}Stop();for(int i=1;i<=3;i++)Native.UnregisterHotKey(Handle,i);};
 }
 protected override void OnShown(EventArgs e) {base.OnShown(e);bool ok=true;ok &= Native.RegisterHotKey(Handle,1,6|0x4000,(uint)Keys.F8);ok &= Native.RegisterHotKey(Handle,2,6|0x4000,(uint)Keys.F9);ok &= Native.RegisterHotKey(Handle,3,6|0x4000,(uint)Keys.F10);if(!ok)SetStatus("Some shortcuts are unavailable. Use the recording controls in this window.");}
 protected override void WndProc(ref Message m) {if(m.Msg==0x312){int id=m.WParam.ToInt32();if(id==1)TogglePause();if(id==2&&recording&&!paused)Capture(Cursor.Position,false);if(id==3)Stop();}base.WndProc(ref m);}
 void Setup(Button b,string text,EventHandler action){b.Text=text;b.AutoSize=true;b.Height=34;b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.White;b.Margin=new Padding(0,0,8,4);b.Click+=action;}
 void AddButton(Control parent,string text,EventHandler action){var b=new Button();Setup(b,text,action);parent.Controls.Add(b);}
 void SetStatus(string text){status.Text=text;tray.Text=recording?(paused?"GuideCapture - PAUSED":"GuideCapture - RECORDING ("+steps.Count+" steps)"):"GuideCapture - ready";}
 void Start(){if(recording)return;hook=Native.SetWindowsHookEx(14,hookProc,Native.GetModuleHandle(null),0);if(hook==IntPtr.Zero){MessageBox.Show("Windows could not start click capture.");return;}recording=true;paused=false;record.Enabled=false;pause.Enabled=true;pause.Text="Pause";SetStatus("RECORDING  |  Ctrl+Shift+F8 pause  |  F9 capture  |  F10 stop (all with Ctrl+Shift)");WindowState=FormWindowState.Minimized;}
 void TogglePause(){if(!recording)return;paused=!paused;pause.Text=paused?"Resume":"Pause";SetStatus(paused?"PAUSED  |  Ctrl+Shift+F8 to resume":"RECORDING  |  Ctrl+Shift+F8 pause / F9 capture / F10 stop");}
 void Stop(){recording=false;paused=false;if(hook!=IntPtr.Zero){Native.UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}record.Enabled=true;pause.Enabled=false;pause.Text="Pause";if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;SetStatus(steps.Count+" steps ready to review. Edit instructions, redact, then export Word.");}
 IntPtr MouseHook(int code,IntPtr msg,IntPtr data){if(code>=0&&recording&&!paused&&(msg.ToInt32()==0x201||msg.ToInt32()==0x204)){var m=(Native.Mouse)Marshal.PtrToStructure(data,typeof(Native.Mouse));var point=m.point;bool right=msg.ToInt32()==0x204;if((DateTime.UtcNow-last).TotalMilliseconds>350){last=DateTime.UtcNow;BeginInvoke((Action)(()=>{if(recording&&!paused)Capture(point,right);}));}}return Native.CallNextHookEx(hook,code,msg,data);}
 new void Capture(Point point,bool right){
  try {if(steps.Count>=500){Stop();SetStatus("500-step prototype limit reached. Export or start another guide.");return;}uint pid;var hwnd=Native.GetAncestor(Native.WindowFromPoint(point),2);Native.GetWindowThreadProcessId(hwnd,out pid);if(pid==(uint)System.Diagnostics.Process.GetCurrentProcess().Id)return;
   var bounds=CaptureArea.Bounds(Screen.FromPoint(point).Bounds,point,(int)cropWidth.Value,(int)cropHeight.Value,captureMode.SelectedIndex==0);var bmp=new Bitmap(bounds.Width,bounds.Height);using(var g=Graphics.FromImage(bmp)){g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);using(var p=new Pen(Color.FromArgb(235,65,56),5))g.DrawEllipse(p,point.X-bounds.X-19,point.Y-bounds.Y-19,38,38);}
   var name=new StringBuilder(512);Native.GetWindowText(hwnd,name,512);string label="";
   // UI Automation can block on busy applications. Resolve labels off the UI thread with a short timeout.
   if(labelTask==null||labelTask.IsCompleted){labelTask=System.Threading.Tasks.Task.Factory.StartNew(()=>{try{var el=AutomationElement.FromPoint(new System.Windows.Point(point.X,point.Y));if(el!=null&&!el.Current.IsPassword)return el.Current.Name;}catch{}return "";});if(labelTask.Wait(100))label=labelTask.Result;}
   string text=(right?"Right-click ":"Click ")+(String.IsNullOrWhiteSpace(label)?"the highlighted location":("\""+label.Substring(0,Math.Min(160,label.Length))+"\""));if(name.Length>0)text+=" in "+name.ToString();text+=".";
   steps.Add(new Step(text,bmp));dirty=true;RefreshList(steps.Count-1);SetStatus("RECORDING  |  "+steps.Count+" steps  |  Ctrl+Shift+F8 pause / F9 capture / F10 stop");
  }catch(Exception ex){paused=true;pause.Text="Resume";SetStatus("Capture paused: "+ex.Message);}
 }
 void RefreshList(int index){refreshing=true;list.Items.Clear();for(int i=0;i<steps.Count;i++)list.Items.Add((i+1)+". "+steps[i].Text.Replace("\r"," ").Replace("\n"," "));refreshing=false;selected=-1;list.SelectedIndex=steps.Count==0?-1:Math.Max(0,Math.Min(index,steps.Count-1));if(steps.Count==0){instruction.Clear();afterInstructions.Clear();preview.Image=null;}}
 void SelectStep(){if(refreshing)return;selected=list.SelectedIndex;refreshing=true;instruction.Text=selected<0?"":steps[selected].Text;afterInstructions.Text=selected<0?"":steps[selected].Notes;preview.Image=selected<0?null:steps[selected].Image;refreshing=false;}
 void UpdateListText(){if(selected<0)return;refreshing=true;list.Items[selected]=(selected+1)+". "+steps[selected].Text.Replace("\r"," ").Replace("\n"," ");refreshing=false;}
 new void Move(int delta){int i=selected,j=i+delta;if(i<0||j<0||j>=steps.Count)return;var s=steps[i];steps.RemoveAt(i);steps.Insert(j,s);dirty=true;RefreshList(j);}
 void Delete(){if(selected<0)return;int i=selected;preview.Image=null;steps[i].Image.Dispose();steps.RemoveAt(i);dirty=true;RefreshList(i);}
 void Redact(Point a,Point b){if(!redact.Checked||selected<0)return;var img=steps[selected].Image;float scale=Math.Min((float)preview.Width/img.Width,(float)preview.Height/img.Height);float ox=(preview.Width-img.Width*scale)/2,oy=(preview.Height-img.Height*scale)/2;var rect=Rectangle.FromLTRB((int)((Math.Min(a.X,b.X)-ox)/scale),(int)((Math.Min(a.Y,b.Y)-oy)/scale),(int)((Math.Max(a.X,b.X)-ox)/scale),(int)((Math.Max(a.Y,b.Y)-oy)/scale));rect.Intersect(new Rectangle(0,0,img.Width,img.Height));if(rect.Width<2||rect.Height<2)return;using(var g=Graphics.FromImage(img))g.FillRectangle(Brushes.Black,rect);dirty=true;preview.Invalidate();}
 bool ConfirmDiscard(){return !dirty||MessageBox.Show("Unsaved changes will be lost. Continue? Save project preserves editable steps; Word export does not save the project.","Unsaved project",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes;}
 void Clear(){preview.Image=null;foreach(var s in steps)s.Image.Dispose();steps.Clear();RefreshList(0);}
 void NewGuide(){if(!ConfirmDiscard())return;Stop();Clear();title.Text="Untitled technical guide";dirty=false;}
 void SaveProject(){Stop();using(var d=new SaveFileDialog {Filter="GuideCapture project|*.guide",FileName="Technical guide.guide"})if(d.ShowDialog()==DialogResult.OK)try{Storage.SaveProject(d.FileName,title.Text,steps);dirty=false;SetStatus("Project saved: "+d.FileName);}catch(Exception ex){MessageBox.Show(ex.Message,"Save failed");}}
 void OpenProject(){if(!ConfirmDiscard())return;Stop();using(var d=new OpenFileDialog {Filter="GuideCapture project|*.guide"})if(d.ShowDialog()==DialogResult.OK)try{string t;var loaded=Storage.LoadProject(d.FileName,out t);Clear();steps.AddRange(loaded);title.Text=t;RefreshList(0);dirty=false;}catch(Exception ex){MessageBox.Show(ex.Message,"Open failed");}}
 void Export(){Stop();if(steps.Count==0){MessageBox.Show("Record at least one step first.");return;}using(var d=new SaveFileDialog {Filter="Word document|*.docx",FileName="Technical guide.docx"})if(d.ShowDialog()==DialogResult.OK)try{Storage.Export(d.FileName,title.Text,steps);SetStatus("Word document exported: "+d.FileName);}catch(Exception ex){MessageBox.Show(ex.Message,"Export failed");}}
 public void TestEditor(){list.SelectedIndex=0;instruction.Text="Edited instruction";afterInstructions.Text="After screenshot\r\nSecond instruction";list.SelectedIndex=1;list.SelectedIndex=0;if(afterInstructions.Text!="After screenshot\r\nSecond instruction")throw new Exception("Notes selection failed");if(steps[0].Text!="Edited instruction")throw new Exception("Edit failed");Move(1);if(steps[1].Text!="Edited instruction"||steps[1].Notes!="After screenshot\r\nSecond instruction")throw new Exception("Reorder failed");redact.Checked=true;Redact(new Point(preview.Width/2-12,preview.Height/2-12),new Point(preview.Width/2+12,preview.Height/2+12));var im=steps[1].Image;if(im.GetPixel(im.Width/2,im.Height/2).ToArgb()!=Color.Black.ToArgb())throw new Exception("Redaction failed");Delete();if(steps.Count!=2)throw new Exception("Deletion failed");dirty=false;}
 public void Demo(string folder){title.Text="Create a service desk ticket";for(int i=1;i<=3;i++){var b=new Bitmap(1440,900);using(var g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(242,245,250));g.FillRectangle(Brushes.White,200,120,1040,650);using(var f=new Font("Segoe UI",28,FontStyle.Bold))g.DrawString("Service desk  |  Demonstration",f,Brushes.DarkSlateBlue,245,160);using(var f=new Font("Segoe UI",21))g.DrawString(i==1?"Choose New ticket":i==2?"Enter the request summary":"Review and save the ticket",f,Brushes.Black,245,300);g.FillRectangle(Brushes.RoyalBlue,245,420,300,65);using(var f=new Font("Segoe UI",20))g.DrawString(i==3?"Save ticket":"New ticket",f,Brushes.White,265,430);using(var p=new Pen(Color.Tomato,5))g.DrawEllipse(p,260,425,50,50);}steps.Add(new Step(i==1?"Click New ticket to open a service request.":i==2?"Enter a concise summary of the request and select the appropriate queue.":"Review the request details, then click Save ticket.",b));}steps[0].Notes="Make sure X is Y.\r\nTurn Z into A.\r\nDelete Q and F.";RefreshList(0);Storage.Export(Path.Combine(folder,"sample.docx"),title.Text,steps);Storage.SaveProject(Path.Combine(folder,"sample.guide"),title.Text,steps);string t;var roundtrip=Storage.LoadProject(Path.Combine(folder,"sample.guide"),out t);if(roundtrip.Count!=3||t!=title.Text||roundtrip[1].Text!=steps[1].Text||roundtrip[0].Notes.Replace("\r","")!=steps[0].Notes.Replace("\r",""))throw new Exception("Roundtrip failed");foreach(var s in roundtrip)s.Image.Dispose();Show();Application.DoEvents();Refresh();preview.Refresh();Application.DoEvents();using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,Width,Height));b.Save(Path.Combine(folder,"app.png"));}dirty=false;}
}
static class Storage {
 static string X(string value){return SecurityElement.Escape(value??"");}
 static void Put(ZipArchive z,string name,string text){using(var w=new StreamWriter(z.CreateEntry(name).Open(),new UTF8Encoding(false)))w.Write(text);}
 static void Image(ZipArchive z,string name,Bitmap image){using(var s=z.CreateEntry(name).Open())image.Save(s,ImageFormat.Png);}
 static void Atomic(string path,Action<ZipArchive> write){string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var f=File.Create(tmp))using(var z=new ZipArchive(f,ZipArchiveMode.Create))write(z);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
 public static void SaveProject(string path,string title,List<Step> steps){Atomic(path,z=>{var root=new XElement("guide",new XAttribute("version",2),new XAttribute("title",title));for(int i=0;i<steps.Count;i++){root.Add(new XElement("step",new XElement("instruction",steps[i].Text),new XElement("notes",steps[i].Notes)));Image(z,"images/"+i+".png",steps[i].Image);}Put(z,"guide.xml",root.ToString());});}
 public static List<Step> LoadProject(string path,out string title){var result=new List<Step>();try{using(var f=File.OpenRead(path))using(var z=new ZipArchive(f,ZipArchiveMode.Read)){var entry=z.GetEntry("guide.xml");if(entry==null||entry.Length>10000000)throw new Exception("Invalid project.");XElement root;using(var s=entry.Open())root=XElement.Load(s);if(root.Name!="guide"||((string)root.Attribute("version")!="1"&&(string)root.Attribute("version")!="2"))throw new Exception("Unsupported project version.");title=(string)root.Attribute("title")??"Technical guide";var items=root.Elements("step").ToList();if(items.Count>500)throw new Exception("This prototype supports up to 500 steps per project.");for(int i=0;i<items.Count;i++){var e=z.GetEntry("images/"+i+".png");if(e==null||e.Length>50000000)throw new Exception("Invalid screenshot.");using(var s=e.Open())using(var b=new Bitmap(s)){if((long)b.Width*b.Height>50000000)throw new Exception("Screenshot too large.");bool legacy=(string)root.Attribute("version")=="1";result.Add(new Step(legacy?items[i].Value:((string)items[i].Element("instruction")??""),new Bitmap(b)){Notes=legacy?"":((string)items[i].Element("notes")??"")});}}}return result;}catch{foreach(var s in result)s.Image.Dispose();throw;}}
 static string P(string text,string style){return "<w:p><w:pPr><w:pStyle w:val=\""+style+"\"/></w:pPr><w:r><w:t xml:space=\"preserve\">"+X(text)+"</w:t></w:r></w:p>";}
 public static void Export(string path,string title,List<Step> steps){Atomic(path,z=>{
  Put(z,"[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
  Put(z,"_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rDoc\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
  Put(z,"word/styles.xml","<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"24\"/><w:color w:val=\"000000\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"80\"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:after=\"240\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"40\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"BeforeScreenshot\"><w:name w:val=\"Before Screenshot\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/></w:pPr></w:style></w:styles>");
  var rel=new StringBuilder("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"styles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
  var xml=new StringBuilder("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>");
  xml.Append(P(title,"Title"));xml.Append(P("Follow the numbered steps below to complete this procedure.","Normal"));
  for(int i=0;i<steps.Count;i++){xml.Append(P("Step "+(i+1),"Heading1"));foreach(var line in steps[i].Text.Replace("\r","").Split('\n'))xml.Append(P(line,"BeforeScreenshot"));double scale=Math.Min(4.8/steps[i].Image.Width,2.4/steps[i].Image.Height);long cx=(long)(steps[i].Image.Width*scale*914400),cy=(long)(steps[i].Image.Height*scale*914400);string id="image"+i;Image(z,"word/media/"+id+".png",steps[i].Image);rel.Append("<Relationship Id=\""+id+"\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/"+id+".png\"/>");xml.Append("<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\""+cx+"\" cy=\""+cy+"\"/><wp:docPr id=\""+(i+1)+"\" name=\"Step "+(i+1)+" screenshot\" descr=\""+X(steps[i].Text)+"\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"Screenshot\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\""+id+"\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\""+cx+"\" cy=\""+cy+"\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");if(!String.IsNullOrWhiteSpace(steps[i].Notes))foreach(var line in steps[i].Notes.Replace("\r","").Split('\n'))xml.Append(P(line,"Normal"));}
  xml.Append("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1080\" w:right=\"1080\" w:bottom=\"1080\" w:left=\"1080\"/></w:sectPr></w:body></w:document>");rel.Append("</Relationships>");Put(z,"word/document.xml",xml.ToString());Put(z,"word/_rels/document.xml.rels",rel.ToString());
 });}
}
static class Program {
 [STAThread] static void Main(string[] args){Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var app=new MainForm()){if(args.Length==2&&args[0]=="--self-test"){Directory.CreateDirectory(args[1]);CaptureArea.Test();app.Demo(args[1]);app.TestEditor();File.WriteAllText(Path.Combine(args[1],"tests.txt"),"PASS: center and edge crops, negative monitor coordinates, oversized crops, full monitor mode, project roundtrip, edit, reorder, pixel redaction, deletion, Word export");return;}Application.Run(app);}}
}
}



