using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class WorkspaceTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(Environment.GetEnvironmentVariable("AWCONTROL_EMBEDDED")=="1")
        {
            string path=args.FirstOrDefault();
            using(var editor=new Form { Text=(path==null?"Documento":Path.GetFileName(path))+" - Word AW",Size=new Size(900,600),FormBorderStyle=FormBorderStyle.None })
            {
                editor.Controls.Add(new Label { Text=path??"Nova página",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",24) });
                editor.FormClosing+=(s,e)=>{if(path!=null && File.Exists(path+".keep")){editor.Text="Cancelado - Word AW";e.Cancel=true;}};
                Application.Run(editor);
            }
            return 0;
        }
        bool real=args.Contains("--real");
        string folder=Path.Combine(Path.GetTempPath(),"AWcontrol-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string word=Path.Combine(folder,"relatório de teste.txt"),excel=Path.Combine(folder,"tabela de teste.csv");
        File.WriteAllText(word,"Documento de teste do AWcontrol.");File.WriteAllText(excel,"Produto,Quantidade\nTeste,3\n");
        if(!real)File.WriteAllText(word+".keep","");
        string failure=null;int phase=0;var timeout=Stopwatch.StartNew();var since=Stopwatch.StartNew();EditorPage first=null,second=null;int dragStep=0;Task<bool> detachTask=null;Task<bool> fixedHeaders=null;
        var all=new System.Collections.Generic.List<Process>();
        try
        {
            using(var host=new SuiteHost(real?(Func<int,string>)Distribution.InstalledPath:app=>Application.ExecutablePath,true))
            using(var broker=new WorkspaceBroker(host))
            using(var timer=new Timer { Interval=100 })
            {
                host.NativeCommands=real;host.StartPosition=FormStartPosition.Manual;host.Location=Point.Empty;host.Size=new Size(1000,700);
                timer.Tick+=(s,e)=>
                {
                    try
                    {
                        if(timeout.Elapsed.TotalSeconds>(real?75:45))throw new Exception("Workspace test timeout at phase "+phase+"; "+host.CloseStatus);
                        if(phase==6 && since.ElapsedMilliseconds>2000){Console.WriteLine("Closing: "+host.CloseStatus);since.Restart();}
                        if(phase==0)
                        {
                            if(!real)CheckHome(host,folder);
                            Console.WriteLine("Opening editor processes");Capture(host,real?"real-home":"home");host.OpenDocument(0,null);host.OpenDocument(1,real?null:word);all.AddRange(host.Pages.Select(p=>p.Process));first=host.Pages[0];second=host.Pages[1];phase=1;Console.WriteLine("Waiting for native editor windows");return;
                        }
                        if(phase==1 && host.Pages.Count==2 && host.Pages.All(p=>p.Window!=IntPtr.Zero && Native.IsWindow(p.Window) && Native.GetParent(p.Window)==host.Stage.Handle && p.Attached.ElapsedMilliseconds>1000))
                        {
                            Require(host.Pages.All(p=>Native.GetParent(p.Window)==host.Stage.Handle),"editor must remain a real child of the workspace after its first rendered frame");
                            host.SelectPage(first);Eventually(()=>Native.IsWindowVisible(first.Window)&&!Native.IsWindowVisible(second.Window),"tab switching must show only the selected editor");
                            host.SelectPage(null);Eventually(()=>!Native.IsWindowVisible(first.Window)&&!Native.IsWindowVisible(second.Window),"Home must hide both editors");
                            host.SelectPage(second);host.Size=new Size(1040,680);host.Reorder(second,0);Require(host.Pages[0]==second,"drag order must retain page identity");phase=2;Console.WriteLine("Both editors attached and switched");since.Restart();return;
                        }
                        if(phase==2 && since.ElapsedMilliseconds>500)
                        {
                            Native.RECT bounds;Native.GetClientRect(second.Window,out bounds);Require(bounds.Right==host.Stage.ClientSize.Width && bounds.Bottom==host.Stage.ClientSize.Height,"embedded editor must follow workspace resizing");
                            Capture(host,real?"real-mixed-tabs":"mixed-tabs");
                            if(real){fixedHeaders=VerifyFixedHeaders(host,first,second);phase=31;since.Restart();return;}
                            host.ClosePage(second);phase=3;since.Restart();return;
                        }
                        if(phase==31&&fixedHeaders.IsCompleted){Require(fixedHeaders.Result,"hosted headers must stay fixed");Native.SetForegroundWindow(host.Handle);Native.FocusEditor(second.Window);KeyboardTest.ClickOnPage(second.Window);phase=19;since.Restart();return;}
                        if(phase==19 && since.ElapsedMilliseconds>500){KeyboardTest.DescribeFocus(second.Window);KeyboardTest.Type("AWCONTROLETESTE");phase=20;since.Restart();return;}
                        if(phase==20 && since.ElapsedMilliseconds>600){Capture(host,"real-word-after-typing");KeyboardTest.Shortcut(0x41);KeyboardTest.Shortcut(0x43);phase=21;since.Restart();return;}
                        if(phase==21 && since.ElapsedMilliseconds>500)
                        {
                            string copied="";try{copied=Clipboard.GetText();}catch(System.Runtime.InteropServices.ExternalException){}
                            if(!copied.Contains("AWCONTROLETESTE")&&since.ElapsedMilliseconds<5000)return;
                            Require(copied.Contains("AWCONTROLETESTE"),"a new hosted Word page must accept actual keyboard input and copy its typed text");
                            Capture(host,"real-word-typing");Console.WriteLine("Blank hosted Word typing and clipboard passed");KeyboardTest.Shortcut(0x24);phase=28;since.Restart();return;
                        }
                        if(phase==28 && since.ElapsedMilliseconds>500){KeyboardTest.BeginSelection(second.Window);phase=24;since.Restart();return;}
                        if(phase==24 && since.ElapsedMilliseconds>150){dragStep++;KeyboardTest.MoveSelection(second.Window,215+dragStep*57);if(dragStep==3)phase=27;since.Restart();return;}
                        if(phase==27 && since.ElapsedMilliseconds>250){KeyboardTest.EndSelection();phase=25;since.Restart();return;}
                        if(phase==25 && since.ElapsedMilliseconds>500){KeyboardTest.Shortcut(0x43);phase=26;since.Restart();return;}
                        if(phase==26 && since.ElapsedMilliseconds>500)
                        {
                            string copied="";try{copied=Clipboard.GetText();}catch(System.Runtime.InteropServices.ExternalException){}
                            if(!copied.Contains("CONTROLE")&&since.ElapsedMilliseconds<5000)return;
                            Capture(host,"real-word-mouse-selection");Require(copied.Contains("CONTROLE"),"mouse drag must select editable Word text after switching tabs");
                            Console.WriteLine("Hosted Word mouse selection and copy passed");detachTask=host.DetachPage(second,new Point(100,100));phase=30;since.Restart();return;
                        }
                        if(phase==30 && detachTask.IsCompleted)
                        {
                            Require(detachTask.Result,"detaching must preserve the running editor");Require(Native.GetParent(second.Window)==IntPtr.Zero,"detached editor must be an independent window");Require(host.Pages.Count==1,"detached document must leave the tab list");
                            CaptureNative(second.Window,"real-word-detached");Console.WriteLine("Dirty Word detached without restarting its process");Native.PostMessage(second.Window,0x0010,IntPtr.Zero,IntPtr.Zero);phase=22;since.Restart();return;
                        }
                        if(phase==22 && since.ElapsedMilliseconds>800){Capture(host,"real-word-save-question");KeyboardTest.Key(0x27);phase=23;since.Restart();return;}
                        if(phase==23 && since.ElapsedMilliseconds>350){KeyboardTest.Key(0x0D);phase=4;Console.WriteLine("Discarding typed Word test document");return;}
                        if(phase==3 && since.ElapsedMilliseconds>600)
                        {
                            if(!real){Require(!second.Process.HasExited,"Cancel must keep the editor and its document alive");File.Delete(word+".keep");host.ClosePage(second);}
                            phase=4;Console.WriteLine("Closing initial Word page");return;
                        }
                        if(phase==4 && host.Pages.Count==1 && (!real||second.Process.HasExited))
                        {
                            // Same path through the live broker as a second AWcontrol invocation.
                            var send=Task.Run(()=>WorkspaceBroker.Forward(new[]{excel,word}));
                            send.ContinueWith(t=>{if(!t.Result)failure="broker did not acknowledge forwarded files";});phase=5;Console.WriteLine("Forwarding two files");return;
                        }
                        if(phase==5 && host.Pages.Count==3 && host.Pages.All(p=>p.Window!=IntPtr.Zero && Native.IsWindow(p.Window) && Native.GetParent(p.Window)==host.Stage.Handle && p.Attached.ElapsedMilliseconds>1000))
                        {
                            all.AddRange(host.Pages.Select(p=>p.Process).Where(p=>!all.Contains(p)));
                            Require(host.Pages.Count(p=>p.App==0)==2 && host.Pages.Count(p=>p.App==1)==1,"forwarded files must create mixed Excel/Word pages");
                            Require(host.Pages.Any(p=>p.Path==word)&&host.Pages.Any(p=>p.Path==excel),"Unicode paths must survive forwarding");
                            if(real)
                            {
                                // A Windows file association invokes the standalone executable; it must hand the file to AWcontrol and exit.
                                var exe=Distribution.InstalledPath(1);
                                using(var forwarded=Process.Start(new ProcessStartInfo(exe,SuiteHost.Quote(word)){UseShellExecute=false}))
                                    Require(forwarded.WaitForExit(5000)&&forwarded.ExitCode==0,"standalone Word must hand an existing file to AWcontrol and exit");
                                exe=Distribution.InstalledPath(0);
                                using(var forwarded=Process.Start(new ProcessStartInfo(exe,SuiteHost.Quote(excel)){UseShellExecute=false}))
                                    Require(forwarded.WaitForExit(5000)&&forwarded.ExitCode==0,"standalone Excel must hand an existing file to AWcontrol and exit");
                                // The transport ACK means queued, not yet applied by this UI thread.
                                // Return to the message loop before testing suite closure, so the last
                                // file-open request cannot intentionally cancel a simultaneous close.
                                phase=7;since.Restart();return;
                            }
                            Capture(host,real?"real-files":"files");phase=6;Console.WriteLine("Closing suite");host.Close();return;
                        }
                        if(phase==7 && host.ActivePage!=null && host.ActivePage.Path==excel && since.ElapsedMilliseconds>200)
                        {
                            Require(host.Pages.Count==3,"reopening an existing file must activate its page without duplicates");
                            Capture(host,"real-files");phase=6;Console.WriteLine("All file opens applied; closing suite");host.Close();return;
                        }
                        if(phase==6 && host.IsDisposed)timer.Stop();
                    }
                    catch(Exception error){failure=error.ToString();timer.Stop();Capture(host,real?"real-failure":"failure");host.Dispose();}
                };
                timer.Start();Application.Run(host);
                Require(failure==null,failure??"");Require(phase==6,"test ended before all checks");
            }
            if(real){Standalone(0,excel);Standalone(1,word);}
            Console.WriteLine(real?"Real Excel/Word workspace integration passed.":"Workspace tabs, routing, resize, Home and Cancel lifecycle passed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally
        {
            foreach(var p in all)try{if(!p.HasExited)p.Kill();}catch(InvalidOperationException){}
            try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private static async Task<bool> VerifyFixedHeaders(SuiteHost host,EditorPage first,EditorPage second)
    {
        foreach(var page in new[]{first,second})
        {
            host.SelectPage(page);await Task.Delay(300);Native.RECT before,after;Native.GetWindowRect(page.Window,out before);KeyboardTest.HeaderPress(page.Window);await Task.Delay(150);KeyboardTest.HeaderMove(page.Window);await Task.Delay(250);KeyboardTest.EndSelection();await Task.Delay(300);Native.GetWindowRect(page.Window,out after);
            Require(before.Left==after.Left&&before.Top==after.Top&&before.Right==after.Right&&before.Bottom==after.Bottom,"dragging the editor header must not move its hosted page");
            KeyboardTest.HeaderDoubleClick(page.Window);await Task.Delay(350);Native.GetWindowRect(page.Window,out after);Require(before.Left==after.Left&&before.Top==after.Top&&before.Right==after.Right&&before.Bottom==after.Bottom,"double-clicking the editor header must not maximize its hosted page");
            Require((Native.GetWindowLongPtr(page.Window,-20).ToInt64()&0x00040000L)==0,"hosted editor must not advertise its own taskbar button");
        }
        host.SelectPage(second);Console.WriteLine("Hosted Excel/Word headers stay fixed and taskbar styles are hidden");return true;
    }
    private static void CheckHome(SuiteHost host,string folder)
    {
        host.MaximumSize=new Size(4096,4096);host.Size=new Size(1440,900);
        Require(host.Stage.Left==host.Stage.Top&&host.Stage.Left==host.Stage.Parent.ClientSize.Width-host.Stage.Right&&host.Stage.Top==host.Stage.Parent.ClientSize.Height-host.Stage.Bottom,"document frame must have equal spacing on all four sides");
        var header=host.Controls.OfType<WorkspaceStrip>().Single();var down=typeof(WorkspaceStrip).GetMethod("OnMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);down.Invoke(header,new object[]{new MouseEventArgs(MouseButtons.Left,2,80,16,0)});Require(host.WindowState==FormWindowState.Maximized,"double-clicking header must maximize");down.Invoke(header,new object[]{new MouseEventArgs(MouseButtons.Left,2,80,16,0)});Require(host.WindowState==FormWindowState.Normal,"double-clicking header again must restore");
        string[] names={"TOPS SANKHYA.xlsx","Relatório mensal.docx","Controle de estoque.xlsx","Proposta comercial.docx"};
        foreach(string name in names){string path=Path.Combine(folder,name);File.WriteAllText(path,"Home fixture");host.Home.AddRecent(path);}
        host.Home.UpdateProfile("Caio Rodrigues",null);host.Refresh();host.Home.Refresh();
        Require(host.Home.ActionBounds("app0new").Width>0&&host.Home.ActionBounds("app1new").Width>0,"both creation actions must be visible at desktop size");
        Require(!host.Home.ActionBounds("app0new").IntersectsWith(host.Home.ActionBounds("app1new")),"app actions must not overlap");
        CaptureHome(host,"home-reference-desktop");CaptureWideHome(host);
        host.Home.SearchText="TOPS";host.Home.Refresh();
        host.Home.InvokeAction("more0");var menu=host.Home.FileActions;menu.Items[1].PerformClick();menu.Close();Require(!menu.IsDisposed,"recent-file menu must survive its click/close processing");
        host.Home.InvokeAction("more0");menu.Items[1].PerformClick();menu.Close();Require(!menu.IsDisposed,"recent-file menu must reopen after an action");
        var strip=host.Controls.OfType<WorkspaceStrip>().Single();strip.NewFileMenu.Show(strip,Point.Empty);strip.NewFileMenu.Close();Require(!strip.NewFileMenu.IsDisposed,"new-file menu must survive close");strip.NewFileMenu.Show(strip,Point.Empty);strip.NewFileMenu.Close();
        Require(host.Home.FilteredFiles().Length==1,"search must filter real file names");host.Home.InvokeAction("favorite0");host.Home.Navigate("favorites");host.Home.SearchText="";host.Home.Refresh();Require(host.Home.FilteredFiles().Length==1,"favorites must contain only the selected file");CaptureHome(host,"home-favorites");
        host.ManageApps();host.Home.Refresh();Require(host.Home.Page=="apps","applications must be a page");Require(host.Home.Controls.OfType<Central>().Single().Visible,"application management must remain inside Home");Require(host.OwnedForms.Length==0,"applications must not open a separate window");CaptureHome(host,"applications-page");
        host.Home.Navigate("models");host.Home.Refresh();Require(host.Home.ActionBounds("modelbudget").Width>0,"budget model must be actionable");CaptureHome(host,"home-models");
        host.Home.Navigate("settings");host.Home.Refresh();bool before=host.Home.Animations;host.Home.InvokeAction("motion");Require(!host.Home.Animations,"reduced motion must disable transitions");host.Home.InvokeAction("motion");host.Home.Refresh();
        host.Home.Navigate("home");host.Size=new Size(720,500);host.Home.Refresh();var profile=host.Home.ActionBounds("profile");var settings=host.Home.ActionBounds("settings");Require(!profile.IntersectsWith(settings),"sidebar account and settings must not overlap at minimum height");CaptureHome(host,"home-reference-compact");
        host.Size=new Size(1000,700);host.Home.Refresh();
    }
    private static void CaptureWideHome(SuiteHost host)
    {
        string folder=Environment.GetEnvironmentVariable("AWCONTROL_TEST_SCREENSHOTS");if(string.IsNullOrEmpty(folder))return;
        var strip=host.Controls.OfType<WorkspaceStrip>().Single();var homeBounds=host.Home.Bounds;var stripBounds=strip.Bounds;
        try
        {
            host.Home.Dock=DockStyle.None;strip.Dock=DockStyle.None;strip.SetBounds(0,0,1440,36);host.Home.SetBounds(0,0,1424,848);host.Home.Refresh();
            Require(host.Home.ActionBounds("modelbudget").IsEmpty&&host.Home.ActionBounds("manage").IsEmpty,"Home must keep templates and application management off the main canvas");
            Require(host.Home.ActionBounds("recent").Width>0,"all recent files must remain accessible in the sidebar");
            using(var top=new Bitmap(1440,36))using(var body=new Bitmap(1424,848))using(var image=new Bitmap(1440,900))
            {strip.DrawToBitmap(top,new Rectangle(Point.Empty,top.Size));host.Home.DrawToBitmap(body,new Rectangle(Point.Empty,body.Size));using(var g=Graphics.FromImage(image)){g.Clear(Color.FromArgb(235,232,230));g.DrawImageUnscaled(top,0,0);g.DrawImageUnscaled(body,8,44);}image.Save(Path.Combine(folder,"home-reference-wide.png"));}
        }
        finally{host.Home.Bounds=homeBounds;strip.Bounds=stripBounds;host.Home.Dock=DockStyle.Fill;strip.Dock=DockStyle.Top;host.PerformLayout();}
    }
    private static void CaptureNative(IntPtr window,string name)
    {
        string folder=Environment.GetEnvironmentVariable("AWCONTROL_TEST_SCREENSHOTS");if(string.IsNullOrEmpty(folder))return;Native.RECT rect;Native.GetWindowRect(window,out rect);using(var image=new Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top)){using(var g=Graphics.FromImage(image))g.CopyFromScreen(new Point(rect.Left,rect.Top),Point.Empty,image.Size);image.Save(Path.Combine(folder,name+".png"));}
    }
    private static void CaptureHome(Form form,string name)
    {
        string folder=Environment.GetEnvironmentVariable("AWCONTROL_TEST_SCREENSHOTS");if(string.IsNullOrEmpty(folder))return;Directory.CreateDirectory(folder);using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,name+".png"));}
    }
    private static void Standalone(int app,string path)
    {
        using(var p=Process.Start(new ProcessStartInfo(Distribution.InstalledPath(app),SuiteHost.Quote(path)){UseShellExecute=false}))
        {
            var clock=Stopwatch.StartNew();IntPtr window=IntPtr.Zero;
            while(clock.ElapsedMilliseconds<30000 && !p.HasExited){window=Native.FindWindow(p.Id);if(window!=IntPtr.Zero && Native.IsWindowVisible(window))break;System.Threading.Thread.Sleep(100);}
            try{Require(window!=IntPtr.Zero && !p.HasExited,"standalone editor must still open without AWcontrol");Require(Native.GetParent(window)==IntPtr.Zero,"standalone editor must own its native window");Native.RECT outer;Native.GetWindowRect(window,out outer);var client=new Native.POINT();Native.ClientToScreen(window,ref client);Require(client.Y-outer.Top<12,"Word and Excel must omit the native title strip; client offset="+(client.Y-outer.Top));Native.PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);Require(p.WaitForExit(10000),"clean standalone document must close normally");}
            finally{if(!p.HasExited)p.Kill();}
        }
    }
    private static void Eventually(Func<bool> valid,string error){var time=Stopwatch.StartNew();while(!valid()&&time.ElapsedMilliseconds<1500)System.Threading.Thread.Sleep(10);Require(valid(),error);}
    private static void Require(bool valid,string error){if(!valid)throw new Exception(error);}
    private static void Capture(Form form,string name)
    {
        string folder=Environment.GetEnvironmentVariable("AWCONTROL_TEST_SCREENSHOTS");if(string.IsNullOrEmpty(folder))return;Directory.CreateDirectory(folder);
        using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)){using(var g=Graphics.FromImage(image))g.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,image.Size);image.Save(Path.Combine(folder,name+".png"));}
    }
}

