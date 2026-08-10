//! AVIF encoding for macshot, behind a C ABI.
//!
//! The one save format macOS has and this port did not. `ImageEncoder.swift:179` reaches
//! for ImageIO, which writes AVIF on any supported macOS; Windows has no equivalent —
//! WIC enumerates no AVIF encoder, and the Store's "AV1 Video Extension" adds a decoder
//! only. So AVIF is written here, the way WebP is written by libwebp: a library carried
//! beside the app and called through one function.
//!
//! Rust rather than C++ because an AV1 encoder in C is libaom or SVT-AV1 — CMake, a C
//! toolchain, and a cross-compile story for win-arm64. `ravif` is a line in a manifest
//! and `cargo build --target aarch64-pc-windows-msvc`.
//!
//! Everything crossing the boundary is `extern "C"`, so nothing may unwind across it:
//! since Rust 1.81 a panic in an `extern "C"` function aborts the process, and aborting
//! macshot because one capture was an awkward size would lose the capture and everything
//! else the app was holding. Every entry point catches instead and returns a status.

use std::panic::catch_unwind;

use ravif::{BitDepth, Encoder, Img, RGB8, RGBA8};

/// The shape of the contract this library implements, checked once when it loads.
///
/// The DLL is built from this tree and shipped in the same directory as the app, so a
/// mismatch means a stale copy rather than a version to be negotiated with. The C# side
/// treats anything but this number as "no AVIF on this machine", which is a format
/// quietly missing from a menu instead of a crash at save time.
pub const ABI_VERSION: u32 = 1;

pub const STATUS_OK: i32 = 0;
pub const STATUS_NULL_ARGUMENT: i32 = -1;
pub const STATUS_EMPTY_IMAGE: i32 = -2;
pub const STATUS_SHORT_BUFFER: i32 = -3;
pub const STATUS_ENCODE_FAILED: i32 = -4;
pub const STATUS_PANIC: i32 = -5;

/// How much work rav1e does looking for a smaller file.
///
/// rav1e's range is 0 (exhaustive) to 10 (fastest) and its own default is 5. The number
/// that matters is 5, not the speed itself: `SpeedTweaks::from_my_preset` gives speeds
/// 5..=8 a partition range starting at 8px and only 1..=4 the 4px blocks, and 4px blocks
/// are what a screenshot's text edges are made of. So the curve has a cliff between 5
/// and 4 that a photograph would never show.
///
/// Measured at quality 85 on real screenshots rather than a synthetic one, which is what
/// hid this before: on the Windows VM, a 2038x1588 desktop went 104.5 KB at speed 8 to
/// 85.3 KB at speed 4, for 0.53s against 1.44s. Speed 3 took another 0.24s to save a
/// further 0.7%, so 4 is the knee and not a step on the way down.
///
/// The wait is real, and it is why `ImageDelivery.EncodeToBytesAsync` runs this off the
/// UI thread — a second and a half of frozen window would be the wrong trade, a second
/// and a half of background work for a fifth off every file is not.
const SPEED: u8 = 4;

/// Encoded bytes handed back to the caller, still owned by Rust's allocator.
///
/// `capacity` travels with the pointer because that is what `Vec::from_raw_parts` needs
/// to give the allocation back. libwebp gets away with a bare pointer by owning its own
/// deallocator; a `Vec` cannot, and guessing the capacity is undefined behaviour rather
/// than a leak.
#[repr(C)]
pub struct AvifBuffer {
    pub data: *mut u8,
    pub len: usize,
    pub capacity: usize,
}

impl AvifBuffer {
    const EMPTY: Self = Self {
        data: std::ptr::null_mut(),
        len: 0,
        capacity: 0,
    };
}

/// Answers whether this library loaded and speaks the ABI the caller was built against.
///
/// The cheapest export there is, which is the whole point: a missing file, a library
/// built for the other architecture, and one whose own dependencies are absent all
/// surface here rather than at the first save.
#[unsafe(no_mangle)]
pub extern "C" fn macshot_avif_abi_version() -> u32 {
    ABI_VERSION
}

