using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class FirefoxDb : IDisposable {
    IntPtr db;
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int sqlite3_open16(string file,out IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int sqlite3_prepare16_v2(IntPtr db,string sql,int length,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] static extern int sqlite3_bind_text16(IntPtr statement,int index,string value,int length,IntPtr destructor);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text16(IntPtr statement,int index);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr statement);
    internal FirefoxDb(string file){if(sqlite3_open16(file,out db)!=0){Dispose();throw new InvalidOperationException("Không mở được dữ liệu Firefox.");}}
    internal List<string[]> Query(string sql,params string[] values){IntPtr statement;if(sqlite3_prepare16_v2(db,sql,-1,out statement,IntPtr.Zero)!=0)throw new InvalidOperationException("Định dạng dữ liệu Firefox chưa được hỗ trợ. Chưa thay đổi bookmark.");
        try {for(int i=0;i<values.Length;i++)if(sqlite3_bind_text16(statement,i+1,values[i],-1,new IntPtr(-1))!=0)throw new Exception("Firefox parameter binding failed");var rows=new List<string[]>();int code;while((code=sqlite3_step(statement))==100){var row=new string[sqlite3_column_count(statement)];for(int i=0;i<row.Length;i++)row[i]=Marshal.PtrToStringUni(sqlite3_column_text16(statement,i));rows.Add(row);}if(code!=101)throw new InvalidOperationException("Không cập nhật được bookmark Firefox. Dữ liệu gốc được giữ nguyên.");return rows;}finally{sqlite3_finalize(statement);}}
    internal string One(string sql,params string[] values){var rows=Query(sql,values);return rows.Count==0?null:rows[0][0];}
    public void Dispose(){if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
}
internal static class FirefoxBookmarks {
    static string Digest(string file){using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))return Convert.ToBase64String(sha.ComputeHash(stream));}
    static uint Hash(string value,int max){uint h=0;byte[] data=Encoding.UTF8.GetBytes(value);unchecked{for(int i=0;i<Math.Min(max,data.Length);i++)h=0x9e3779b9U*((h<<5|h>>27)^data[i]);}return h;}
    internal static long UrlHash(string value){int colon=value.IndexOf(':');return (long)((colon>=0&&colon<50?((ulong)(Hash(value.Substring(0,colon),1500)&65535)<<32):0)+Hash(value,1500));}
    static string Guid12(){return Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace('+','-').Replace('/','_').Substring(0,12);}
    internal static string Install(string profile,string payload,bool checkRunning){
        if(checkRunning&&BookmarkStore.ChromeRunning("firefox"))throw new InvalidOperationException("Vui lòng đóng Firefox trước khi thêm bookmark.");
        string file=Path.Combine(profile,"places.sqlite"),wal=file+"-wal";
        if(!File.Exists(file))throw new InvalidOperationException("Không tìm thấy hồ sơ Firefox.");
        if(File.Exists(wal)&&new FileInfo(wal).Length>0)throw new InvalidOperationException("Firefox chưa đóng hoàn toàn. Hãy thoát Firefox rồi thử lại.");
        if(payload.Length>65536)throw new InvalidOperationException("Bookmark Firefox quá dài.");
        string before=Digest(file),pending=file+".loc-ho-so-"+Guid.NewGuid().ToString("N")+".tmp",backup=file+".loc-ho-so-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".bak";
        try{File.Copy(file,pending);using(var db=new FirefoxDb(pending)){
            if(db.One("PRAGMA integrity_check")!="ok")throw new InvalidOperationException("Dữ liệu Firefox cần được kiểm tra. Chưa thay đổi bookmark.");
            db.Query("PRAGMA journal_mode=DELETE");db.Query("BEGIN IMMEDIATE");
            string parent=db.One("SELECT id FROM moz_bookmarks WHERE guid='toolbar_____' AND type=2");if(parent==null)throw new InvalidOperationException("Không tìm thấy thanh bookmark Firefox.");
            string existing=db.One("SELECT b.id FROM moz_bookmarks b JOIN moz_places p ON p.id=b.fk WHERE b.parent=? AND b.type=1 AND p.url LIKE 'javascript:%' AND p.url LIKE '%locHoSoUpdateChannel%' LIMIT 1",parent);
            string oldPlace=existing==null?null:db.One("SELECT fk FROM moz_bookmarks WHERE id=?",existing);
            string place=db.One("SELECT id FROM moz_places WHERE url=?",payload);
            if(place==null){db.Query("INSERT INTO moz_places(url,title,rev_host,hidden,frecency,guid,foreign_count,url_hash) VALUES(?,?,NULL,1,-1,?,0,?)",payload,BookmarkStore.Title,Guid12(),UrlHash(payload).ToString());place=db.One("SELECT last_insert_rowid()");}
            string time=((DateTime.UtcNow.Ticks-new DateTime(1970,1,1).Ticks)/10).ToString();
            if(existing==null){string position=db.One("SELECT COALESCE(MAX(position)+1,0) FROM moz_bookmarks WHERE parent=?",parent);db.Query("INSERT INTO moz_bookmarks(type,fk,parent,position,title,dateAdded,lastModified,guid,syncStatus,syncChangeCounter) VALUES(1,?,?,?,?,?,?,?,1,1)",place,parent,position,BookmarkStore.Title,time,time,Guid12());}
            else db.Query("UPDATE moz_bookmarks SET fk=?,title=?,lastModified=?,syncChangeCounter=syncChangeCounter+1 WHERE id=?",place,BookmarkStore.Title,time,existing);
            if(oldPlace!=place){db.Query("UPDATE moz_places SET foreign_count=foreign_count+1 WHERE id=?",place);if(oldPlace!=null)db.Query("UPDATE moz_places SET foreign_count=MAX(0,foreign_count-1) WHERE id=?",oldPlace);}
            db.Query("UPDATE moz_bookmarks SET lastModified=?,syncChangeCounter=syncChangeCounter+1 WHERE id=?",time,parent);db.Query("COMMIT");
            if(db.One("PRAGMA integrity_check")!="ok")throw new Exception("Firefox integrity check failed");
        }
        if((checkRunning&&BookmarkStore.ChromeRunning("firefox"))||Digest(file)!=before||(File.Exists(wal)&&new FileInfo(wal).Length>0))throw new InvalidOperationException("Firefox vừa được mở hoặc dữ liệu đã thay đổi. Hãy đóng Firefox rồi thử lại.");
        File.Replace(pending,file,backup);return backup;
        }finally{if(File.Exists(pending))File.Delete(pending);}
    }
}
