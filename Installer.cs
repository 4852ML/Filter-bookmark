using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class BookmarkStore {
    internal const string Title = "Lọc hồ sơ · Báo cáo";
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16777216 };
    internal static string Payload(string resource="bookmark.txt") { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd().Trim(); }
    internal static bool ChromeRunning(string processName = "chrome") { return Process.GetProcessesByName(processName).Length != 0; }
    static long MaxId(object value) {
        long max = 0, n;
        var d = value as Dictionary<string, object>;
        if (d != null) { if (d.ContainsKey("id") && Int64.TryParse(Convert.ToString(d["id"]), out n)) max = n; foreach (var v in d.Values) max = Math.Max(max, MaxId(v)); }
        var list = value as IList; if (list != null) foreach (var v in list) max = Math.Max(max, MaxId(v));
        return max;
    }
    internal static string Install(string profile, string payload, bool checkChrome, string processName = "chrome") {
        if (checkChrome && ChromeRunning(processName)) throw new InvalidOperationException("Vui lòng đóng tất cả cửa sổ trình duyệt đã chọn trước khi cài. Nếu trình duyệt vẫn chạy nền, thoát trình duyệt ở khay hệ thống rồi thử lại.");
        if (!Directory.Exists(profile)) throw new InvalidOperationException("Không tìm thấy hồ sơ trình duyệt đã chọn.");
        string file = Path.Combine(profile, "Bookmarks"), original = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : null;
        var root = original == null ? new Dictionary<string, object> { {"version", 1}, {"roots", new Dictionary<string, object> { {"bookmark_bar", new Dictionary<string, object> { {"id", "1"}, {"name", "Bookmarks bar"}, {"type", "folder"}, {"children", new object[0]} }}, {"other", new Dictionary<string, object> { {"id", "2"}, {"name", "Other bookmarks"}, {"type", "folder"}, {"children", new object[0]} }}, {"synced", new Dictionary<string, object> { {"id", "3"}, {"name", "Mobile bookmarks"}, {"type", "folder"}, {"children", new object[0]} }} } } } : Json.Deserialize<Dictionary<string, object>>(original);
        if (!root.ContainsKey("version") || Convert.ToInt32(root["version"]) != 1) throw new InvalidOperationException("Định dạng dấu trang chưa được hỗ trợ. Dữ liệu chưa được thay đổi.");
        var roots = root["roots"] as Dictionary<string, object>;
        if (roots == null || !roots.ContainsKey("bookmark_bar")) throw new InvalidOperationException("Không tìm thấy thanh dấu trang. Dữ liệu chưa được thay đổi.");
        var bar = roots["bookmark_bar"] as Dictionary<string, object>;
        var children = bar["children"] as IList;
        if (children == null) throw new InvalidOperationException("Không đọc được dấu trang. Dữ liệu chưa được thay đổi.");
        var items = new List<object>(); foreach (object child in children) items.Add(child); Dictionary<string, object> existing = null;
        foreach (var item in items) { var d = item as Dictionary<string, object>; if (d != null && d.ContainsKey("url") && Convert.ToString(d["url"]).StartsWith("javascript:") && Uri.UnescapeDataString(Convert.ToString(d["url"])).Contains("locHoSoUpdateChannel")) { existing = d; break; } }
        if (existing == null) { existing = new Dictionary<string, object> { {"id", (MaxId(root)+1).ToString()}, {"guid", Guid.NewGuid().ToString()}, {"date_added", ((DateTime.UtcNow.Ticks - new DateTime(1601,1,1).Ticks)/10).ToString()}, {"type", "url"} }; items.Add(existing); }
        existing["name"] = Title; existing["url"] = payload;
        bar["children"] = items.ToArray(); bar["date_modified"] = ((DateTime.UtcNow.Ticks - new DateTime(1601,1,1).Ticks)/10).ToString();
        // Chrome regenerates this checksum when loading the edited bookmark tree.
        root.Remove("checksum"); string serialized = Json.Serialize(root);
        Json.Deserialize<Dictionary<string, object>>(serialized);
        string backup = original == null ? null : file + ".loc-ho-so-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,6) + ".bak";
        string pending = file + ".loc-ho-so-" + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(pending, serialized, new UTF8Encoding(false));
            if (checkChrome && ChromeRunning(processName)) throw new InvalidOperationException("Trình duyệt vừa được mở lại. Hãy đóng trình duyệt rồi thử lại.");
            if (original != null) { if (File.ReadAllText(file, Encoding.UTF8) != original) throw new InvalidOperationException("Dấu trang đã thay đổi trong lúc cài. Hãy thử lại."); File.Replace(pending, file, backup); }
            else File.Move(pending, file);
        } finally { if (File.Exists(pending)) File.Delete(pending); }
        return backup;
    }
}

