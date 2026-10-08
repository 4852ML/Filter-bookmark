using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

internal static class BookmarkIcon {
    static string Digest(string file){using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))return Convert.ToBase64String(sha.ComputeHash(stream));}
    internal static bool Apply(string profile,string payload,string browser,bool checkRunning){
        string process=browser=="Firefox"?"firefox":browser=="Edge"?"msedge":"chrome";
        string file=Path.Combine(profile,browser=="Firefox"?"favicons.sqlite":"Favicons"),pending=file+".loc-ho-so-"+Guid.NewGuid().ToString("N")+".tmp";
        try {
            if(!File.Exists(file)||(checkRunning&&BookmarkStore.ChromeRunning(process)))return false;
            foreach(string suffix in new[]{"-wal","-journal"})if(File.Exists(file+suffix)&&new FileInfo(file+suffix).Length>0)return false;
            string before=Digest(file);File.Copy(file,pending);byte[] image;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("filter.png"))using(var memory=new MemoryStream()){stream.CopyTo(memory);image=memory.ToArray();}
            string hex=BitConverter.ToString(image).Replace("-",""),iconUrl="https://raw.githubusercontent.com/4852ML/loc-ho-so-bookmark/main/icons/filter.png";
            using(var db=new FirefoxDb(pending)){
                if(db.One("PRAGMA integrity_check")!="ok")return false;
                db.Query("PRAGMA journal_mode=DELETE");db.Query("BEGIN IMMEDIATE");
                if(browser=="Firefox"){
                    string icon=db.One("SELECT id FROM moz_icons WHERE icon_url=?",iconUrl);
                    if(icon==null){db.Query("INSERT INTO moz_icons(icon_url,fixed_icon_url_hash,width,root,color,expire_ms,flags,data) VALUES(?,?,32,0,NULL,?,0,X'"+hex+"')",iconUrl,FirefoxBookmarks.UrlHash(iconUrl).ToString(),((DateTime.UtcNow.AddYears(1).Ticks-new DateTime(1970,1,1).Ticks)/10000).ToString());icon=db.One("SELECT last_insert_rowid()");}
                    string page=db.One("SELECT id FROM moz_pages_w_icons WHERE page_url=?",payload);
                    if(page==null){db.Query("INSERT INTO moz_pages_w_icons(page_url,page_url_hash) VALUES(?,?)",payload,FirefoxBookmarks.UrlHash(payload).ToString());page=db.One("SELECT last_insert_rowid()");}
                    db.Query("INSERT OR REPLACE INTO moz_icons_to_pages(page_id,icon_id,expire_ms) VALUES(?,?,?)",page,icon,((DateTime.UtcNow.AddYears(1).Ticks-new DateTime(1970,1,1).Ticks)/10000).ToString());
                }else{
                    string icon=db.One("SELECT id FROM favicons WHERE url=? AND icon_type=1",iconUrl);
                    if(icon==null){db.Query("INSERT INTO favicons(url,icon_type) VALUES(?,1)",iconUrl);icon=db.One("SELECT last_insert_rowid()");}
                    db.Query("DELETE FROM favicon_bitmaps WHERE icon_id=?",icon);
                    db.Query("INSERT INTO favicon_bitmaps(icon_id,last_updated,image_data,width,height,last_requested) VALUES(?,?,X'"+hex+"',32,32,0)",icon,((DateTime.UtcNow.Ticks-new DateTime(1601,1,1).Ticks)/10).ToString());
                    // Only replace this application's mapping; other cached icons remain intact.
                    db.Query("DELETE FROM icon_mapping WHERE page_url=? AND icon_id IN(SELECT id FROM favicons WHERE icon_type=1)",payload);
                    db.Query("INSERT INTO icon_mapping(page_url,icon_id) VALUES(?,?)",payload,icon);
                }
                db.Query("COMMIT");if(db.One("PRAGMA integrity_check")!="ok")return false;
            }
            if((checkRunning&&BookmarkStore.ChromeRunning(process))||Digest(file)!=before)return false;
            foreach(string suffix in new[]{"-wal","-journal"})if(File.Exists(file+suffix)&&new FileInfo(file+suffix).Length>0)return false;
            File.Replace(pending,file,file+".loc-ho-so-"+Guid.NewGuid().ToString("N")+".bak");return true;
        }catch{return false;}finally{if(File.Exists(pending))File.Delete(pending);}
    }
}
