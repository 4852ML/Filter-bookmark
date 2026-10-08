using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
internal sealed class InstallerFonts : IDisposable {
    [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr data,uint size,IntPtr reserved,ref uint count);
    [DllImport("gdi32.dll")] static extern bool RemoveFontMemResourceEx(IntPtr handle);
    readonly PrivateFontCollection collection=new PrivateFontCollection();readonly List<IntPtr> buffers=new List<IntPtr>(),handles=new List<IntPtr>();
    internal FontFamily Family {get{return collection.Families[0];}}
    internal InstallerFonts(){foreach(string name in new[]{"NotoSans-Regular.ttf","NotoSans-Bold.ttf"})using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var memory=new MemoryStream()){source.CopyTo(memory);byte[] data=memory.ToArray();IntPtr buffer=Marshal.AllocHGlobal(data.Length);buffers.Add(buffer);Marshal.Copy(data,0,buffer,data.Length);uint count=0;handles.Add(AddFontMemResourceEx(buffer,(uint)data.Length,IntPtr.Zero,ref count));collection.AddMemoryFont(buffer,data.Length);}}
    public void Dispose(){collection.Dispose();foreach(var handle in handles)if(handle!=IntPtr.Zero)RemoveFontMemResourceEx(handle);foreach(var buffer in buffers)Marshal.FreeHGlobal(buffer);}
}
