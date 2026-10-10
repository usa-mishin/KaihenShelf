using System.IO.Compression;
using System.Text;
using KaihenShelf;

static class BatchPackageTests
{
    public static void Run(Action<string, Action> test, string temp)
    {
        string root=Path.Combine(temp,"batch"); Directory.CreateDirectory(root);
        string Write(string name,string guid,string path,string body,string metaExtra="")
        {
            string file=Path.Combine(root,name+".unitypackage");
            using var output=File.Create(file); using var gzip=new GZipStream(output,CompressionLevel.Fastest);
            PackageArchive.WriteEntry(gzip,guid+"/pathname",Encoding.UTF8.GetBytes(path));
            PackageArchive.WriteEntry(gzip,guid+"/asset",Encoding.UTF8.GetBytes(body));
            PackageArchive.WriteEntry(gzip,guid+"/asset.meta",Encoding.UTF8.GetBytes("fileFormatVersion: 2\nguid: "+guid+"\n"+metaExtra));
            gzip.Write(new byte[1024]); return file;
        }
        void Check(bool ok) { if(!ok) throw new Exception("batch assertion failed"); }
        string a="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",b="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        string cache=Path.Combine(root,"cache");
        void Reject(params string[] files)
        {
            bool rejected=false; try { var s=BatchPackageStage.Prepare(files,cache); s.Cleanup(); } catch(InvalidDataException) { rejected=true; }
            Check(rejected && (!Directory.Exists(cache)||Directory.GetDirectories(cache).Length==0));
        }
        test("batch keeps cross-package GUID references and source membership",()=>{
            string material=Write("material",a,"Assets/商品/材質.mat","material"),prefab=Write("prefab",b,"Assets/商品/プレハブ.prefab","m_Material: {fileID: 2100000, guid: "+a+", type: 2}");
            var s=BatchPackageStage.Prepare(new[]{prefab,material},cache);
            try { Check(s.assets.Count==2 && s.sources.Count==2 && File.ReadAllText(s.Payload(s.assets.First(x=>x.guid==b),false)).Contains(a)); }
            finally { s.Cleanup(); }
        });
        test("batch deduplicates identical assets but preserves each source",()=>{
            var s=BatchPackageStage.Prepare(new[]{Write("one",a,"Assets/a.txt","same"),Write("two",a,"Assets/a.txt","same")},cache);
            try { Check(s.assets.Count==1 && s.sources.Count==2 && s.sources.All(x=>x.guids.Single()==a)); } finally { s.Cleanup(); }
        });
        test("batch rejects different bytes with same GUID before placement",()=>Reject(Write("one",a,"Assets/a.txt","one"),Write("two",a,"Assets/a.txt","two")));
        test("batch rejects conflicting importer settings with same GUID",()=>Reject(Write("one",a,"Assets/a.txt","one"),Write("two",a,"Assets/a.txt","one","userData: different\n")));
        test("batch rejects same destination with different GUIDs ignoring case",()=>Reject(Write("one",a,"Assets/a.txt","one"),Write("two",b,"Assets/A.txt","one")));
        test("batch rejects file-folder conflict across separate packages",()=>Reject(Write("one",a,"Assets/Shop","one"),Write("two",b,"Assets/Shop/a.txt","one")));
        test("batch cancellation removes all owned staging",()=>{
            bool cancelled=false; using var c=new CancellationTokenSource(); c.Cancel();
            try { BatchPackageStage.Prepare(new[]{Write("one",a,"Assets/a.txt","one")},cache,c.Token); } catch(OperationCanceledException) { cancelled=true; }
            Check(cancelled && Directory.GetDirectories(cache).Length==0);
        });
    }
}
