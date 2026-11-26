//! A minimal reader for UnityFS bundles and the SerializedFiles inside them, driven by the
//! type trees the bundles embed (Unity 6, SerializedFile format 22 and later).

use anyhow::{anyhow, bail, Context, Result};
use std::cell::RefCell;
use std::collections::HashMap;
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::rc::Rc;

// ---------- byte reader ----------

pub struct Cursor<'a> {
    pub b: &'a [u8],
    pub p: usize,
    pub big: bool,
}

impl<'a> Cursor<'a> {
    pub fn new(b: &'a [u8], big: bool) -> Self {
        Self { b, p: 0, big }
    }
    fn take(&mut self, n: usize) -> Result<&'a [u8]> {
        let end = self.p.checked_add(n).ok_or_else(|| anyhow!("overflow"))?;
        if end > self.b.len() {
            bail!("read past end ({} + {} > {})", self.p, n, self.b.len());
        }
        let s = &self.b[self.p..end];
        self.p = end;
        Ok(s)
    }
    pub fn bytes(&mut self, n: usize) -> Result<&'a [u8]> {
        self.take(n)
    }
    pub fn u8(&mut self) -> Result<u8> {
        Ok(self.take(1)?[0])
    }
    pub fn u16(&mut self) -> Result<u16> {
        let a: [u8; 2] = self.take(2)?.try_into()?;
        Ok(if self.big {
            u16::from_be_bytes(a)
        } else {
            u16::from_le_bytes(a)
        })
    }
    pub fn u32(&mut self) -> Result<u32> {
        let a: [u8; 4] = self.take(4)?.try_into()?;
        Ok(if self.big {
            u32::from_be_bytes(a)
        } else {
            u32::from_le_bytes(a)
        })
    }
    pub fn u64(&mut self) -> Result<u64> {
        let a: [u8; 8] = self.take(8)?.try_into()?;
        Ok(if self.big {
            u64::from_be_bytes(a)
        } else {
            u64::from_le_bytes(a)
        })
    }
    pub fn i32(&mut self) -> Result<i32> {
        Ok(self.u32()? as i32)
    }
    pub fn i64(&mut self) -> Result<i64> {
        Ok(self.u64()? as i64)
    }
    pub fn cstr(&mut self) -> Result<String> {
        let rest = &self.b[self.p..];
        let n = rest
            .iter()
            .position(|&c| c == 0)
            .ok_or_else(|| anyhow!("unterminated string"))?;
        let s = String::from_utf8_lossy(&rest[..n]).into_owned();
        self.p += n + 1;
        Ok(s)
    }
    pub fn align(&mut self, n: usize) {
        self.p = self.p.div_ceil(n) * n;
    }
}

// ---------- UnityFS ----------

struct Block {
    unc_off: u64,
    unc_size: u32,
    comp_off: u64,
    comp_size: u32,
    flags: u16,
}

pub struct NodeInfo {
    pub offset: u64,
    pub size: u64,
    pub path: String,
}

/// Recently decompressed blocks, oldest first.
type BlockCache = (Vec<usize>, HashMap<usize, Rc<Vec<u8>>>);

pub struct Bundle {
    pub path: PathBuf,
    file: RefCell<File>,
    blocks: Vec<Block>,
    pub nodes: Vec<NodeInfo>,
    cache: RefCell<BlockCache>,
}

fn decompress(data: &[u8], size: usize, flags: u32) -> Result<Vec<u8>> {
    match flags & 0x3f {
        0 => Ok(data.to_vec()),
        2 | 3 => Ok(lz4_flex::block::decompress(data, size)?),
        other => bail!("unsupported bundle compression {other}"),
    }
}