internal sealed class ProfileChoice { internal string Path; internal string Name; public override string ToString() { return Name; } }
internal sealed class InstallerForm : Form {
    string selectedBrowser="Chrome"; readonly Button[] browserButtons=new Button[3]; readonly ComboBox profiles=new ComboBox(); readonly Button install=new Button(); readonly Label status=new Label(),intro=new Label();
    internal InstallerForm() {
        Text="Cài phần mềm lọc hồ sơ tạo báo cáo"; ClientSize=new Size(640,490);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(247,249,243);
        var title=new Label {Text="Thêm nút lọc hồ sơ vào Chrome",Location=new Point(28,22),Size=new Size(585,36),Font=new Font("Segoe UI",17,FontStyle.Bold),ForeColor=Color.FromArgb(41,64,31)};
        intro.Location=new Point(30,64);intro.Size=new Size(580,40);
        var browserLabel=new Label{Text="Chọn trình duyệt",Location=new Point(30,112),Size=new Size(180,22)};
        string[] names={"Chrome","Edge","Firefox"};
        for(int i=0;i<3;i++){int index=i;var button=new Button{Text=names[i],Location=new Point(30+i*104,140),Size=new Size(96,58),FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleRight,Padding=new Padding(0,0,5,0),Font=new Font("Segoe UI",9)};button.FlatAppearance.BorderSize=1;button.Paint+=(sender,args)=>DrawIcon(args.Graphics,index,8,15);button.Click+=(sender,args)=>{selectedBrowser=names[index];title.Text="Thêm nút lọc hồ sơ vào "+selectedBrowser;UpdateBrowserButtons();LoadProfiles();};browserButtons[i]=button;Controls.Add(button);}
        var profileLabel=new Label{Text="Hồ sơ sử dụng",Location=new Point(356,112),Size=new Size(340,22)};profiles.Location=new Point(356,153);profiles.Size=new Size(252,30);profiles.DropDownStyle=ComboBoxStyle.DropDownList;
        var picture=new Panel{Location=new Point(30,216),Size=new Size(578,100),BackColor=Color.White};picture.Paint+=DrawBrowser;
        install.Text="Thêm vào thanh bookmark";install.Location=new Point(30,332);install.Size=new Size(265,43);install.BackColor=Color.FromArgb(191,211,88);install.ForeColor=Color.FromArgb(28,43,18);install.FlatStyle=FlatStyle.Flat;install.FlatAppearance.BorderSize=0;
        var version=new Label{Text="v1.6.9 · Cập nhật 08/10/2026",Location=new Point(30,383),Size=new Size(578,23),Font=new Font("Segoe UI",9,FontStyle.Italic),ForeColor=Color.Gray};
        status.Location=new Point(30,413);status.Size=new Size(578,74);status.ForeColor=Color.FromArgb(59,73,49);
        Controls.AddRange(new Control[]{title,intro,browserLabel,profileLabel,profiles,picture,install,version,status});install.Click+=OnInstall;
        UpdateBrowserButtons();LoadProfiles();
    }
    void UpdateBrowserButtons(){for(int i=0;i<3;i++){bool active=browserButtons[i].Text==selectedBrowser;browserButtons[i].BackColor=active?Color.FromArgb(216,235,173):Color.White;browserButtons[i].FlatAppearance.BorderColor=active?Color.FromArgb(103,139,42):Color.FromArgb(213,220,205);}}
    static void DrawIcon(Graphics g,int index,int x,int y){
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if(index==0){using(var red=new SolidBrush(Color.FromArgb(234,67,53)))using(var yellow=new SolidBrush(Color.FromArgb(251,188,5)))using(var green=new SolidBrush(Color.FromArgb(52,168,83))){g.FillPie(red,x,y,27,27,210,120);g.FillPie(yellow,x,y,27,27,330,120);g.FillPie(green,x,y,27,27,90,120);g.FillEllipse(Brushes.White,x+7,y+7,13,13);g.FillEllipse(Brushes.DodgerBlue,x+9,y+9,9,9);}}
        else if(index==1){using(var gradient=new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(x,y,28,28),Color.FromArgb(0,192,147),Color.FromArgb(0,97,211),45)){g.FillEllipse(gradient,x,y,28,28);g.FillEllipse(Brushes.White,x+9,y+8,14,12);g.FillRectangle(gradient,x+3,y+16,20,8);}}
        else {g.FillEllipse(Brushes.Indigo,x+2,y+2,25,25);using(var flame=new SolidBrush(Color.FromArgb(255,143,24))){g.FillPie(flame,x,y,28,28,15,290);g.FillEllipse(Brushes.Indigo,x+9,y+9,13,12);g.FillPolygon(flame,new Point[]{new Point(x+4,y+12),new Point(x+6,y),new Point(x+15,y+6)});}}
    }
    void DrawBrowser(object sender,PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var toolbar=new SolidBrush(Color.FromArgb(236,239,245)))using(var small=new Font("Segoe UI",9))using(var text=new SolidBrush(Color.FromArgb(55,58,65)))using(var green=new SolidBrush(Color.FromArgb(205,226,157)))using(var border=new Pen(Color.FromArgb(198,205,216))){
            g.FillRectangle(toolbar,0,0,578,100);g.DrawRectangle(border,0,0,577,99);
            g.DrawString("‹    ›    ↻",small,text,14,15);g.FillRectangle(Brushes.White,98,9,433,31);g.DrawString("⋮",small,text,552,15);g.DrawString("☆",small,Brushes.Gray,507,17);
            g.FillRectangle(Brushes.White,1,49,576,49);g.DrawString("▣  Gmail",small,text,14,65);g.DrawString("▣  Drive",small,text,93,65);
            g.FillRectangle(green,170,57,209,29);g.DrawString("★  Lọc hồ sơ · Báo cáo",small,text,180,65);g.DrawString("»",small,text,550,65);
        }
    }
    void LoadProfiles(){
        profiles.Items.Clear();install.Enabled=true;string browser=selectedBrowser;
        if(browser=="Firefox"){
            profiles.Enabled=true;intro.Text="Chọn hồ sơ sử dụng. Đóng Firefox trước khi thêm bookmark.";status.Text="";
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Mozilla","Firefox"),ini=Path.Combine(root,"profiles.ini");
            try{var section=new Dictionary<string,string>();Action add=()=>{if(!section.ContainsKey("Path"))return;string path=section["Path"];if(!section.ContainsKey("IsRelative")||section["IsRelative"]=="1")path=Path.Combine(root,path.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(Path.Combine(path,"places.sqlite")))profiles.Items.Add(new ProfileChoice{Path=Path.GetFullPath(path),Name=section.ContainsKey("Name")?section["Name"]:Path.GetFileName(path)});};
                if(File.Exists(ini)){foreach(string line in File.ReadAllLines(ini)){string value=line.Trim();if(value.StartsWith("[")){add();section.Clear();}else{int equal=value.IndexOf('=');if(equal>0)section[value.Substring(0,equal)]=value.Substring(equal+1);}}add();}
                if(profiles.Items.Count>0)profiles.SelectedIndex=0;else{install.Enabled=false;status.Text="Chưa tìm thấy hồ sơ Firefox. Mở Firefox một lần rồi chạy lại tệp cài đặt.";}
            }catch{install.Enabled=false;status.Text="Không đọc được hồ sơ Firefox. Chưa thay đổi bookmark.";}return;
        }
        profiles.Enabled=true;intro.Text="Chọn hồ sơ sử dụng. Đóng "+browser+" trước khi thêm bookmark.";status.Text="";
        string vendor=browser=="Edge"?Path.Combine("Microsoft","Edge"):Path.Combine("Google","Chrome");string userData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),vendor,"User Data");
        try{
            Dictionary<string,object> cache=null;string localState=Path.Combine(userData,"Local State");if(File.Exists(localState)){var state=BookmarkStore.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(localState));var p=state.ContainsKey("profile")?state["profile"] as Dictionary<string,object>:null;if(p!=null&&p.ContainsKey("info_cache"))cache=p["info_cache"] as Dictionary<string,object>;}
            if(Directory.Exists(userData))foreach(string dir in Directory.GetDirectories(userData)){string id=Path.GetFileName(dir);if(id!="Default"&&!id.StartsWith("Profile "))continue;if(!File.Exists(Path.Combine(dir,"Preferences")))continue;string name=id;if(cache!=null&&cache.ContainsKey(id)){var p=cache[id] as Dictionary<string,object>;if(p!=null&&p.ContainsKey("name"))name=Convert.ToString(p["name"])+" ("+id+")";}profiles.Items.Add(new ProfileChoice{Path=dir,Name=name});}
            if(profiles.Items.Count>0)profiles.SelectedIndex=0;else{install.Enabled=false;status.Text="Chưa tìm thấy hồ sơ "+browser+". Mở trình duyệt một lần rồi chạy lại tệp cài đặt.";}
        }catch{install.Enabled=false;status.Text="Không đọc được hồ sơ trình duyệt. Chưa thay đổi bookmark.";}
    }
    internal static string FirefoxHtml(string payload){return "<!DOCTYPE NETSCAPE-Bookmark-file-1><META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\"><TITLE>Bookmarks</TITLE><H1>Bookmarks</H1><DL><p><DT><H3 PERSONAL_TOOLBAR_FOLDER=\"true\">Bookmarks Toolbar</H3><DL><p><DT><A HREF=\""+System.Security.SecurityElement.Escape(payload)+"\">Lọc hồ sơ · Báo cáo</A></DL><p></DL><p>";}
    void OnInstall(object sender,EventArgs e){
        try{string browser=selectedBrowser;
            var profile=profiles.SelectedItem as ProfileChoice;if(profile==null)return;
            if(browser=="Firefox")FirefoxBookmarks.Install(profile.Path,BookmarkStore.Payload("firefox-bookmark.txt"),true);
            else BookmarkStore.Install(profile.Path,BookmarkStore.Payload(),true,browser=="Edge"?"msedge":"chrome");status.Text="Đã thêm bookmark! Mở "+browser+" → Ctrl + Shift + B → mở hệ thống → bấm Lọc hồ sơ · Báo cáo.";install.Text="Đã thêm bookmark";install.Enabled=false;MessageBox.Show(this,"Đã thêm bookmark. Các bookmark cũ được giữ lại và sao lưu.","Hoàn tất",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }catch(Exception error){MessageBox.Show(this,error.Message,"Chưa thêm được bookmark",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    }
}
internal static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length==2&&args[0]=="--firefox-test"){try{FirefoxBookmarks.Install(args[1],BookmarkStore.Payload("firefox-bookmark.txt"),false);return 0;}catch(Exception error){File.WriteAllText(Path.Combine(args[1],"test-error.txt"),error.ToString());return 1;}}
        if(args.Length==2&&args[0]=="--self-test")return SelfTest(args[1]);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--preview"){using(var form=new InstallerForm()){PreparePreview(form);form.PerformLayout();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}}return 0;}
        Application.Run(new InstallerForm());return 0;
    }
    static void PreparePreview(Control control){
        var method=typeof(Control).GetMethod("CreateControl",BindingFlags.Instance|BindingFlags.NonPublic,null,new Type[]{typeof(bool)},null);method.Invoke(control,new object[]{true});foreach(Control child in control.Controls)PreparePreview(child);
    }
    static int SelfTest(string folder) {
        try {
            Directory.CreateDirectory(folder);string file=Path.Combine(folder,"Bookmarks");
            string fixture="{\"version\":1,\"checksum\":\"old\",\"roots\":{\"bookmark_bar\":{\"id\":\"1\",\"type\":\"folder\",\"children\":[{\"id\":\"9\",\"type\":\"url\",\"name\":\"Đã có\",\"url\":\"https://example.com\"}]},\"other\":{\"id\":\"2\",\"type\":\"folder\",\"children\":[]}}}";
            File.WriteAllText(file,fixture,new UTF8Encoding(false));string backup=BookmarkStore.Install(folder,BookmarkStore.Payload(),false);
            if(File.ReadAllText(backup)!=fixture)throw new Exception("Backup mismatch");string once=File.ReadAllText(file);var root=BookmarkStore.Json.Deserialize<Dictionary<string,object>>(once);var roots=(Dictionary<string,object>)root["roots"];var bar=(Dictionary<string,object>)roots["bookmark_bar"];var nodes=(IList)bar["children"];
            if(nodes.Count!=2||((Dictionary<string,object>)nodes[0])["url"].ToString()!="https://example.com")throw new Exception("Existing bookmark altered");
            BookmarkStore.Install(folder,BookmarkStore.Payload(),false);root=BookmarkStore.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));roots=(Dictionary<string,object>)root["roots"];bar=(Dictionary<string,object>)roots["bookmark_bar"];if(((IList)bar["children"]).Count!=2)throw new Exception("Duplicate app bookmark");
            File.WriteAllText(file,"not json");bool stopped=false;try{BookmarkStore.Install(folder,BookmarkStore.Payload(),false);}catch{stopped=true;}if(!stopped||File.ReadAllText(file)!="not json")throw new Exception("Malformed data overwritten");
            string edgeFolder=Path.Combine(folder,"edge");Directory.CreateDirectory(edgeFolder);File.WriteAllText(Path.Combine(edgeFolder,"Bookmarks"),fixture,new UTF8Encoding(false));BookmarkStore.Install(edgeFolder,BookmarkStore.Payload(),false,"msedge");BookmarkStore.Install(edgeFolder,BookmarkStore.Payload(),false,"msedge");var edgeRoot=BookmarkStore.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(edgeFolder,"Bookmarks")));var edgeRoots=(Dictionary<string,object>)edgeRoot["roots"];var edgeBar=(Dictionary<string,object>)edgeRoots["bookmark_bar"];if(((IList)edgeBar["children"]).Count!=2)throw new Exception("Edge bookmark update failed");
            string html=InstallerForm.FirefoxHtml(BookmarkStore.Payload("firefox-bookmark.txt"));if(!html.Contains("PERSONAL_TOOLBAR_FOLDER")||!html.Contains("javascript:"))throw new Exception("Firefox import invalid");
            File.WriteAllText(Path.Combine(folder,"self-test.txt"),"PASS: Chrome/Edge backup and bookmark preservation, idempotent update, malformed-data rejection, Firefox import HTML; direct Firefox database checks run separately.\n",Encoding.UTF8);return 0;
        }catch(Exception e){File.WriteAllText(Path.Combine(folder,"self-test.txt"),e.ToString());return 1;}
    }
}

