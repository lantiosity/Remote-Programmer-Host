using System.Reflection;
using System.Runtime.Loader;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
if (args.Length > 1 && args[1] == "--languages") {
 var app=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(args[0], "WireLink.dll")));
 app.GetType("WireLink.App.LanguageResources")!.GetMethod("Install", BindingFlags.Public|BindingFlags.Static)!.Invoke(null,null);
 foreach(var culture in Directory.GetDirectories(Path.Combine(args[0],"lang"))) {
  var name=new AssemblyName("System.Windows.Forms.resources") {CultureInfo=CultureInfo.GetCultureInfo(Path.GetFileName(culture))};
  var resource=AssemblyLoadContext.Default.LoadFromAssemblyName(name);
  if(resource.GetName().CultureName!=name.CultureName)throw new Exception("Wrong culture");
  Console.WriteLine("PASS "+name.CultureName);
 }
 return;
}
var files=Directory.GetFiles(args[0],"*.dll").ToDictionary(Path.GetFileNameWithoutExtension,StringComparer.OrdinalIgnoreCase);
var refs=new Dictionary<string,string[]>();
foreach(var (name,file) in files) {
 using var stream=File.OpenRead(file); using var pe=new PEReader(stream);
 if(pe.HasMetadata) { var md=pe.GetMetadataReader(); refs[name]=md.AssemblyReferences.Select(h=>md.GetString(md.GetAssemblyReference(h).Name)).ToArray(); }
}
var reached=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var queue=new Queue<string>(new[]{"WireLink"});
while(queue.TryDequeue(out var name)) if(reached.Add(name)&&refs.TryGetValue(name,out var values)) foreach(var value in values) queue.Enqueue(value);
foreach(var name in refs.Keys.Except(reached).Order()) Console.WriteLine(name);