// Keyboard injection is confined to disposable CI documents and verifies the user-visible editing path.
internal static class KeyboardTest
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct GuiInfo {internal uint Size,Flags;internal IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;internal Native.RECT CaretRect;}
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    internal static void DescribeFocus(IntPtr window){uint pid;uint thread=Native.GetWindowThreadProcessId(window,out pid);var info=new GuiInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(GuiInfo))};GetGUIThreadInfo(thread,ref info);Console.WriteLine("Keyboard target="+window+" focus="+info.Focus+" active="+info.Active+" foreground="+GetForegroundWindow());}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct Input {internal uint Type;internal Data Value;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] private struct Data {[System.Runtime.InteropServices.FieldOffset(0)] internal KeysInput Key;[System.Runtime.InteropServices.FieldOffset(0)] internal MouseInput Mouse;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct KeysInput {internal ushort Virtual,Scan;internal uint Flags,Time;internal IntPtr Extra;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct MouseInput {internal int X,Y;internal uint Data,Flags,Time;internal IntPtr Extra;}
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint SendInput(uint count,Input[] input,int size);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    internal static void ClickOnPage(IntPtr window)
    {
        Clipboard.Clear();var p=new Native.POINT{X=220,Y=310};Native.ClientToScreen(window,ref p);SetCursorPos(p.X,p.Y);
        Send(new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=2}}},new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=4}}});
    }
    private static void MoveTo(IntPtr window,int x,int y){var p=new Native.POINT{X=x,Y=y};Native.ClientToScreen(window,ref p);SetCursorPos(p.X,p.Y);}
    internal static void HeaderPress(IntPtr window){MoveTo(window,500,15);Send(new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=2}}});}
    internal static void HeaderMove(IntPtr window){MoveTo(window,580,45);}
    internal static void HeaderDoubleClick(IntPtr window){MoveTo(window,500,15);Send(new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=2}}},new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=4}}},new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=2}}},new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=4}}});}
    internal static void BeginSelection(IntPtr window){Clipboard.Clear();var p=new Native.POINT{X=215,Y=343};Native.ClientToScreen(window,ref p);SetCursorPos(p.X,p.Y);Send(new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=2}}});}
    internal static void MoveSelection(IntPtr window,int x){var p=new Native.POINT{X=x,Y=343};Native.ClientToScreen(window,ref p);SetCursorPos(p.X,p.Y);}
    internal static void EndSelection(){Send(new Input{Type=0,Value=new Data{Mouse=new MouseInput{Flags=4}}});}
    internal static void Type(string text){var list=new System.Collections.Generic.List<Input>();list.Add(Press(0x10,false));foreach(char c in text){list.Add(Press(c,false));list.Add(Press(c,true));}list.Add(Press(0x10,true));Send(list.ToArray());}
    internal static void Key(ushort code){Send(Press(code,false),Press(code,true));}
    internal static void Shortcut(ushort code){Send(Press(0x11,false),Press(code,false),Press(code,true),Press(0x11,true));}
    private static Input Press(ushort code,bool up){return new Input{Type=1,Value=new Data{Key=new KeysInput{Virtual=code,Flags=up?2u:0u}}};}
    private static void Send(params Input[] input){if(SendInput((uint)input.Length,input,System.Runtime.InteropServices.Marshal.SizeOf(typeof(Input)))!=input.Length)throw new Exception("Windows did not accept the test keyboard input");}
}
