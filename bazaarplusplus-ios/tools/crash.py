"""Print the exception and crashing-thread frames from the newest .ips report matching a prefix."""
import glob, json, os, sys
prefix = sys.argv[1]
n = int(sys.argv[2]) if len(sys.argv) > 2 else 30
paths = sorted(glob.glob(os.path.expanduser(f"~/Library/Logs/DiagnosticReports/{prefix}*.ips")), key=os.path.getmtime)
if not paths: sys.exit("no report")
text = open(paths[-1]).read()
d = json.loads(text.split("\n", 1)[1])
print(paths[-1])
print("exception:", d.get("exception"), "| termination:", d.get("termination", {}).get("indicator"))
if d.get("asi"): print("asi:", d["asi"])
th = [t for t in d["threads"] if t.get("triggered")][0]
imgs = d["usedImages"]
for f in th["frames"][:n]:
    im = imgs[f["imageIndex"]]
    print(f'  {os.path.basename(im.get("path", im.get("name", "?")))[:28]:28} {f.get("symbol", "?")}+{f.get("symbolLocation", 0)}  {f.get("sourceFile", "")}:{f.get("sourceLine", "")}')