impl Bundle {
    pub fn open(path: &Path) -> Result<Self> {
        let mut f = File::open(path).with_context(|| path.display().to_string())?;
        let mut head = vec![0u8; 4096];
        let n = f.read(&mut head)?;
        head.truncate(n);
        let mut c = Cursor::new(&head, true);
        if c.cstr()? != "UnityFS" {
            bail!("not a UnityFS bundle: {}", path.display());
        }
        let version = c.u32()?;
        let _unity = c.cstr()?;
        let _revision = c.cstr()?;
        let _size = c.i64()?;
        let comp_info = c.u32()? as usize;
        let unc_info = c.u32()? as usize;
        let flags = c.u32()?;
        if flags & 0x200 == 0 && flags & 0x400 != 0 {
            bail!("encrypted bundle");
        }
        if version >= 7 {
            c.align(16);
        }
        let header_end = c.p as u64;
        let len = f.metadata()?.len();
        let mut info = vec![0u8; comp_info];
        if flags & 0x80 != 0 {
            f.seek(SeekFrom::Start(len - comp_info as u64))?;
        } else {
            f.seek(SeekFrom::Start(header_end))?;
        }
        f.read_exact(&mut info)?;
        let info = decompress(&info, unc_info, flags)?;
        let mut c = Cursor::new(&info, true);
        c.bytes(16)?;
        let count = c.i32()? as usize;
        let mut data_start = if flags & 0x80 != 0 {
            header_end
        } else {
            header_end + comp_info as u64
        };
        if flags & 0x200 != 0 {
            data_start = data_start.div_ceil(16) * 16;
        }
        let mut blocks = Vec::with_capacity(count);
        let (mut unc_off, mut comp_off) = (0u64, data_start);
        for _ in 0..count {
            let unc_size = c.u32()?;
            let comp_size = c.u32()?;
            let flags = c.u16()?;
            blocks.push(Block {
                unc_off,
                unc_size,
                comp_off,
                comp_size,
                flags,
            });
            unc_off += unc_size as u64;
            comp_off += comp_size as u64;
        }
        let nodes_count = c.i32()? as usize;
        let mut nodes = Vec::with_capacity(nodes_count);
        for _ in 0..nodes_count {
            let offset = c.i64()? as u64;
            let size = c.i64()? as u64;
            let _flags = c.u32()?;
            let path = c.cstr()?;
            nodes.push(NodeInfo { offset, size, path });
        }
        Ok(Self {
            path: path.to_owned(),
            file: RefCell::new(f),
            blocks,
            nodes,
            cache: RefCell::new((Vec::new(), HashMap::new())),
        })
    }

    fn block(&self, i: usize) -> Result<Rc<Vec<u8>>> {
        if let Some(b) = self.cache.borrow().1.get(&i) {
            return Ok(b.clone());
        }
        let b = &self.blocks[i];
        let mut f = self.file.borrow_mut();
        f.seek(SeekFrom::Start(b.comp_off))?;
        let mut raw = vec![0u8; b.comp_size as usize];
        f.read_exact(&mut raw)?;
        let data = Rc::new(decompress(&raw, b.unc_size as usize, b.flags as u32)?);
        let mut cache = self.cache.borrow_mut();
        cache.0.push(i);
        cache.1.insert(i, data.clone());
        if cache.0.len() > 256 {
            let old = cache.0.remove(0);
            cache.1.remove(&old);
        }
        Ok(data)
    }

    /// Bytes `[offset, offset + len)` of the uncompressed stream.
    pub fn read(&self, offset: u64, len: usize) -> Result<Vec<u8>> {
        let mut out = Vec::with_capacity(len);
        let end = offset + len as u64;
        let mut i = self
            .blocks
            .partition_point(|b| b.unc_off + b.unc_size as u64 <= offset);
        while out.len() < len {
            let b = self
                .blocks
                .get(i)
                .ok_or_else(|| anyhow!("read past bundle end"))?;
            let data = self.block(i)?;
            let from = offset.max(b.unc_off) - b.unc_off;
            let to = end.min(b.unc_off + b.unc_size as u64) - b.unc_off;
            out.extend_from_slice(&data[from as usize..to as usize]);
            i += 1;
        }
        Ok(out)
    }
}

// ---------- type trees ----------

#[derive(Debug)]
pub struct TypeNode {
    pub ty: String,
    pub name: String,
    pub flags: u8,
    pub meta: u32,
    pub children: Vec<TypeNode>,
}

fn common(offset: u32) -> String {
    crate::common_strings::COMMON
        .binary_search_by_key(&offset, |e| e.0)
        .map(|i| crate::common_strings::COMMON[i].1.to_owned())
        .unwrap_or_else(|_| offset.to_string())
}