/// Encodes a BGRA frame as AVIF.
///
/// `has_alpha` is the caller's statement about the fourth byte, not a guess this library
/// makes. An ordinary screen capture is BGRX with an undefined fourth byte, and encoding
/// that as alpha punches holes in screenshots at random.
///
/// # Safety
///
/// `pixels` must point to at least `stride * height` readable bytes and `out` to one
/// writable `AvifBuffer`. On any status but [`STATUS_OK`], `out` is left empty and must
/// not be freed.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn macshot_avif_encode_bgra(
    pixels: *const u8,
    width: u32,
    height: u32,
    stride: u32,
    has_alpha: bool,
    quality: i32,
    out: *mut AvifBuffer,
) -> i32 {
    if out.is_null() {
        return STATUS_NULL_ARGUMENT;
    }

    // Written before anything that can fail, so a caller that ignores the status and
    // frees unconditionally frees a null rather than an uninitialised pointer.
    unsafe { out.write(AvifBuffer::EMPTY) };

    if pixels.is_null() {
        return STATUS_NULL_ARGUMENT;
    }

    if width == 0 || height == 0 {
        return STATUS_EMPTY_IMAGE;
    }

    let required = match (stride as usize).checked_mul(height as usize) {
        Some(bytes) => bytes,
        None => return STATUS_SHORT_BUFFER,
    };

    let buffer = unsafe { std::slice::from_raw_parts(pixels, required) };

    // AssertUnwindSafe is not needed: the slice is the only capture and it is immutable,
    // so there is no state a panic could leave half-updated.
    let encoded = catch_unwind(|| {
        encode_bgra(buffer, width as usize, height as usize, stride as usize, has_alpha, quality)
    });

    match encoded {
        Ok(Ok(mut bytes)) => {
            bytes.shrink_to_fit();
            let buffer = AvifBuffer {
                len: bytes.len(),
                capacity: bytes.capacity(),
                data: bytes.as_mut_ptr(),
            };
            std::mem::forget(bytes);
            unsafe { out.write(buffer) };
            STATUS_OK
        }
        Ok(Err(status)) => status,
        Err(_) => STATUS_PANIC,
    }
}

/// Returns an [`AvifBuffer`] to the allocator it came from.
///
/// # Safety
///
/// The buffer must be one [`macshot_avif_encode_bgra`] returned with [`STATUS_OK`], and
/// must be passed here exactly once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn macshot_avif_free(buffer: AvifBuffer) {
    if buffer.data.is_null() {
        return;
    }

    // Dropped inside catch_unwind for the same reason the encode is: this is called from
    // a `finally`, and an abort there would take the process down on the success path.
    let _ = catch_unwind(|| unsafe {
        drop(Vec::from_raw_parts(buffer.data, buffer.len, buffer.capacity));
    });
}

/// The encoder itself, as a Rust function, so the tests reach it without the C ABI.
///
/// Errors are the same status codes the boundary returns, because the alternative is an
/// error type that exists only to be flattened into them one line later.
pub fn encode_bgra(
    pixels: &[u8],
    width: usize,
    height: usize,
    stride: usize,
    has_alpha: bool,
    quality: i32,
) -> Result<Vec<u8>, i32> {
    if width == 0 || height == 0 {
        return Err(STATUS_EMPTY_IMAGE);
    }

    if stride < width * 4 || pixels.len() < stride * height {
        return Err(STATUS_SHORT_BUFFER);
    }

    // ravif panics rather than erroring on a quality outside 1..=100, and this is the
    // only place that can be prevented — the value arrives from a settings file a user is
    // invited to edit by hand.
    let quality = quality.clamp(1, 100) as f32;

    // Eight bits, against ravif's default of ten. The source is an 8-bit screen capture
    // and every viewer takes it back to 8 bits to put it on a screen, so the extra depth
    // is spent on a round trip that cannot come back clean: measured against the lossless
    // original, the 10-bit file decoded to 8 bits was off by 0.8 of a level per pixel
    // where the 8-bit one was off by 0.06, which is 48 dB against 53 dB for a file of the
    // same size. macOS writes 8-bit too (`ImageEncoder.swift:179` through ImageIO).
    let encoder = Encoder::new()
        .with_quality(quality)
        .with_speed(SPEED)
        .with_bit_depth(BitDepth::Eight);

    // Alpha only where the caller said it means something. The cut-out Remove Background
    // produces is the exception among captures, and its alpha is straight rather than
    // premultiplied, which is what ravif's default UnassociatedClean mode expects.
    let encoded = if has_alpha {
        let rows = rows_rgba(pixels, width, height, stride);
        encoder.encode_rgba(Img::new(rows.as_slice(), width, height))
    } else {
        let rows = rows_rgb(pixels, width, height, stride);
        encoder.encode_rgb(Img::new(rows.as_slice(), width, height))
    };

    encoded.map(|image| image.avif_file).map_err(|_| STATUS_ENCODE_FAILED)
}

