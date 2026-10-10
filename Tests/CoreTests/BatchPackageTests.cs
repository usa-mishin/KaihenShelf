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
        string Folder(string name,string guid,string path,string extra="")
        {
            string file=Path.Combine(root,name+".unitypackage");
            using var output=File.Create(file); using var gzip=new GZipStream(output,CompressionLevel.Fastest);
            PackageArchive.WriteEntry(gzip,guid+"/pathname",Encoding.UTF8.GetBytes(path));
            PackageArchive.WriteEntry(gzip,guid+"/asset.meta",Encoding.UTF8.GetBytes("fileFormatVersion: 2\nguid: "+guid+"\nfolderAsset: yes\n"+extra));
            gzip.Write(new byte[1024]);
            return file;
        }
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
        test("batch conflict choices require explicit selection and retain references and source membership",()=>{
            string first=Write("first",a,"Assets/shared.mat","color: red"), second=Write("second",a,"Assets/shared.mat","color: blue");
            string prefab=Write("dependent",b,"Assets/model.prefab","guid: "+a);
            var s=BatchPackageStage.Prepare(new[]{first,second,prefab},cache,allowChoices:true);
            try {
                Check(s.assets.Count==2 && s.batchConflicts.Count==1 && s.batchConflicts[0].selected==-1);
                bool blocked=false; try { BatchPackageStage.ResolveChoices(s); } catch(InvalidDataException) { blocked=true; }
                Check(blocked && File.ReadAllText(s.Payload(s.assets.Single(x=>x.guid==a),false))=="color: red");
                s.batchConflicts[0].selected=1; BatchPackageStage.ResolveChoices(s);
                Check(File.ReadAllText(s.Payload(s.assets.Single(x=>x.guid==a),false))=="color: blue");
                Check(s.sources.Count==3 && s.sources.Take(2).All(x=>x.guids.Single()==a));
                Check(File.ReadAllText(s.Payload(s.assets.Single(x=>x.guid==b),false))=="guid: "+a);
                s.batchConflicts[0].selected=0; BatchPackageStage.ResolveChoices(s);
                Check(File.ReadAllText(s.Payload(s.assets.Single(x=>x.guid==a),false))=="color: red");
            } finally { s.Cleanup(); }
            Check(Directory.GetDirectories(cache).Length==0);
        });
        test("batch conflict choices carry selected importer metadata with three alternatives",()=>{
            var s=BatchPackageStage.Prepare(new[]{Write("one",a,"Assets/a.txt","one"),Write("two",a,"Assets/a.txt","one","userData: second\n"),Write("three",a,"Assets/a.txt","three","userData: third\n")},cache,allowChoices:true);
            try {
                Check(s.batchConflicts.Single().variants.Count==3);
                s.batchConflicts[0].selected=2; BatchPackageStage.ResolveChoices(s);
                Check(File.ReadAllText(s.Payload(s.assets.Single(),false))=="three");
                Check(File.ReadAllText(s.Payload(s.assets.Single(),true)).Contains("userData: third"));
                Check(DirectPackageStage.Hash(s.Payload(s.assets.Single(),true))==s.assets[0].metaHash);
            } finally { s.Cleanup(); }
        });
        test("batch validates all conflict selections before applying any",()=>{
            var s=BatchPackageStage.Prepare(new[]{Write("a1",a,"Assets/a.txt","a1"),Write("a2",a,"Assets/a.txt","a2"),Write("b1",b,"Assets/b.txt","b1"),Write("b2",b,"Assets/b.txt","b2")},cache,allowChoices:true);
            try {
                s.batchConflicts[0].selected=1;
                bool blocked=false; try { BatchPackageStage.ResolveChoices(s); } catch(InvalidDataException) { blocked=true; }
                Check(blocked && File.ReadAllText(s.Payload(s.assets.Single(x=>x.guid==a),false))=="a1");
            } finally { s.Cleanup(); }
        });
        test("batch choice mode still blocks GUID path collisions and cleans up",()=>{
            bool rejected=false;
            try { var s=BatchPackageStage.Prepare(new[]{Write("left",a,"Assets/a.txt","one"),Write("right",a,"Assets/b.txt","two")},cache,allowChoices:true); s.Cleanup(); }
            catch(InvalidDataException) { rejected=true; }
            Check(rejected && Directory.GetDirectories(cache).Length==0);
        });
        test("batch rejects same destination with different GUIDs ignoring case",()=>Reject(Write("one",a,"Assets/a.txt","one"),Write("two",b,"Assets/A.txt","one")));
        test("batch merges same-path folders with matching settings but distinct GUIDs",()=>{
            var s=BatchPackageStage.Prepare(new[]{Folder("f1",a,"Assets/Shared"),Folder("f2",b,"Assets/Shared"),Folder("f3",b,"Assets/Shared")},cache);
            try { Check(s.assets.Count==1 && s.assets[0].folder && s.sources.Count==3); }
            finally { s.Cleanup(); }
        });
        test("batch refuses differing shared folder settings",()=>Reject(Folder("f1",a,"Assets/Shared"),Folder("f2",b,"Assets/Shared","userData: different\n")));
        test("batch refuses references to dropped folder GUID across buffer boundaries",()=>{
            Reject(Folder("f1",a,"Assets/Shared"),Folder("f2",b,"Assets/Shared"),Write("reference","cccccccccccccccccccccccccccccccc","Assets/ref.txt",new string(' ',65520)+"guid: "+b));
        });
        test("batch rejects file-folder conflict across separate packages",()=>Reject(Write("one",a,"Assets/Shop","one"),Write("two",b,"Assets/Shop/a.txt","one")));
        test("batch cancellation removes all owned staging",()=>{
            bool cancelled=false; using var c=new CancellationTokenSource(); c.Cancel();
            try { BatchPackageStage.Prepare(new[]{Write("one",a,"Assets/a.txt","one")},cache,c.Token); } catch(OperationCanceledException) { cancelled=true; }
            Check(cancelled && Directory.GetDirectories(cache).Length==0);
        });
    }
}
