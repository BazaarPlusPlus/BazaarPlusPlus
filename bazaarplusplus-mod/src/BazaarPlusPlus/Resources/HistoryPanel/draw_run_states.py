from pathlib import Path
import cairosvg
from PIL import Image

# Deterministic, editable artwork; no generated images or external runtime assets.
HEAD = '<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128"><defs>\n<linearGradient id="gold" x1="0%" y1="0%" x2="80%" y2="100%"><stop offset="0%" stop-color="#fff0a0"/><stop offset="30%" stop-color="#efbd49"/><stop offset="57%" stop-color="#c18721"/><stop offset="100%" stop-color="#77500f"/></linearGradient>\n<linearGradient id="edge" x1="0%" y1="0%" x2="70%" y2="100%"><stop offset="0%" stop-color="#ffdf70"/><stop offset="48%" stop-color="#dba22b"/><stop offset="100%" stop-color="#926017"/></linearGradient>\n<radialGradient id="field" cx="50%" cy="40%" r="70%"><stop offset="0%" stop-color="FIELD1"/><stop offset="100%" stop-color="FIELD2"/></radialGradient>\n<linearGradient id="faceLight" x1="10%" y1="0%" x2="80%" y2="100%"><stop offset="0" stop-color="#ffed9a"/><stop offset="0.38" stop-color="#edbd58"/><stop offset="0.72" stop-color="#c79030"/><stop offset="1" stop-color="#98621c"/></linearGradient>\n<linearGradient id="faceDark" x1="0%" y1="0%" x2="90%" y2="100%"><stop offset="0" stop-color="#c9963a"/><stop offset="0.6" stop-color="#97601d"/><stop offset="1" stop-color="#55330f"/></linearGradient>\n</defs>\n'
FRAME = '<path d="M64 5 116 35 116 60 119 65 116 72 116 99 64 123 12 99 12 72 9 65 12 59 12 35Z" fill="url(#edge)" stroke="#241708" stroke-width="1.7"/>\n<path d="m64 7 49 29-5 3-44-25-44 25-5-3Z" fill="#f7d269"/>\n<path d="m15 38 5 3v53l44 24v4L13 98V74l5-3-1-12-3-2Z" fill="#986016"/>\n<path d="m113 38-5 3v53l-44 24v4l51-24V74l-5-3 1-12 3-2Z" fill="#b57a1a"/>\n<path d="M64 17 108 42v51l-44 25-44-25V42Z" fill="#302008" stroke="#6b400c" stroke-width="2"/>\n<path d="M64 23 103 45v45l-39 23-39-23V45Z" fill="url(#field)" stroke="#130e06" stroke-width="2.3"/>\n<path d="M64 27 99 47v41l-35 21-35-21V47Z" fill="none" stroke="FIELD3" stroke-width=".9"/>\n<path d="m64 6 11 7-11 12-11-12Z M64 104l10 10-10 8-10-8Z" fill="url(#gold)" stroke="#5d3b0a" stroke-width="1"/>\n<path d="m64 7-1 10-9-4 10 11Z M64 105l-1 9-7 0 8 7Z" fill="#ffda62"/>\n<path d="m64 7 1 10 8-4-9 11Z M64 105l1 9 7 0-8 7Z" fill="#99600e"/>\n<path d="m12 61 6 1v10l-6-2-3-5Z M116 61l-6 1v10l6-2 3-5Z" fill="url(#gold)" stroke="#4d300b" stroke-width="1"/>\n<path d="m13 36 14-8 M30 26l29-17 M69 9l30 18 M102 29l11 7 M15 40v17 M113 40v17 M16 95l36 20 M112 95l-36 20" fill="none" stroke="#ffe797" stroke-width=".8"/>\n'

BODIES = {
    "misfortune": ('#383c38', '#191c1a', '#666249', """
<g stroke="#29271d" stroke-width="2" stroke-linejoin="round">
<path d="M43 43H33v10q0 15 18 16M85 43h10v10q0 15-18 16" fill="none" stroke="#9b8c64" stroke-width="5"/>
<path d="M42 36h23l-5 12 10 6-8 13 9 8q-8 7-14 0-15-8-15-39Z" fill="#a59c77"/>
<path d="M71 36h15q0 28-13 37l-6-8 10-13-11-6Z" fill="#726949"/>
<path d="M60 77h9v12h12v7H47v-7h13Z" fill="#8d815c"/>
<path d="M45 39h15M50 92h26" fill="none" stroke="#c3b58a" stroke-width="2"/>
</g>"""),
    "abandoned": ('#393635', '#1f1c1a', '#6e6250', """
<g stroke="#2b2115" stroke-width="2" stroke-linejoin="round">
<path d="M45 33h6v66h-6Z" fill="#b58c43"/>
<path d="M51 38 87 43 81 55 89 63 60 69 51 65Z" fill="#817566"/>
<path d="M51 38 62 48 59 65 51 65Z" fill="#b6aa8e"/>
<path d="M62 48 87 43 81 55 60 60Z" fill="#655b50"/>
<path d="M41 32q7-10 14 0l-7 7Z" fill="#cfaa60"/>
<path d="M41 100h15M54 42l7 5M63 64l19-3" fill="none" stroke="#c7b58f" stroke-width="2"/>
</g>"""),
    "active": ('#1d504b', '#102b29', '#477d68', """
<g stroke="#352811" stroke-width="2" stroke-linejoin="round">
<path d="M44 36h40v8H44ZM44 87h40v8H44Z" fill="url(#gold)"/>
<path d="M49 45h30q1 13-12 20 13 9 12 21H49q-1-13 12-21-13-7-12-20Z" fill="#5e8a78"/>
<path d="M52 48h24q-2 8-12 13-10-5-12-13Z" fill="#f0d282"/>
<path d="M52 83q4-7 12-12 8 5 12 12Z" fill="#dbb24f"/>
<path d="M64 63v7M47 39h34M48 91h33" fill="none" stroke="#ffedb0" stroke-width="2"/>
<path d="M52 51q1 6 6 9M52 78l5-5" fill="none" stroke="#acc9b1" stroke-width="1.5"/>
</g>""")
}

if __name__ == "__main__":
    root = Path(__file__).parent
    atlas = Image.new("RGBA", (384, 128))
    for index, (name, (light, dark, edge, body)) in enumerate(BODIES.items()):
        svg = (HEAD + FRAME + body + "</svg>").replace("FIELD1", light).replace("FIELD2", dark).replace("FIELD3", edge)
        (root / (name + ".svg")).write_text(svg)
        png = cairosvg.svg2png(bytestring=svg.encode(), output_width=128, output_height=128)
        import io
        icon = Image.open(io.BytesIO(png)).convert("RGBA")
        atlas.paste(icon, (index * 128, 0))
    atlas.save(root / "run-state-badges.png")