/// BGRA bytes as RGBA pixels, dropping any padding the stride describes.
fn rows_rgba(pixels: &[u8], width: usize, height: usize, stride: usize) -> Vec<RGBA8> {
    let mut out = Vec::with_capacity(width * height);

    for row in 0..height {
        let start = row * stride;
        for pixel in pixels[start..start + width * 4].chunks_exact(4) {
            out.push(RGBA8::new(pixel[2], pixel[1], pixel[0], pixel[3]));
        }
    }

    out
}

/// The same, with the fourth byte discarded rather than carried as alpha.
fn rows_rgb(pixels: &[u8], width: usize, height: usize, stride: usize) -> Vec<RGB8> {
    let mut out = Vec::with_capacity(width * height);

    for row in 0..height {
        let start = row * stride;
        for pixel in pixels[start..start + width * 4].chunks_exact(4) {
            out.push(RGB8::new(pixel[2], pixel[1], pixel[0]));
        }
    }

    out
}

#[cfg(test)]
mod tests {
    use super::*;

    /// One opaque pixel, laid out the way `CapturedFrame.BgraPixels` lays them out.
    const BLUE: [u8; 4] = [0xff, 0x00, 0x00, 0xff];
    const RED: [u8; 4] = [0x00, 0x00, 0xff, 0xff];

    fn frame(pixel: [u8; 4], width: usize, height: usize) -> Vec<u8> {
        pixel.repeat(width * height)
    }

    /// The byte order is the single thing this library can get wrong that still produces
    /// a valid file: swap the first and third and every screenshot saves with its reds
    /// and blues exchanged, which no status code and no test of the container would
    /// catch. Windows hands over BGRA and ravif wants RGBA.
    #[test]
    fn rows_rgba_reverses_the_windows_byte_order_rather_than_copying_it() {
        let pixels = frame(RED, 2, 1);
        let rows = rows_rgba(&pixels, 2, 1, 8);

        assert_eq!(rows[0], RGBA8::new(0xff, 0x00, 0x00, 0xff));
        assert_eq!(rows[1], RGBA8::new(0xff, 0x00, 0x00, 0xff));

        let rows = rows_rgba(&frame(BLUE, 1, 1), 1, 1, 4);
        assert_eq!(rows[0], RGBA8::new(0x00, 0x00, 0xff, 0xff));
    }

    /// The same swap, on the path every ordinary capture takes — an opaque screenshot
    /// goes through `rows_rgb`, so testing only the alpha path would leave the common
    /// case uncovered.
    #[test]
    fn rows_rgb_reverses_the_byte_order_on_the_path_opaque_captures_take() {
        assert_eq!(rows_rgb(&frame(RED, 1, 1), 1, 1, 4)[0], RGB8::new(0xff, 0x00, 0x00));
        assert_eq!(rows_rgb(&frame(BLUE, 1, 1), 1, 1, 4)[0], RGB8::new(0x00, 0x00, 0xff));
    }

    /// A stride wider than the row is padding, not picture. Reading it as picture shears
    /// the image progressively down its height — recognisable, but only once a capture
    /// has already been saved wrong.
    #[test]
    fn rows_skip_the_padding_a_wider_stride_describes() {
        // Two rows of one red pixel each, with one blue pixel of padding after each.
        let mut pixels = Vec::new();
        for _ in 0..2 {
            pixels.extend_from_slice(&RED);
            pixels.extend_from_slice(&BLUE);
        }

        let rows = rows_rgb(&pixels, 1, 2, 8);

        assert_eq!(rows.len(), 2);
        assert!(rows.iter().all(|pixel| *pixel == RGB8::new(0xff, 0x00, 0x00)));
    }

