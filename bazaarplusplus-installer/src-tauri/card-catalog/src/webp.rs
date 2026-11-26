//! WebP encoding the way Pillow 12's `Image.save(format="WEBP")` drives libwebp 1.6.0
//! (`_webp.c` `WebPEncode_wrapper`), so equal pixels give equal files.

use anyhow::{bail, Result};
use libwebp_sys as w;

pub struct Options {
    pub lossless: bool,
    pub quality: f32,
    pub method: i32,
    pub exact: bool,
}

/// `rgba` holds `width * height` pixels; `alpha` false ignores the fourth byte (Pillow's RGB).
pub fn encode(
    rgba: &[u8],
    width: usize,
    height: usize,
    alpha: bool,
    options: &Options,
) -> Result<Vec<u8>> {
    if rgba.len() != width * height * 4 {
        bail!("pixel buffer does not match {width}x{height}");
    }
    let mut config = w::WebPConfig::new().map_err(|()| anyhow::anyhow!("WebPConfigInit failed"))?;
    config.lossless = i32::from(options.lossless);
    config.quality = options.quality;
    config.alpha_quality = 100;
    config.method = options.method;
    config.exact = i32::from(options.exact);
    // SAFETY: `config` is an initialised WebPConfig.
    if unsafe { w::WebPValidateConfig(&config) } == 0 {
        bail!("invalid WebP configuration");
    }
    let mut picture =
        w::WebPPicture::new().map_err(|()| anyhow::anyhow!("WebPPictureInit failed"))?;
    picture.width = i32::try_from(width)?;
    picture.height = i32::try_from(height)?;
    picture.use_argb = 1;
    // SAFETY: `picture` is initialised with its dimensions set; libwebp allocates `argb`.
    if unsafe { w::WebPPictureAlloc(&mut picture) } == 0 {
        bail!("cannot allocate a {width}x{height} picture");
    }
    let stride = picture.argb_stride as usize;
    // SAFETY: WebPPictureAlloc made `argb` hold `argb_stride * height` pixels.
    let argb = unsafe { std::slice::from_raw_parts_mut(picture.argb, stride * height) };
    for y in 0..height {
        for x in 0..width {
            let p = &rgba[(y * width + x) * 4..][..4];
            let a = if alpha { u32::from(p[3]) } else { 0xff };
            argb[y * stride + x] =
                u32::from(p[2]) | (u32::from(p[1]) << 8) | (u32::from(p[0]) << 16) | (a << 24);
        }
    }
    // SAFETY: a zeroed writer is valid input for WebPMemoryWriterInit.
    let mut writer: w::WebPMemoryWriter = unsafe { std::mem::zeroed() };
    // SAFETY: `writer` lives until after the encode and is cleared below.
    unsafe { w::WebPMemoryWriterInit(&mut writer) };
    picture.writer = Some(w::WebPMemoryWrite);
    picture.custom_ptr = std::ptr::from_mut(&mut writer).cast();
    // SAFETY: config and picture are valid; the writer callback appends to `writer`.
    let ok = unsafe { w::WebPEncode(&config, &mut picture) };
    let error = picture.error_code;
    // SAFETY: the picture was allocated by WebPPictureAlloc.
    unsafe { w::WebPPictureFree(&mut picture) };
    if ok == 0 {
        // SAFETY: the writer was initialised above.
        unsafe { w::WebPMemoryWriterClear(&mut writer) };
        bail!("WebP encoding failed: {error:?}");
    }
    // Pillow passes the encoded image through WebPMux before returning it.
    let image = w::WebPData {
        bytes: writer.mem,
        size: writer.size,
    };
    let mux = w::WebPMuxNew();
    let mut assembled = w::WebPData::default();
    // SAFETY: `mux` is a fresh mux; it borrows `image` (copy_data 0) until it is deleted,
    // and `writer` outlives it.
    let status = unsafe {
        w::WebPMuxSetImage(mux, &image, 0);
        let status = w::WebPMuxAssemble(mux, &mut assembled);
        w::WebPMuxDelete(mux);
        status
    };
    let out = if status == w::WebPMuxError::WEBP_MUX_OK && assembled.size > 0 {
        // SAFETY: WebPMuxAssemble filled `assembled` with `size` bytes.
        Ok(unsafe { std::slice::from_raw_parts(assembled.bytes, assembled.size) }.to_vec())
    } else {
        Err(anyhow::anyhow!("WebP mux assembly failed: {status:?}"))
    };
    // SAFETY: both buffers came from libwebp and are freed once.
    unsafe {
        w::WebPDataClear(&mut assembled);
        w::WebPMemoryWriterClear(&mut writer);
    }
    out
}
