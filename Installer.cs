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
    internal static bool ChromeRunning() { return Process.GetProcessesByName("chrome").Length != 0; }
    static long MaxId(object value) {
        long max = 0, n;
        var d = value as Dictionary<string, object>;
        if (d != null) { if (d.ContainsKey("id") && Int64.TryParse(Convert.ToString(d["id"]), out n)) max = n; foreach (var v in d.Values) max = Math.Max(max, MaxId(v)); }
        var list = value as IList; if (list != null) foreach (var v in list) max = Math.Max(max, MaxId(v));
        return max;
    }
    internal static string Install(string profile, string payload, bool checkChrome) {
        if (checkChrome && ChromeRunning()) throw new InvalidOperationException("Vui lòng đóng tất cả cửa sổ Chrome trước khi cài. Nếu Chrome vẫn chạy nền, thoát Chrome ở khay hệ thống rồi thử lại.");
        if (!Directory.Exists(profile)) throw new InvalidOperationException("Không tìm thấy hồ sơ Chrome đã chọn.");
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
            if (checkChrome && ChromeRunning()) throw new InvalidOperationException("Chrome vừa được mở lại. Hãy đóng Chrome rồi thử lại.");
            if (original != null) { if (File.ReadAllText(file, Encoding.UTF8) != original) throw new InvalidOperationException("Dấu trang đã thay đổi trong lúc cài. Hãy thử lại."); File.Replace(pending, file, backup); }
            else File.Move(pending, file);
        } finally { if (File.Exists(pending)) File.Delete(pending); }
        return backup;
    }
}

internal sealed class ProfileChoice { internal string Path; internal string Name; public override string ToString() { return Name; } }
internal sealed class InstallerForm : Form {
    readonly ComboBox profiles = new ComboBox(); readonly Button install = new Button(); readonly Label status = new Label();
    internal InstallerForm() {
        Text = "Cài Lọc hồ sơ · Báo cáo"; ClientSize = new Size(620,400); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",10); BackColor=Color.White;
        var title=new Label {Text="Thêm nút Lọc hồ sơ vào Chrome",Location=new Point(28,24),Size=new Size(560,45),Font=new Font("Segoe UI",18,FontStyle.Bold),ForeColor=Color.FromArgb(24,83,133)};
        var intro=new Label {Text="Chọn hồ sơ Chrome, đóng Chrome rồi bấm Cài dấu trang.\nỨng dụng kiểm tra phiên bản mới trên GitHub mỗi lần bạn mở bằng dấu trang.",Location=new Point(30,80),Size=new Size(560,65)};
        profiles.Location=new Point(30,154);profiles.Size=new Size(560,32);profiles.DropDownStyle=ComboBoxStyle.DropDownList;
        install.Text="Cài dấu trang";install.Location=new Point(30,204);install.Size=new Size(170,42);install.BackColor=Color.FromArgb(24,105,175);install.ForeColor=Color.White; install.FlatStyle=FlatStyle.Flat;
        status.Location=new Point(30,260);status.Size=new Size(560,115);status.Text="Không cần quyền quản trị. Các dấu trang hiện có được giữ lại và sao lưu.\nChỉ mã ứng dụng được tải từ GitHub. Dữ liệu người khám không được gửi lên GitHub.";
        Controls.AddRange(new Control[]{title,intro,profiles,install,status}); install.Click+=OnInstall; LoadProfiles();
    }
    void LoadProfiles() {
        string userData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google","Chrome","User Data");
        try {
            Dictionary<string,object> cache=null; string localState=Path.Combine(userData,"Local State");
            if(File.Exists(localState)) { var state=BookmarkStore.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(localState)); var p=state.ContainsKey("profile")?state["profile"] as Dictionary<string,object>:null; if(p!=null&&p.ContainsKey("info_cache"))cache=p["info_cache"] as Dictionary<string,object>; }
            if(Directory.Exists(userData))foreach(string dir in Directory.GetDirectories(userData)) { string id=Path.GetFileName(dir); if(id!="Default"&&!id.StartsWith("Profile "))continue; if(!File.Exists(Path.Combine(dir,"Preferences")))continue; string name=id; if(cache!=null&&cache.ContainsKey(id)){var p=cache[id] as Dictionary<string,object>; if(p!=null&&p.ContainsKey("name"))name=Convert.ToString(p["name"])+" ("+id+")";} profiles.Items.Add(new ProfileChoice{Path=dir,Name=name}); }
            if(profiles.Items.Count>0)profiles.SelectedIndex=0;else{install.Enabled=false;status.Text="Chưa tìm thấy hồ sơ Chrome. Mở Chrome một lần rồi chạy lại tệp cài đặt.";}
        } catch { install.Enabled=false; status.Text="Không đọc được hồ sơ Chrome. Chưa thay đổi dấu trang."; }
    }
    void OnInstall(object sender,EventArgs e) {
        try { var p=profiles.SelectedItem as ProfileChoice; if(p==null)return; string backup=BookmarkStore.Install(p.Path,BookmarkStore.Payload(),true); status.Text="Đã cài thành công!\nMở Chrome → Ctrl + Shift + B → đăng nhập hệ thống → bấm Lọc hồ sơ · Báo cáo.\nBấm Xem trên danh sách một lần để kết nối ứng dụng."; install.Text="Đã cài"; install.Enabled=false; MessageBox.Show(this,"Đã thêm dấu trang vào Chrome."+(backup==null?"":"\nĐã lưu bản sao dấu trang cũ trong hồ sơ Chrome."),"Hoàn tất",MessageBoxButtons.OK,MessageBoxIcon.Information); }
        catch(Exception error){MessageBox.Show(this,error.Message,"Chưa cài được",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    }
}
internal static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length==2&&args[0]=="--self-test")return SelfTest(args[1]);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new InstallerForm());return 0;
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
            File.WriteAllText(Path.Combine(folder,"self-test.txt"),"PASS: backup, preserve existing bookmarks, idempotent update, malformed-data rejection.\n",Encoding.UTF8);return 0;
        }catch(Exception e){File.WriteAllText(Path.Combine(folder,"self-test.txt"),e.ToString());return 1;}
    }
}
