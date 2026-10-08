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
    internal static string Payload() { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bookmark.txt")) using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd().Trim(); }
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
    readonly ComboBox browsers=new ComboBox(), profiles=new ComboBox(); readonly Button install=new Button(); readonly Label status=new Label(),intro=new Label();
    internal InstallerForm() {
        Text="Cài phần mềm lọc hồ sơ tạo báo cáo"; ClientSize=new Size(640,490);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(247,249,243);
        var title=new Label {Text="Thêm nút lọc hồ sơ vào Chrome",Location=new Point(28,22),Size=new Size(585,36),Font=new Font("Segoe UI",17,FontStyle.Bold),ForeColor=Color.FromArgb(41,64,31)};
        intro.Location=new Point(30,64);intro.Size=new Size(580,40);
        var browserLabel=new Label{Text="Trình duyệt",Location=new Point(30,116),Size=new Size(140,22)};browsers.Location=new Point(30,142);browsers.Size=new Size(200,30);browsers.DropDownStyle=ComboBoxStyle.DropDownList;browsers.Items.AddRange(new object[]{"Chrome","Edge","Firefox"});
        var profileLabel=new Label{Text="Hồ sơ sử dụng",Location=new Point(248,116),Size=new Size(340,22)};profiles.Location=new Point(248,142);profiles.Size=new Size(360,30);profiles.DropDownStyle=ComboBoxStyle.DropDownList;
        var picture=new Panel{Location=new Point(30,194),Size=new Size(578,100),BackColor=Color.White};picture.Paint+=DrawBrowser;
        install.Text="Thêm vào thanh bookmark";install.Location=new Point(30,313);install.Size=new Size(265,43);install.BackColor=Color.FromArgb(191,211,88);install.ForeColor=Color.FromArgb(28,43,18);install.FlatStyle=FlatStyle.Flat;install.FlatAppearance.BorderSize=0;
        var version=new Label{Text="v1.6.8 · Cập nhật 08/10/2026",Location=new Point(30,367),Size=new Size(578,23),Font=new Font("Segoe UI",9,FontStyle.Italic),ForeColor=Color.Gray};
        status.Location=new Point(30,399);status.Size=new Size(578,74);status.ForeColor=Color.FromArgb(59,73,49);
        Controls.AddRange(new Control[]{title,intro,browserLabel,browsers,profileLabel,profiles,picture,install,version,status});install.Click+=OnInstall;
        browsers.SelectedIndexChanged+=(sender,args)=>{title.Text="Thêm nút lọc hồ sơ vào "+browsers.SelectedItem;LoadProfiles();};browsers.SelectedIndex=0;
    }
    void DrawBrowser(object sender,PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var border=new Pen(Color.FromArgb(208,216,200)))using(var address=new SolidBrush(Color.FromArgb(235,239,231)))using(var bookmark=new SolidBrush(Color.FromArgb(213,228,173)))using(var text=new SolidBrush(Color.FromArgb(43,58,35)))using(var small=new Font("Segoe UI",9)){
            g.DrawRectangle(border,0,0,577,99);g.FillRectangle(address,8,8,561,34);g.DrawString("‹    ›    ↻",small,text,16,17);g.FillRectangle(Brushes.White,103,13,432,24);g.DrawString("Thanh địa chỉ",small,Brushes.Gray,117,17);
            g.FillRectangle(bookmark,15,53,232,34);g.DrawString("★  Lọc hồ sơ · Báo cáo",small,text,25,61);g.DrawString("Thanh bookmark",small,Brushes.Gray,266,61);
        }
    }
    void LoadProfiles(){
        profiles.Items.Clear();install.Enabled=true;string browser=Convert.ToString(browsers.SelectedItem);
        if(browser=="Firefox"){profiles.Items.Add("Hồ sơ đang dùng trong Firefox");profiles.SelectedIndex=0;profiles.Enabled=false;intro.Text="Tạo tệp bookmark, sau đó nhập tệp vào Firefox theo hướng dẫn.";status.Text="Firefox dùng bước nhập bookmark. Các bookmark hiện có được giữ nguyên.";return;}
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
        try{string browser=Convert.ToString(browsers.SelectedItem);
            if(browser=="Firefox"){using(var save=new SaveFileDialog{FileName="Loc-ho-so-bookmark.html",Filter="Bookmark HTML|*.html",Title="Lưu bookmark cho Firefox"}){if(save.ShowDialog(this)!=DialogResult.OK)return;File.WriteAllText(save.FileName,FirefoxHtml(BookmarkStore.Payload()),new UTF8Encoding(false));status.Text="Đã tạo tệp bookmark. Trong Firefox: Ctrl + Shift + O → Import and Backup → Import Bookmarks from HTML → chọn tệp vừa lưu.";MessageBox.Show(this,status.Text,"Nhập bookmark vào Firefox",MessageBoxButtons.OK,MessageBoxIcon.Information);}return;}
            var profile=profiles.SelectedItem as ProfileChoice;if(profile==null)return;BookmarkStore.Install(profile.Path,BookmarkStore.Payload(),true,browser=="Edge"?"msedge":"chrome");status.Text="Đã thêm bookmark! Mở "+browser+" → Ctrl + Shift + B → mở hệ thống → bấm Lọc hồ sơ · Báo cáo.";install.Text="Đã thêm bookmark";install.Enabled=false;MessageBox.Show(this,"Đã thêm bookmark. Các bookmark cũ được giữ lại và sao lưu.","Hoàn tất",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }catch(Exception error){MessageBox.Show(this,error.Message,"Chưa thêm được bookmark",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    }
}
internal static class Program {
    [STAThread] static int Main(string[] args) {
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
            string html=InstallerForm.FirefoxHtml(BookmarkStore.Payload());if(!html.Contains("PERSONAL_TOOLBAR_FOLDER")||!html.Contains("javascript:"))throw new Exception("Firefox import invalid");
            File.WriteAllText(Path.Combine(folder,"self-test.txt"),"PASS: Chrome/Edge backup and bookmark preservation, idempotent update, malformed-data rejection, Firefox import HTML.\n",Encoding.UTF8);return 0;
        }catch(Exception e){File.WriteAllText(Path.Combine(folder,"self-test.txt"),e.ToString());return 1;}
    }
}