fn parse_tree(c: &mut Cursor<'_>, version: u32) -> Result<TypeNode> {
    if version >= 23 {
        if c.bytes(4)? != b"mhtt" {
            bail!("bad type tree magic");
        }
        c.i32()?;
    }
    let count = c.i32()? as usize;
    let strings_len = c.i32()? as usize;
    let mut raw = Vec::with_capacity(count);
    for _ in 0..count {
        let _version = c.u16()?;
        let level = c.u8()?;
        let flags = c.u8()?;
        let ty = c.u32()?;
        let name = c.u32()?;
        let _size = c.i32()?;
        let _index = c.i32()?;
        let meta = c.u32()?;
        if version >= 19 {
            c.u64()?;
        }
        raw.push((level, flags, ty, name, meta));
    }
    let strings = c.bytes(strings_len)?;
    let text = |v: u32| -> String {
        if v & 0x8000_0000 == 0 {
            let rest = &strings[v as usize..];
            let n = rest.iter().position(|&b| b == 0).unwrap_or(rest.len());
            String::from_utf8_lossy(&rest[..n]).into_owned()
        } else {
            common(v & 0x7fff_ffff)
        }
    };
    // Build the tree from levels.
    fn build(
        raw: &[(u8, u8, u32, u32, u32)],
        i: &mut usize,
        text: &dyn Fn(u32) -> String,
    ) -> TypeNode {
        let (level, flags, ty, name, meta) = raw[*i];
        *i += 1;
        let mut node = TypeNode {
            ty: text(ty),
            name: text(name),
            flags,
            meta,
            children: Vec::new(),
        };
        while *i < raw.len() && raw[*i].0 > level {
            node.children.push(build(raw, i, text));
        }
        node
    }
    let mut i = 0;
    Ok(build(&raw, &mut i, &text))
}

#[derive(Debug, Clone)]
pub enum Value {
    Int(i64),
    UInt(u64),
    Float(f64),
    Bool(bool),
    Str(String),
    Bytes(Vec<u8>),
    List(Vec<Value>),
    Obj(Vec<(String, Value)>),
}

impl Value {
    pub fn get(&self, key: &str) -> Option<&Value> {
        match self {
            Value::Obj(fields) => fields.iter().find(|(k, _)| k == key).map(|(_, v)| v),
            _ => None,
        }
    }
    pub fn at(&self, path: &str) -> Result<&Value> {
        let mut v = self;
        for key in path.split('.') {
            v = v
                .get(key)
                .ok_or_else(|| anyhow!("missing field {key} in {path}"))?;
        }
        Ok(v)
    }
    pub fn int(&self) -> Result<i64> {
        match self {
            Value::Int(v) => Ok(*v),
            Value::UInt(v) => Ok(*v as i64),
            Value::Bool(v) => Ok(*v as i64),
            _ => bail!("not an integer: {self:?}"),
        }
    }
    pub fn float(&self) -> Result<f64> {
        match self {
            Value::Float(v) => Ok(*v),
            _ => Ok(self.int()? as f64),
        }
    }
    pub fn str(&self) -> Result<&str> {
        match self {
            Value::Str(s) => Ok(s),
            _ => bail!("not a string"),
        }
    }
    pub fn list(&self) -> Result<&[Value]> {
        match self {
            Value::List(v) => Ok(v),
            _ => bail!("not a list"),
        }
    }
    pub fn bytes(&self) -> Result<&[u8]> {
        match self {
            Value::Bytes(v) => Ok(v),
            _ => bail!("not bytes"),
        }
    }
}

