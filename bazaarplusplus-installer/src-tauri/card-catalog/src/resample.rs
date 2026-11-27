//! Pillow 12's 8-bit two-pass resampling (`libImaging/Resample.c`), so resized pixels
//! match what the publication pipeline produced with `Image.resize`.

use std::f64::consts::PI;

#[derive(Clone, Copy)]
pub enum Filter {
    Box,
    Lanczos,
}

impl Filter {
    fn support(self) -> f64 {
        match self {
            Filter::Box => 0.5,
            Filter::Lanczos => 3.0,
        }
    }
    fn weight(self, x: f64) -> f64 {
        match self {
            Filter::Box => {
                if x > -0.5 && x <= 0.5 {
                    1.0
                } else {
                    0.0
                }
            }
            Filter::Lanczos => {
                if (-3.0..3.0).contains(&x) {
                    sinc(x) * sinc(x / 3.0)
                } else {
                    0.0
                }
            }
        }
    }
}

fn sinc(x: f64) -> f64 {
    if x == 0.0 {
        return 1.0;
    }
    let x = x * PI;
    x.sin() / x
}

const PRECISION_BITS: u32 = 32 - 8 - 2;

struct Coefficients {
    ksize: usize,
    bounds: Vec<(usize, usize)>,
    weights: Vec<i32>,
}

#[expect(
    clippy::cast_possible_truncation,
    reason = "C `(int)` casts, reproduced on purpose"
)]
fn coefficients(in_size: usize, out_size: usize, filter: Filter) -> Coefficients {
    // Pillow passes the box as C floats.
    let span = f64::from(in_size as f32 - 0.0_f32);
    let scale = span / out_size as f64;
    let filterscale = scale.max(1.0);
    let support = filter.support() * filterscale;
    let ksize = support.ceil() as usize * 2 + 1;
    let mut bounds = Vec::with_capacity(out_size);
    let mut weights = vec![0i32; out_size * ksize];
    let mut k = vec![0f64; ksize];
    for xx in 0..out_size {
        let center = (xx as f64 + 0.5) * scale;
        let ss = 1.0 / filterscale;
        let xmin = ((center - support + 0.5) as i32).max(0) as usize;
        let xmax = ((center + support + 0.5) as i32).min(in_size as i32) as usize - xmin;
        let mut ww = 0.0;
        for (x, slot) in k.iter_mut().enumerate().take(xmax) {
            let w = filter.weight((x as f64 + xmin as f64 - center + 0.5) * ss);
            *slot = w;
            ww += w;
        }
        for (x, slot) in k.iter().enumerate().take(xmax) {
            let v = if ww == 0.0 { *slot } else { *slot / ww };
            let scaled = v * f64::from(1u32 << PRECISION_BITS);
            weights[xx * ksize + x] = if v < 0.0 {
                (-0.5 + scaled) as i32
            } else {
                (0.5 + scaled) as i32
            };
        }
        bounds.push((xmin, xmax));
    }
    Coefficients {
        ksize,
        bounds,
        weights,
    }
}

fn clip8(ss: i32) -> u8 {
    (ss >> PRECISION_BITS).clamp(0, 255) as u8
}

/// Resize interleaved 8-bit channels, each channel independently, like Pillow's `resize`
/// on an image without premultiplied alpha.
pub fn resize(
    px: &[u8],
    width: usize,
    height: usize,
    channels: usize,
    out_w: usize,
    out_h: usize,
    filter: Filter,
) -> Vec<u8> {
    if (width, height) == (out_w, out_h) {
        return px.to_vec();
    }
    let horizontal = coefficients(width, out_w, filter);
    let mut vertical = coefficients(height, out_h, filter);
    let first = vertical.bounds[0].0;
    let last = vertical.bounds[out_h - 1].0 + vertical.bounds[out_h - 1].1;
    let need_vertical = out_h != height;
    let (src, w): (std::borrow::Cow<'_, [u8]>, usize) = if out_w != width {
        for b in &mut vertical.bounds {
            b.0 -= first;
        }
        let rows = last - first;
        let mut out = vec![0u8; out_w * rows * channels];
        for yy in 0..rows {
            let row = &px[(yy + first) * width * channels..];
            for xx in 0..out_w {
                let (xmin, xmax) = horizontal.bounds[xx];
                let k = &horizontal.weights[xx * horizontal.ksize..];
                for c in 0..channels {
                    let mut ss: i32 = 1 << (PRECISION_BITS - 1);
                    for x in 0..xmax {
                        ss = ss.wrapping_add(
                            i32::from(row[(x + xmin) * channels + c]).wrapping_mul(k[x]),
                        );
                    }
                    out[(yy * out_w + xx) * channels + c] = clip8(ss);
                }
            }
        }
        (std::borrow::Cow::Owned(out), out_w)
    } else {
        (std::borrow::Cow::Borrowed(px), width)
    };
    if !need_vertical {
        return src.into_owned();
    }
    let mut out = vec![0u8; w * out_h * channels];
    for yy in 0..out_h {
        let (ymin, ymax) = vertical.bounds[yy];
        let k = &vertical.weights[yy * vertical.ksize..];
        for xx in 0..w {
            for c in 0..channels {
                let mut ss: i32 = 1 << (PRECISION_BITS - 1);
                for y in 0..ymax {
                    let v = src[((y + ymin) * w + xx) * channels + c];
                    ss = ss.wrapping_add(i32::from(v).wrapping_mul(k[y]));
                }
                out[(yy * w + xx) * channels + c] = clip8(ss);
            }
        }
    }
    out
}

/// Pillow's RGBA resize: premultiply (`RGBa`), resample, then divide out alpha again.
pub fn resize_rgba(
    px: &[u8],
    width: usize,
    height: usize,
    out_w: usize,
    out_h: usize,
    filter: Filter,
) -> Vec<u8> {
    if (width, height) == (out_w, out_h) {
        return px.to_vec();
    }
    let mut pre = px.to_vec();
    for p in pre.chunks_mut(4) {
        let a = u32::from(p[3]);
        for c in &mut p[..3] {
            let t = u32::from(*c) * a + 128;
            *c = (((t >> 8) + t) >> 8) as u8;
        }
    }
    let mut out = resize(&pre, width, height, 4, out_w, out_h, filter);
    for p in out.chunks_mut(4) {
        let a = u32::from(p[3]);
        if a != 0 && a != 255 {
            for c in &mut p[..3] {
                *c = ((255 * u32::from(*c)) / a).min(255) as u8;
            }
        }
    }
    out
}
