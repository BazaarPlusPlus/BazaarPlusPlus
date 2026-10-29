// Patch only a hash-pinned game's derived FMOD integration, never its downloaded original.
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 3) throw new ArgumentException("FmodAdapter <game.json> <original.dll> <output directory>");
var profile = JsonNode.Parse(File.ReadAllText(args[0]))!;
var pin = profile["managedAdapters"]!.AsArray().Single(x => (string?)x!["adapter"] == "fmod-static-resonance")!;
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
if (Hash(args[1]) != (string)pin["sourceSha256"]!) throw new InvalidDataException("Unvalidated FMODUnity assembly");
using var module = ModuleDefinition.ReadModule(args[1]);
var platform = module.GetType("FMODUnity.Platform");
var system = module.GetType("FMOD.System");
var result = module.GetType("FMOD.RESULT");
var handle = system.Fields.Single(f => f.Name == "handle" && f.FieldType.FullName == "System.IntPtr");
var nativeModule = module.ModuleReferences.FirstOrDefault(m => m.Name == "__Internal");
if (nativeModule == null) { nativeModule = new ModuleReference("__Internal"); module.ModuleReferences.Add(nativeModule); }
var register = new MethodDefinition("BppRegisterResonance",
    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PInvokeImpl, result) {
    PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvCdecl, "bpp_fmod_register_resonance", nativeModule)
};
register.Parameters.Add(new ParameterDefinition("system", ParameterAttributes.None, handle.FieldType));
platform.Methods.Add(register);
var dynamicLoader = platform.Methods.Single(m => m.Name == "LoadDynamicPlugins");
var report = dynamicLoader.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>()
    .Single(m => m.Name == "Invoke" && m.DeclaringType.FullName.StartsWith("System.Action`2"));
foreach (var name in new[] { "LoadDynamicPlugins", "LoadStaticPlugins" }) {
    var method = platform.Methods.Single(m => m.Name == name);
    if (method.Parameters.Count != 2 || method.Parameters[0].ParameterType.FullName != "FMOD.System")
        throw new InvalidDataException("FMOD platform method shape changed");
    method.Body = new MethodBody(method);
    var il = method.Body.GetILProcessor();
    il.Emit(OpCodes.Ldarg_2); // Preserve the integration's CheckInitResult error reporting.
    il.Emit(OpCodes.Ldarga_S, method.Parameters[0]);
    il.Emit(OpCodes.Ldfld, handle);
    il.Emit(OpCodes.Call, register);
    il.Emit(OpCodes.Ldstr, "Registering static Resonance Audio DSPs");
    il.Emit(OpCodes.Callvirt, report);
    il.Emit(OpCodes.Ret);
}
foreach (var pair in new[] {
    ("FMOD_Studio_System_Initialize", "bpp_fmod_studio_initialize"),
    ("FMOD_Studio_System_Release", "bpp_fmod_studio_release") }) {
    var method = module.GetType("FMOD.Studio.System").Methods.Single(m => m.Name == pair.Item1 && m.IsPInvokeImpl);
    method.PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvCdecl, pair.Item2, nativeModule);
}
foreach (var pair in new[] {
    ("FMOD5_System_MixerSuspend", "bpp_fmod_mixer_suspend"),
    ("FMOD5_System_MixerResume", "bpp_fmod_mixer_resume") }) {
    var method = system.Methods.Single(m => m.Name == pair.Item1 && m.IsPInvokeImpl);
    method.PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvCdecl, pair.Item2, nativeModule);
}
Directory.CreateDirectory(args[2]);
string destination = Path.Combine(args[2], "FMODUnity.dll");
module.Write(destination);
var receipt = new JsonObject {
    ["gameId"] = (string)profile["id"]!, ["adapter"] = (string)pin["adapter"]!,
    ["assembly"] = "FMODUnity.dll", ["sourceSha256"] = (string)pin["sourceSha256"]!,
    ["patchedSha256"] = Hash(destination)
};
File.WriteAllText(Path.Combine(args[2], "manifest.json"), receipt.ToJsonString() + "\n");
Console.WriteLine("FMOD adapter: pinned integration patched; original preserved.");