fn read_value(node: &TypeNode, c: &mut Cursor<'_>) -> Result<Value> {
    let mut align = node.meta & 0x4000 != 0;
    let v = match node.ty.as_str() {
        "SInt8" => Value::Int(c.u8()? as i8 as i64),
        "UInt8" | "char" => Value::UInt(c.u8()? as u64),
        "bool" => Value::Bool(c.u8()? != 0),
        "SInt16" | "short" => Value::Int(c.u16()? as i16 as i64),
        "UInt16" | "unsigned short" => Value::UInt(c.u16()? as u64),
        "SInt32" | "int" => Value::Int(c.i32()? as i64),
        "UInt32" | "unsigned int" | "Type*" => Value::UInt(c.u32()? as u64),
        "SInt64" | "long long" => Value::Int(c.i64()?),
        "UInt64" | "unsigned long long" | "FileSize" => Value::UInt(c.u64()?),
        "float" => Value::Float(f32::from_bits(c.u32()?) as f64),
        "double" => Value::Float(f64::from_bits(c.u64()?)),
        "string" => {
            let n = c.i32()? as usize;
            let s = String::from_utf8_lossy(c.bytes(n)?).into_owned();
            if node.children.first().is_some_and(|a| a.meta & 0x4000 != 0) {
                align = true;
            }
            Value::Str(s)
        }
        "TypelessData" => {
            let n = c.i32()? as usize;
            Value::Bytes(c.bytes(n)?.to_vec())
        }
        _ if node.children.first().is_some_and(|a| a.flags & 1 != 0) => {
            let array = &node.children[0];
            if array.meta & 0x4000 != 0 {
                align = true;
            }
            let n = c.i32()? as usize;
            let elem = &array.children[1];
            if matches!(elem.ty.as_str(), "UInt8" | "SInt8" | "char") && elem.children.is_empty() {
                Value::Bytes(c.bytes(n)?.to_vec())
            } else {
                let mut items = Vec::with_capacity(n.min(1 << 16));
                for _ in 0..n {
                    items.push(read_value(elem, c)?);
                }
                Value::List(items)
            }
        }
        _ => {
            let mut fields = Vec::with_capacity(node.children.len());
            for child in &node.children {
                fields.push((child.name.clone(), read_value(child, c)?));
            }
            Value::Obj(fields)
        }
    };
    if align {
        c.align(4);
    }
    Ok(v)
}

// ---------- SerializedFile ----------

pub struct ObjectInfo {
    pub path_id: i64,
    pub start: u64,
    pub size: u32,
    pub type_index: usize,
}

pub struct Serialized {
    pub bundle: Rc<Bundle>,
    pub node: usize,
    pub name: String,
    big: bool,
    data_offset: u64,
    pub types: Vec<(i32, Option<TypeNode>)>,
    pub objects: HashMap<i64, ObjectInfo>,
    pub externals: Vec<String>,
}

impl Serialized {
    pub fn open(bundle: Rc<Bundle>, node: usize) -> Result<Self> {
        let info = &bundle.nodes[node];
        let head = bundle.read(info.offset, 48.min(info.size as usize))?;
        let mut c = Cursor::new(&head, true);
        c.u32()?;
        c.u32()?;
        let version = c.u32()?;
        c.u32()?;
        if version < 22 {
            bail!("SerializedFile version {version} unsupported");
        }
        let big = c.u8()? != 0;
        c.bytes(3)?;
        let meta_size = c.u32()? as usize;
        let _file_size = c.i64()?;
        let data_offset = c.i64()? as u64;
        let meta = bundle.read(info.offset + 48, meta_size)?;
        let mut c = Cursor::new(&meta, big);
        let _unity = c.cstr()?;
        let _platform = c.i32()?;
        let tree = c.u8()? != 0;
        let type_count = c.i32()? as usize;
        let mut types = Vec::with_capacity(type_count);
        for _ in 0..type_count {
            let class_id = c.i32()?;
            c.u8()?; // stripped
            c.u16()?; // script type index
            if class_id == 114 {
                c.bytes(16)?;
            }
            c.bytes(16)?;
            let mut node = None;
            if tree {
                let mut has = true;
                if version >= 23 {
                    c.bytes(16)?;
                    has = c.i32()? != 0;
                }
                if has {
                    node = Some(parse_tree(&mut c, version)?);
                }
                if version >= 21 {
                    let n = c.i32()? as usize;
                    c.bytes(4 * n)?;
                }
            }
            types.push((class_id, node));
        }
        let count = c.i32()? as usize;
        let mut objects = HashMap::with_capacity(count);
        for _ in 0..count {
            c.align(4);
            let path_id = c.i64()?;
            let start = c.i64()? as u64;
            let size = c.u32()?;
            let type_index = c.i32()? as usize;
            objects.insert(
                path_id,
                ObjectInfo {
                    path_id,
                    start,
                    size,
                    type_index,
                },
            );
        }
        let scripts = c.i32()? as usize;
        for _ in 0..scripts {
            c.i32()?;
            c.align(4);
            c.i64()?;
        }
        let ext = c.i32()? as usize;
        let mut externals = Vec::with_capacity(ext);
        for _ in 0..ext {
            c.cstr()?;
            c.bytes(16)?;
            c.i32()?;
            externals.push(c.cstr()?);
        }
        let name = bundle.nodes[node].path.clone();
        Ok(Self {
            bundle,
            node,
            name,
            big,
            data_offset,
            types,
            objects,
            externals,
        })
    }

