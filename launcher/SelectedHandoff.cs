using System;
using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;

// Copy -> acknowledge -> remove source. Until acknowledgement the source remains authoritative.
// A lost commit reply retains the destination copy, never discards the last surviving document.
internal static class SelectedHandoff
{
    internal static async Task<bool> Move(SuiteHost host,EditorPage source)
    {
        string id=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");EditorPage target=null;bool importAttempted=false,commitStarted=false,adopted=false;
        try
        {
            var exported=await EditorControl.Send(source,"export",id);object count;
            if(exported.TryGetValue("count",out count)&&Equals(count,1)){await EditorControl.Send(source,"cancel",id);source.Detaching=false;DeleteCheckpoint(id);return await host.DockPage(source);}
            target=host.CreateEditor(source.App);if(target==null)throw new IOException("O editor de destino não iniciou.");host.StagingPages.Add(target);
            await Wait(target);importAttempted=true;var receipt=await EditorControl.Send(target,"import",id);object transfer,pid;
            if(!receipt.TryGetValue("transfer",out transfer)||!Equals(transfer,id)||!receipt.TryGetValue("sourcePid",out pid)||!Equals(pid,source.Process.Id))throw new IOException("A transferência não foi confirmada.");
            commitStarted=true;await EditorControl.Send(source,"commit",id);
            await EditorControl.Send(target,"accept",id);target.Title=source.Title;var current=await EditorControl.Send(target,"state");object path;if(current.TryGetValue("path",out path))target.Path=path as string;
            if(!source.DockOrigin.IsEmpty)Native.SetWindowPos(source.Window,IntPtr.Zero,source.DockOrigin.X,source.DockOrigin.Y,source.DockOrigin.Width,source.DockOrigin.Height,0x4014);
            host.AdoptImported(target);adopted=true;
        }
        catch(Exception error)
        {
            if(!(error is IOException||error is SocketException||error is AggregateException||error is InvalidOperationException||error is UnauthorizedAccessException||error is ArgumentException))throw;
        }
        // Cleanup remains outside catch/finally for the Framework compiler.
        {
            // Clearing the source lock is harmless after a successful commit and essential on cancellation.
            try{await EditorControl.Send(source,"cancel",id);}catch(Exception error){if(!Expected(error))throw;}
            if(target!=null&&!adopted)
            {
                if(importAttempted)
                {
                    // The source may have committed even if its reply was lost. Keep the accepted copy.
                    try{await EditorControl.Send(target,"accept",id);}catch(Exception error){if(!Expected(error))throw;}
                    host.AdoptImported(target);adopted=true;host.Notify(commitStarted?"Não foi possível confirmar a origem. A cópia transferida foi mantida para proteger seu trabalho.":"Não foi possível concluir a transferência. A origem foi mantida e o destino ficou aberto para conferência.");
                }
                else
                {
                    host.StagingPages.Remove(target);IntPtr window=Native.FindWindow(target.Process.Id);if(window!=IntPtr.Zero)Native.PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);
                    // No process termination: a failed or refused import never replaces the source.
                    target.Process.Dispose();
                }
            }
            DeleteCheckpoint(id);
        }
        if(!adopted)host.Notify("Não foi possível mover a aba. O documento original continua na janela de origem.");return adopted;
    }
    private static void DeleteCheckpoint(string id){try{string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"AlimentosWilson","transfers",id+".json");if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    private static async Task Wait(EditorPage page)
    {
        var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<30){if(page.Process.HasExited)throw new IOException("O editor de destino fechou.");try{await EditorControl.Send(page,"state");return;}catch(IOException){}catch(SocketException){}await Task.Delay(100);}
        throw new IOException("O editor de destino não respondeu.");
    }
    private static bool Expected(Exception error){return error is IOException||error is SocketException||error is AggregateException||error is InvalidOperationException||error is UnauthorizedAccessException||error is ArgumentException;}
}
