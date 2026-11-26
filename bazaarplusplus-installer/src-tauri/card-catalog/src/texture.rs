//! Texture2D and Sprite pixels as UnityPy's `.image` gives them: RGBA rows, top row first.

use crate::unity::{Env, Serialized};
use anyhow::{anyhow, bail, Result};

pub struct Image {
    pub width: usize,
    pub height: usize,
    /// RGBA, 4 bytes per pixel.
    pub rgba: Vec<u8>,
}

fn flip_rows(px: &[u8], stride: usize) -> Vec<u8> {
    px.chunks(stride).rev().flatten().copied().collect()
}

fn from_decoder(px: &[u32]) -> Vec<u8> {
    px.iter()
        .flat_map(|p| {
            let [b, g, r, a] = p.to_le_bytes();
            [r, g, b, a]
        })
        .collect()
}

/// Unity's rows are bottom-up; `flip` turns them top-down.
pub fn texture(env: &Env, file: &Serialized, path_id: i64, flip: bool) -> Result<Image> {
    let t = file.read(path_id)?;
    let width = usize::try_from(t.at("m_Width")?.int()?)?;
    let height = usize::try_from(t.at("m_Height")?.int()?)?;
    let format = t.at("m_TextureFormat")?.int()?;
    let mut data = t.at("image data")?.bytes()?.to_vec();
    if data.is_empty() {
        let s = t.at("m_StreamData")?;
        let offset = u64::try_from(s.at("offset")?.int()?)?;
        let size = usize::try_from(s.at("size")?.int()?)?;
        data = env.stream(s.at("path")?.str()?, offset, size)?;
    }
    let n = width * height;
    let need = |bytes: usize| -> Result<&[u8]> {
        data.get(..bytes)
            .ok_or_else(|| anyhow!("texture data shorter than {width}x{height} format {format}"))
    };
    let rgba: Vec<u8> = match format {
        // Alpha8: alpha only.
        1 => need(n)?.iter().flat_map(|&a| [0, 0, 0, a]).collect(),
        3 => need(n * 3)?
            .chunks(3)
            .flat_map(|c| [c[0], c[1], c[2], 255])
            .collect(),
        4 => need(n * 4)?.to_vec(),
        5 => need(n * 4)?
            .chunks(4)
            .flat_map(|c| [c[1], c[2], c[3], c[0]])
            .collect(),
        14 => need(n * 4)?
            .chunks(4)
            .flat_map(|c| [c[2], c[1], c[0], c[3]])
            .collect(),
        // R8 into RGB.
        63 => need(n)?.iter().flat_map(|&r| [r, 0, 0, 255]).collect(),
        10 | 12 | 25 | 26 | 27 | 28 | 29 => {
            let mut px = vec![0u32; n];
            let result = match format {
                10 => texture2ddecoder::decode_bc1(&data, width, height, &mut px),
                12 => texture2ddecoder::decode_bc3(&data, width, height, &mut px),
                25 => texture2ddecoder::decode_bc7(&data, width, height, &mut px),
                26 => texture2ddecoder::decode_bc4(&data, width, height, &mut px),
                27 => texture2ddecoder::decode_bc5(&data, width, height, &mut px),
                _ => texture2ddecoder::decode_unity_crunch(&data, width, height, &mut px),
            };
            result.map_err(|e| anyhow!("decode texture format {format}: {e}"))?;
            let mut out = from_decoder(&px);
            // UnityPy reads BC4 as L and BC5 as RGB: no alpha.
            if matches!(format, 26 | 27) {
                for p in out.chunks_mut(4) {
                    if format == 26 {
                        p[1] = p[0];
                        p[2] = p[0];
                    } else {
                        p[2] = 0;
                    }
                    p[3] = 255;
                }
            }
            out
        }
        other => bail!("unsupported texture format {other}"),
    };
    Ok(Image {
        width,
        height,
        rgba: if flip {
            flip_rows(&rgba, width * 4)
        } else {
            rgba
        },
    })
}

/// Python's `round()` on a float: halves go to the even neighbour.
pub fn py_round(x: f64) -> i64 {
    let r = x.round();
    #[expect(clippy::cast_possible_truncation, reason = "image coordinates")]
    let r = r as i64;
    if (x - x.trunc()).abs() == 0.5 && r % 2 != 0 {
        r - x.signum() as i64
    } else {
        r
    }
}

/// A plain sprite: its texture cropped to `textureRect`. Atlas, rotated and tight sprites
/// are refused rather than drawn differently from the published set.
pub fn sprite(env: &Env, file: &Serialized, path_id: i64) -> Result<Image> {
    let s = file.read(path_id)?;
    let rd = s.at("m_RD")?;
    let settings = rd.at("settingsRaw")?.int()?;
    if s.at("m_SpriteAtlas.m_PathID")?.int()? != 0 || settings & 0b11 != 0 {
        bail!("sprite needs atlas, rotation or mesh handling (settings {settings})");
    }
    let (tf, tid) = env.deref(file, rd.at("texture")?)?;
    let raw = texture(env, &tf, tid, false)?;
    let r = rd.at("textureRect")?;
    let (x, y, w, h) = (
        r.at("x")?.float()?,
        r.at("y")?.float()?,
        r.at("width")?.float()?,
        r.at("height")?.float()?,
    );
    let to = |v: f64| usize::try_from(py_round(v)).map_err(|_| anyhow!("negative sprite rect"));
    let (x0, y0, x1, y1) = (to(x)?, to(y)?, to(x + w)?, to(y + h)?);
    if x1 > raw.width || y1 > raw.height || x0 > x1 || y0 > y1 {
        bail!("sprite rect outside its texture");
    }
    let (cw, ch) = (x1 - x0, y1 - y0);
    let mut crop = Vec::with_capacity(cw * ch * 4);
    for row in y0..y1 {
        crop.extend_from_slice(&raw.rgba[(row * raw.width + x0) * 4..(row * raw.width + x1) * 4]);
    }
    Ok(Image {
        width: cw,
        height: ch,
        rgba: flip_rows(&crop, cw * 4),
    })
}