    /// `Encoder::with_quality` panics outside 1..=100, and the value reaches it from a
    /// settings file macshot invites the user to edit by hand. Clamping here is what
    /// keeps a hand-typed `"quality": 0` from being an abort instead of a saved file.
    #[test]
    fn quality_outside_the_valid_range_is_clamped_rather_than_a_panic() {
        for quality in [i32::MIN, 0, 101, i32::MAX] {
            let encoded = encode_bgra(&frame(RED, 4, 4), 4, 4, 16, false, quality);
            assert!(encoded.is_ok(), "quality {quality} was not clamped");
        }
    }

    /// The FFI entry point trusts the caller for the buffer length, so the safe function
    /// underneath it is where a mismatch has to be caught. Returning a status is the
    /// difference between a refused encode and a read past the end of the capture.
    #[test]
    fn a_buffer_too_small_for_its_dimensions_is_refused_not_read_past() {
        assert_eq!(encode_bgra(&frame(RED, 4, 4), 8, 8, 32, false, 90), Err(STATUS_SHORT_BUFFER));
        assert_eq!(encode_bgra(&frame(RED, 4, 4), 4, 4, 8, false, 90), Err(STATUS_SHORT_BUFFER));
        assert_eq!(encode_bgra(&[], 0, 0, 0, false, 90), Err(STATUS_EMPTY_IMAGE));
    }

    /// What the C# side receives has to be a file a viewer will open. An AVIF is an ISO
    /// base media container whose `ftyp` box names the `avif` brand; bytes that encoded
    /// without error but carried another brand would save with an extension that lies.
    #[test]
    fn the_encoded_bytes_are_an_avif_container_and_not_merely_non_empty() {
        let encoded = encode_bgra(&frame(RED, 32, 32), 32, 32, 128, false, 90).expect("encode");

        assert_eq!(&encoded[4..8], b"ftyp", "not an ISO base media file");
        assert_eq!(&encoded[8..12], b"avif", "the brand is not avif");
    }

    /// Alpha is carried only when the caller says the fourth byte means something, and
    /// the two paths are different ravif calls. Both must produce a container, because a
    /// cut-out from Remove Background takes the path an ordinary capture never does.
    #[test]
    fn a_frame_with_alpha_encodes_through_the_other_path_and_still_produces_a_container() {
        let mut pixels = frame(RED, 8, 8);
        pixels[3] = 0x00;

        let encoded = encode_bgra(&pixels, 8, 8, 32, true, 90).expect("encode");

        assert_eq!(&encoded[4..8], b"ftyp");
    }

    /// A null `out` is the one argument error that cannot be reported through `out`, and
    /// freeing a buffer the encode never filled must be a no-op — the C# side frees in a
    /// `finally`, which runs whether or not the encode returned OK.
    #[test]
    fn the_boundary_refuses_null_arguments_and_freeing_an_empty_buffer_is_a_no_op() {
        let pixels = frame(RED, 2, 2);
        let mut out = AvifBuffer::EMPTY;

        unsafe {
            assert_eq!(
                macshot_avif_encode_bgra(pixels.as_ptr(), 2, 2, 8, false, 90, std::ptr::null_mut()),
                STATUS_NULL_ARGUMENT
            );
            assert_eq!(
                macshot_avif_encode_bgra(std::ptr::null(), 2, 2, 8, false, 90, &mut out),
                STATUS_NULL_ARGUMENT
            );

            // Left empty by the failure above, and freed anyway, exactly as the caller does.
            assert!(out.data.is_null());
            macshot_avif_free(out);
        }
    }

    /// The round trip the C# side actually performs: encode, copy out `len` bytes, hand
    /// the allocation back. A capacity that did not travel with the pointer would make
    /// the free undefined rather than merely leaky.
    #[test]
    fn the_boundary_hands_back_a_buffer_that_can_be_copied_and_then_freed() {
        let pixels = frame(RED, 16, 16);
        let mut out = AvifBuffer::EMPTY;

        unsafe {
            let status = macshot_avif_encode_bgra(pixels.as_ptr(), 16, 16, 64, false, 90, &mut out);

            assert_eq!(status, STATUS_OK);
            assert!(out.len > 0 && out.capacity >= out.len);

            let copied = std::slice::from_raw_parts(out.data, out.len).to_vec();
            macshot_avif_free(out);

            assert_eq!(&copied[4..8], b"ftyp");
        }
    }
}