    pub fn class_id(&self, path_id: i64) -> Option<i32> {
        self.objects
            .get(&path_id)
            .map(|o| self.types[o.type_index].0)
    }

    pub fn read(&self, path_id: i64) -> Result<Value> {
        let o = self
            .objects
            .get(&path_id)
            .ok_or_else(|| anyhow!("no object {path_id} in {}", self.name))?;
        let tree = self.types[o.type_index]
            .1
            .as_ref()
            .ok_or_else(|| anyhow!("object without type tree"))?;
        let info = &self.bundle.nodes[self.node];
        let data = self
            .bundle
            .read(info.offset + self.data_offset + o.start, o.size as usize)?;
        let mut c = Cursor::new(&data, self.big);
        read_value(tree, &mut c)
    }
}

// ---------- environment ----------

/// Every node across the loaded bundles by its file name, for PPtr and stream resolution.
pub struct Env {
    bundles: Vec<Rc<Bundle>>,
    nodes: HashMap<String, (usize, usize)>,
    files: RefCell<HashMap<String, Rc<Serialized>>>,
}

fn base(path: &str) -> String {
    path.rsplit('/').next().unwrap_or(path).to_ascii_lowercase()
}

impl Default for Env {
    fn default() -> Self {
        Self::new()
    }
}

impl Env {
    pub fn new() -> Self {
        Self {
            bundles: Vec::new(),
            nodes: HashMap::new(),
            files: RefCell::new(HashMap::new()),
        }
    }
    pub fn add(&mut self, path: &Path) -> Result<Vec<String>> {
        let bundle = Rc::new(Bundle::open(path)?);
        let i = self.bundles.len();
        let mut serialized = Vec::new();
        for (n, node) in bundle.nodes.iter().enumerate() {
            self.nodes.insert(base(&node.path), (i, n));
            if !node.path.ends_with(".resS") && !node.path.ends_with(".resource") {
                serialized.push(node.path.clone());
            }
        }
        self.bundles.push(bundle);
        Ok(serialized)
    }
    pub fn file(&self, name: &str) -> Result<Rc<Serialized>> {
        let key = base(name);
        if let Some(f) = self.files.borrow().get(&key) {
            return Ok(f.clone());
        }
        let (b, n) = *self
            .nodes
            .get(&key)
            .ok_or_else(|| anyhow!("file {name} not found"))?;
        let f = Rc::new(Serialized::open(self.bundles[b].clone(), n)?);
        self.files.borrow_mut().insert(key, f.clone());
        Ok(f)
    }
    pub fn stream(&self, path: &str, offset: u64, size: usize) -> Result<Vec<u8>> {
        let (b, n) = *self
            .nodes
            .get(&base(path))
            .ok_or_else(|| anyhow!("stream {path} not found"))?;
        let bundle = &self.bundles[b];
        bundle.read(bundle.nodes[n].offset + offset, size)
    }
    /// Follow a PPtr value from `from`.
    pub fn deref(&self, from: &Serialized, pptr: &Value) -> Result<(Rc<Serialized>, i64)> {
        let file_id = pptr.at("m_FileID")?.int()?;
        let path_id = pptr.at("m_PathID")?.int()?;
        let file = if file_id == 0 {
            self.file(&from.name)?
        } else {
            let ext = from
                .externals
                .get(file_id as usize - 1)
                .ok_or_else(|| anyhow!("bad file id"))?;
            self.file(ext)?
        };
        Ok((file, path_id))
    }
}
