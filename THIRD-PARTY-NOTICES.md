# Third-Party Notices

Layer Herder is MIT-licensed (see [LICENSE](LICENSE)). Nodify is no
longer included. The mapping-window program (`acad_layer_ui.exe`) is built from
the open-source Rust crates listed below. Versions and licenses come from
`rust/Cargo.lock` and each crate's published crates.io metadata.

Where a crate offers a choice ("X OR Y"), it is used under the first license
listed or any listed permissive license. The `self_cell` crate is used under
Apache-2.0 (its "OR GPL-2.0-only" option is not taken).

Full license texts and copyright notices for MIT, Apache-2.0, BSD, ISC, Zlib,
Unicode-3.0, BSL-1.0, CC0-1.0 and Unlicense crates are shipped inside each
crate's source package. A bundled copy of those texts is still to be added
before a signed release.

## Rust crates (acad_layer_ui.exe)

Every crate below is linked into `acad_layer_ui.exe` (verified against the release build artifacts). Crates are grouped under the direct dependency that first brings them in; each crate is listed once. Crates used only at compile time (proc-macros such as `serde_derive`, and their helpers `syn`, `quote`, `proc-macro2`) are not listed because they are not in the shipped binary.

### acad_layer_core 0.1.0 (our crate) (6 crates)

| Crate | Version | License | Repository |
|---|---|---|---|
| itoa | 1.0.18 | MIT OR Apache-2.0 | https://github.com/dtolnay/itoa |
| memchr | 2.8.3 | Unlicense OR MIT | https://github.com/BurntSushi/memchr |
| serde | 1.0.229 | MIT OR Apache-2.0 | https://github.com/serde-rs/serde |
| serde_core | 1.0.229 | MIT OR Apache-2.0 | https://github.com/serde-rs/serde |
| serde_json | 1.0.151 | MIT OR Apache-2.0 | https://github.com/serde-rs/json |
| zmij | 1.0.23 | MIT | https://github.com/dtolnay/zmij |

### acad_layer_ipc 0.1.0 (our crate) (no new crates; all covered above)

### eframe 0.35.0 (154 crates)

| Crate | Version | License | Repository |
|---|---|---|---|
| accesskit | 0.24.1 | MIT OR Apache-2.0 | https://github.com/AccessKit/accesskit |
| adler2 | 2.0.1 | 0BSD OR MIT OR Apache-2.0 | https://github.com/oyvindln/adler2 |
| ahash | 0.8.12 | MIT OR Apache-2.0 | https://github.com/tkaitchuck/ahash |
| allocator-api2 | 0.2.21 | MIT OR Apache-2.0 | https://github.com/zakarumych/allocator-api2 |
| arboard | 3.6.1 | MIT OR Apache-2.0 | https://github.com/1Password/arboard |
| arrayvec | 0.7.8 | MIT OR Apache-2.0 | https://github.com/bluss/arrayvec |
| ash | 0.38.0+1.3.281 | MIT OR Apache-2.0 | https://github.com/ash-rs/ash |
| bit-set | 0.9.1 | Apache-2.0 OR MIT | https://github.com/contain-rs/bit-set |
| bit-vec | 0.9.1 | Apache-2.0 OR MIT | https://github.com/contain-rs/bit-vec |
| bitflags | 2.13.2 | MIT OR Apache-2.0 | https://github.com/bitflags/bitflags |
| bytemuck | 1.25.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/Lokathor/bytemuck |
| byteorder-lite | 0.1.0 | Unlicense OR MIT | https://github.com/image-rs/byteorder-lite |
| cfg-if | 1.0.5 | MIT OR Apache-2.0 | https://github.com/rust-lang/cfg-if |
| clipboard-win | 5.4.1 | BSL-1.0 | https://github.com/DoumanAsh/clipboard-win |
| codespan-reporting | 0.13.1 | Apache-2.0 | https://github.com/brendanzab/codespan |
| color | 0.3.3 | Apache-2.0 OR MIT | https://github.com/linebender/color |
| crc32fast | 1.5.2 | MIT OR Apache-2.0 | https://github.com/srijs/rust-crc32fast |
| cursor-icon | 1.2.0 | MIT OR Apache-2.0 OR Zlib | https://github.com/rust-windowing/cursor-icon |
| dpi | 0.1.2 | Apache-2.0 AND MIT | https://github.com/rust-windowing/winit |
| ecolor | 0.35.0 | MIT OR Apache-2.0 | https://github.com/emilk/egui |
| eframe | 0.35.0 | MIT OR Apache-2.0 | https://github.com/emilk/egui/tree/main/crates/eframe |
| egui | 0.35.0 | MIT OR Apache-2.0 | https://github.com/emilk/egui |
| egui-wgpu | 0.35.0 | MIT OR Apache-2.0 | https://github.com/emilk/egui/tree/main/crates/egui-wgpu |
| egui-winit | 0.35.0 | MIT OR Apache-2.0 | https://github.com/emilk/egui/tree/main/crates/egui-winit |
| either | 1.18.0 | MIT OR Apache-2.0 | https://github.com/rayon-rs/either |
| emath | 0.35.0 | MIT OR Apache-2.0 | https://github.com/emilk/egui/tree/main/crates/emath |
| epaint | 0.35.0 | MIT OR Apache-2.0 | https://github.com/emilk/egui/tree/main/crates/epaint |
| epaint_default_fonts | 0.35.0 | (MIT OR Apache-2.0) AND OFL-1.1 AND Ubuntu-font-1.0 | https://github.com/emilk/egui/tree/main/crates/epaint_default_fonts |
| equivalent | 1.0.2 | Apache-2.0 OR MIT | https://github.com/indexmap-rs/equivalent |
| error-code | 3.4.0 | BSL-1.0 | https://github.com/DoumanAsh/error-code |
| euclid | 0.22.14 | MIT OR Apache-2.0 | https://github.com/servo/euclid |
| fdeflate | 0.3.7 | MIT OR Apache-2.0 | https://github.com/image-rs/fdeflate |
| fearless_simd | 0.4.1 | Apache-2.0 OR MIT | https://github.com/linebender/fearless_simd |
| flate2 | 1.1.10 | MIT OR Apache-2.0 | https://github.com/rust-lang/flate2-rs |
| foldhash | 0.1.5 | Zlib | https://github.com/orlp/foldhash |
| foldhash | 0.2.0 | Zlib | https://github.com/orlp/foldhash |
| font-types | 0.11.3 | MIT OR Apache-2.0 | https://github.com/googlefonts/fontations |
| form_urlencoded | 1.2.2 | MIT OR Apache-2.0 | https://github.com/servo/rust-url |
| glow | 0.17.0 | MIT OR Apache-2.0 OR Zlib | https://github.com/grovesNL/glow |
| glutin_wgl_sys | 0.6.1 | Apache-2.0 | https://github.com/rust-windowing/glutin |
| gpu-allocator | 0.28.0 | MIT OR Apache-2.0 | https://github.com/Traverse-Research/gpu-allocator |
| gpu-descriptor | 0.3.2 | MIT OR Apache-2.0 | https://github.com/zakarumych/gpu-descriptor |
| gpu-descriptor-types | 0.2.0 | MIT OR Apache-2.0 | https://github.com/zakarumych/gpu-descriptor |
| guillotiere | 0.7.0 | MIT/Apache-2.0 | https://github.com/nical/guillotiere |
| half | 2.7.1 | MIT OR Apache-2.0 | https://github.com/VoidStarKat/half-rs |
| harfrust | 0.7.0 | MIT | https://github.com/harfbuzz/harfrust |
| hashbrown | 0.15.5 | MIT OR Apache-2.0 | https://github.com/rust-lang/hashbrown |
| hashbrown | 0.16.1 | MIT OR Apache-2.0 | https://github.com/rust-lang/hashbrown |
| hashbrown | 0.17.1 | MIT OR Apache-2.0 | https://github.com/rust-lang/hashbrown |
| hexf-parse | 0.2.1 | CC0-1.0 | https://github.com/lifthrasiir/hexf |
| home | 0.5.12 | MIT OR Apache-2.0 | https://github.com/rust-lang/cargo |
| icu_collections | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| icu_locale_core | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| icu_normalizer | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| icu_normalizer_data | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| icu_properties | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| icu_properties_data | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| icu_provider | 2.3.1 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| idna | 1.1.0 | MIT OR Apache-2.0 | https://github.com/servo/rust-url/ |
| idna_adapter | 1.2.2 | Apache-2.0 OR MIT | https://github.com/hsivonen/idna_adapter |
| image | 0.25.10 | MIT OR Apache-2.0 | https://github.com/image-rs/image |
| indexmap | 2.14.2 | Apache-2.0 OR MIT | https://github.com/indexmap-rs/indexmap |
| itertools | 0.14.0 | MIT OR Apache-2.0 | https://github.com/rust-itertools/itertools |
| khronos-egl | 6.0.0 | MIT/Apache-2.0 | https://github.com/timothee-haudebourg/khronos-egl |
| kurbo | 0.13.1 | Apache-2.0 OR MIT | https://github.com/linebender/kurbo |
| libc | 0.2.189 | MIT OR Apache-2.0 | https://github.com/rust-lang/libc |
| libloading | 0.8.9 | ISC | https://github.com/nagisa/rust_libloading/ |
| libm | 0.2.16 | MIT | https://github.com/rust-lang/compiler-builtins |
| linebender_resource_handle | 0.1.1 | Apache-2.0 OR MIT | https://github.com/linebender/raw_resource_handle |
| litemap | 0.8.3 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| lock_api | 0.4.14 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot |
| log | 0.4.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/log |
| miniz_oxide | 0.8.9 | MIT OR Zlib OR Apache-2.0 | https://github.com/Frommi/miniz_oxide/tree/master/miniz_oxide |
| miniz_oxide | 0.9.1 | MIT OR Zlib OR Apache-2.0 | https://github.com/Frommi/miniz_oxide/tree/master/miniz_oxide |
| moxcms | 0.8.1 | BSD-3-Clause OR Apache-2.0 | https://github.com/awxkee/moxcms.git |
| naga | 29.0.4 | MIT OR Apache-2.0 | https://github.com/gfx-rs/wgpu |
| nohash-hasher | 0.2.0 | Apache-2.0 OR MIT | https://github.com/paritytech/nohash-hasher |
| num-traits | 0.2.19 | MIT OR Apache-2.0 | https://github.com/rust-num/num-traits |
| once_cell | 1.21.4 | MIT OR Apache-2.0 | https://github.com/matklad/once_cell |
| ordered-float | 5.5.0 | MIT | https://github.com/reem/rust-ordered-float |
| parking_lot | 0.12.5 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot |
| parking_lot_core | 0.9.12 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot |
| peniko | 0.6.1 | Apache-2.0 OR MIT | https://github.com/linebender/peniko |
| percent-encoding | 2.3.2 | MIT OR Apache-2.0 | https://github.com/servo/rust-url/ |
| pin-project-lite | 0.2.17 | Apache-2.0 OR MIT | https://github.com/taiki-e/pin-project-lite |
| png | 0.18.1 | MIT OR Apache-2.0 | https://github.com/image-rs/image-png |
| pollster | 0.4.0 | Apache-2.0/MIT | https://github.com/zesterer/pollster |
| polycool | 0.4.0 | MIT OR Apache-2.0 | https://github.com/linebender/kurbo |
| potential_utf | 0.1.6 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| presser | 0.3.1 | MIT OR Apache-2.0 | https://github.com/EmbarkStudios/presser |
| profiling | 1.0.18 | MIT OR Apache-2.0 | https://github.com/aclysma/profiling |
| pxfm | 0.1.30 | BSD-3-Clause OR Apache-2.0 | https://github.com/awxkee/pxfm |
| range-alloc | 0.1.5 | MIT OR Apache-2.0 | https://github.com/gfx-rs/range-alloc |
| raw-window-handle | 0.6.2 | MIT OR Apache-2.0 OR Zlib | https://github.com/rust-windowing/raw-window-handle |
| read-fonts | 0.39.2 | MIT OR Apache-2.0 | https://github.com/googlefonts/fontations |
| renderdoc-sys | 1.1.0 | MIT OR Apache-2.0 | https://github.com/ebkalderon/renderdoc-rs |
| ron | 0.12.2 | MIT OR Apache-2.0 | https://github.com/ron-rs/ron |
| rustc-hash | 1.1.0 | Apache-2.0/MIT | https://github.com/rust-lang-nursery/rustc-hash |
| rustc-hash | 2.1.3 | Apache-2.0 OR MIT | https://github.com/rust-lang/rustc-hash |
| scopeguard | 1.2.0 | MIT OR Apache-2.0 | https://github.com/bluss/scopeguard |
| self_cell | 1.3.0 | Apache-2.0 OR GPL-2.0-only | https://github.com/Voultapher/self_cell |
| simd-adler32 | 0.3.10 | MIT | https://github.com/mcountryman/simd-adler32 |
| skrifa | 0.42.1 | MIT OR Apache-2.0 | https://github.com/googlefonts/fontations |
| smallvec | 1.16.2 | MIT OR Apache-2.0 | https://github.com/servo/rust-smallvec |
| smol_str | 0.2.2 | MIT OR Apache-2.0 | https://github.com/rust-analyzer/smol_str |
| spirv | 0.4.0+sdk-1.4.341.0 | Apache-2.0 | https://github.com/gfx-rs/rspirv |
| stable_deref_trait | 1.2.1 | MIT OR Apache-2.0 | https://github.com/storyyeller/stable_deref_trait |
| static_assertions | 1.1.0 | MIT OR Apache-2.0 | https://github.com/nvzqz/static-assertions-rs |
| thiserror | 2.0.21 | MIT OR Apache-2.0 | https://github.com/dtolnay/thiserror |
| tinystr | 0.8.4 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| tracing | 0.1.44 | MIT | https://github.com/tokio-rs/tracing |
| tracing-core | 0.1.36 | MIT | https://github.com/tokio-rs/tracing |
| type-map | 0.5.1 | MIT/Apache-2.0 | https://github.com/kardeiz/type-map |
| typeid | 1.0.3 | MIT OR Apache-2.0 | https://github.com/dtolnay/typeid |
| unicode-general-category | 1.1.0 | Apache-2.0 | https://github.com/yeslogic/unicode-general-category |
| unicode-ident | 1.0.26 | (MIT OR Apache-2.0) AND Unicode-3.0 | https://github.com/dtolnay/unicode-ident |
| unicode-segmentation | 1.13.3 | MIT OR Apache-2.0 | https://github.com/unicode-rs/unicode-segmentation |
| unicode-width | 0.2.2 | MIT OR Apache-2.0 | https://github.com/unicode-rs/unicode-width |
| url | 2.5.8 | MIT OR Apache-2.0 | https://github.com/servo/rust-url |
| utf8_iter | 1.0.4 | Apache-2.0 OR MIT | https://github.com/hsivonen/utf8_iter |
| uuid | 1.26.1 | Apache-2.0 OR MIT | https://github.com/uuid-rs/uuid |
| vello_common | 0.0.9 | Apache-2.0 OR MIT | https://github.com/linebender/vello |
| vello_cpu | 0.0.9 | Apache-2.0 OR MIT | https://github.com/linebender/vello |
| web-time | 1.1.0 | MIT OR Apache-2.0 | https://github.com/daxpedda/web-time |
| webbrowser | 1.2.4 | MIT OR Apache-2.0 | https://github.com/amodm/webbrowser-rs |
| wgpu | 29.0.4 | MIT OR Apache-2.0 | https://github.com/gfx-rs/wgpu |
| wgpu-core | 29.0.4 | MIT OR Apache-2.0 | https://github.com/gfx-rs/wgpu |
| wgpu-core-deps-windows-linux-android | 29.0.4 | MIT OR Apache-2.0 | https://github.com/gfx-rs/wgpu |
| wgpu-hal | 29.0.4 | MIT OR Apache-2.0 | https://github.com/gfx-rs/wgpu |
| wgpu-naga-bridge | 29.0.4 | MIT OR Apache-2.0 | https://github.com/gfx-rs/wgpu |
| wgpu-types | 29.0.4 | MIT OR Apache-2.0 | https://github.com/gfx-rs/wgpu |
| windows | 0.62.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows_x86_64_msvc | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows_x86_64_msvc | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-collections | 0.3.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-core | 0.62.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-future | 0.3.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-link | 0.2.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-numerics | 0.3.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-result | 0.4.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-strings | 0.5.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-sys | 0.52.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-sys | 0.60.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-sys | 0.61.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-targets | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-targets | 0.53.5 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| windows-threading | 0.2.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs |
| winit | 0.30.13 | Apache-2.0 | https://github.com/rust-windowing/winit |
| writeable | 0.6.4 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| yoke | 0.8.3 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| zerocopy | 0.8.59 | BSD-2-Clause OR Apache-2.0 OR MIT | https://github.com/google/zerocopy |
| zerofrom | 0.1.8 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| zerotrie | 0.2.5 | Unicode-3.0 | https://github.com/unicode-org/icu4x |
| zerovec | 0.11.8 | Unicode-3.0 | https://github.com/unicode-org/icu4x |

### egui 0.35.0 (no new crates; all covered above)

### raw-window-handle 0.6.2 (no new crates; all covered above)

### rfd 0.17.2 (1 crate)

| Crate | Version | License | Repository |
|---|---|---|---|
| rfd | 0.17.2 | MIT | https://github.com/PolyMeilex/rfd |

### serde 1.0.229 (no new crates; all covered above)

### serde_json 1.0.151 (no new crates; all covered above)

### spatial-ui-kit 0.1.0 (SpatialUiKit repo, sibling folder) (1 crate)

| Crate | Version | License | Repository |
|---|---|---|---|
| egui_dock | 0.20.1 | MIT | https://github.com/Adanos020/egui_dock |

### windows-sys 0.61.2 (no new crates; all covered above)

### Index (alphabetical)

- accesskit 0.24.1 — eframe 0.35.0
- adler2 2.0.1 — eframe 0.35.0
- ahash 0.8.12 — eframe 0.35.0
- allocator-api2 0.2.21 — eframe 0.35.0
- arboard 3.6.1 — eframe 0.35.0
- arrayvec 0.7.8 — eframe 0.35.0
- ash 0.38.0+1.3.281 — eframe 0.35.0
- bit-set 0.9.1 — eframe 0.35.0
- bit-vec 0.9.1 — eframe 0.35.0
- bitflags 2.13.2 — eframe 0.35.0
- bytemuck 1.25.2 — eframe 0.35.0
- byteorder-lite 0.1.0 — eframe 0.35.0
- cfg-if 1.0.5 — eframe 0.35.0
- clipboard-win 5.4.1 — eframe 0.35.0
- codespan-reporting 0.13.1 — eframe 0.35.0
- color 0.3.3 — eframe 0.35.0
- crc32fast 1.5.2 — eframe 0.35.0
- cursor-icon 1.2.0 — eframe 0.35.0
- dpi 0.1.2 — eframe 0.35.0
- ecolor 0.35.0 — eframe 0.35.0
- eframe 0.35.0 — eframe 0.35.0
- egui 0.35.0 — eframe 0.35.0
- egui_dock 0.20.1 — spatial-ui-kit 0.1.0 (SpatialUiKit repo, sibling folder)
- egui-wgpu 0.35.0 — eframe 0.35.0
- egui-winit 0.35.0 — eframe 0.35.0
- either 1.18.0 — eframe 0.35.0
- emath 0.35.0 — eframe 0.35.0
- epaint 0.35.0 — eframe 0.35.0
- epaint_default_fonts 0.35.0 — eframe 0.35.0
- equivalent 1.0.2 — eframe 0.35.0
- error-code 3.4.0 — eframe 0.35.0
- euclid 0.22.14 — eframe 0.35.0
- fdeflate 0.3.7 — eframe 0.35.0
- fearless_simd 0.4.1 — eframe 0.35.0
- flate2 1.1.10 — eframe 0.35.0
- foldhash 0.1.5 — eframe 0.35.0
- foldhash 0.2.0 — eframe 0.35.0
- font-types 0.11.3 — eframe 0.35.0
- form_urlencoded 1.2.2 — eframe 0.35.0
- glow 0.17.0 — eframe 0.35.0
- glutin_wgl_sys 0.6.1 — eframe 0.35.0
- gpu-allocator 0.28.0 — eframe 0.35.0
- gpu-descriptor 0.3.2 — eframe 0.35.0
- gpu-descriptor-types 0.2.0 — eframe 0.35.0
- guillotiere 0.7.0 — eframe 0.35.0
- half 2.7.1 — eframe 0.35.0
- harfrust 0.7.0 — eframe 0.35.0
- hashbrown 0.15.5 — eframe 0.35.0
- hashbrown 0.16.1 — eframe 0.35.0
- hashbrown 0.17.1 — eframe 0.35.0
- hexf-parse 0.2.1 — eframe 0.35.0
- home 0.5.12 — eframe 0.35.0
- icu_collections 2.3.0 — eframe 0.35.0
- icu_locale_core 2.3.0 — eframe 0.35.0
- icu_normalizer 2.3.0 — eframe 0.35.0
- icu_normalizer_data 2.3.0 — eframe 0.35.0
- icu_properties 2.3.0 — eframe 0.35.0
- icu_properties_data 2.3.0 — eframe 0.35.0
- icu_provider 2.3.1 — eframe 0.35.0
- idna 1.1.0 — eframe 0.35.0
- idna_adapter 1.2.2 — eframe 0.35.0
- image 0.25.10 — eframe 0.35.0
- indexmap 2.14.2 — eframe 0.35.0
- itertools 0.14.0 — eframe 0.35.0
- itoa 1.0.18 — acad_layer_core 0.1.0 (our crate)
- khronos-egl 6.0.0 — eframe 0.35.0
- kurbo 0.13.1 — eframe 0.35.0
- libc 0.2.189 — eframe 0.35.0
- libloading 0.8.9 — eframe 0.35.0
- libm 0.2.16 — eframe 0.35.0
- linebender_resource_handle 0.1.1 — eframe 0.35.0
- litemap 0.8.3 — eframe 0.35.0
- lock_api 0.4.14 — eframe 0.35.0
- log 0.4.34 — eframe 0.35.0
- memchr 2.8.3 — acad_layer_core 0.1.0 (our crate)
- miniz_oxide 0.8.9 — eframe 0.35.0
- miniz_oxide 0.9.1 — eframe 0.35.0
- moxcms 0.8.1 — eframe 0.35.0
- naga 29.0.4 — eframe 0.35.0
- nohash-hasher 0.2.0 — eframe 0.35.0
- num-traits 0.2.19 — eframe 0.35.0
- once_cell 1.21.4 — eframe 0.35.0
- ordered-float 5.5.0 — eframe 0.35.0
- parking_lot 0.12.5 — eframe 0.35.0
- parking_lot_core 0.9.12 — eframe 0.35.0
- peniko 0.6.1 — eframe 0.35.0
- percent-encoding 2.3.2 — eframe 0.35.0
- pin-project-lite 0.2.17 — eframe 0.35.0
- png 0.18.1 — eframe 0.35.0
- pollster 0.4.0 — eframe 0.35.0
- polycool 0.4.0 — eframe 0.35.0
- potential_utf 0.1.6 — eframe 0.35.0
- presser 0.3.1 — eframe 0.35.0
- profiling 1.0.18 — eframe 0.35.0
- pxfm 0.1.30 — eframe 0.35.0
- range-alloc 0.1.5 — eframe 0.35.0
- raw-window-handle 0.6.2 — eframe 0.35.0
- read-fonts 0.39.2 — eframe 0.35.0
- renderdoc-sys 1.1.0 — eframe 0.35.0
- rfd 0.17.2 — rfd 0.17.2
- ron 0.12.2 — eframe 0.35.0
- rustc-hash 1.1.0 — eframe 0.35.0
- rustc-hash 2.1.3 — eframe 0.35.0
- scopeguard 1.2.0 — eframe 0.35.0
- self_cell 1.3.0 — eframe 0.35.0
- serde 1.0.229 — acad_layer_core 0.1.0 (our crate)
- serde_core 1.0.229 — acad_layer_core 0.1.0 (our crate)
- serde_json 1.0.151 — acad_layer_core 0.1.0 (our crate)
- simd-adler32 0.3.10 — eframe 0.35.0
- skrifa 0.42.1 — eframe 0.35.0
- smallvec 1.16.2 — eframe 0.35.0
- smol_str 0.2.2 — eframe 0.35.0
- spirv 0.4.0+sdk-1.4.341.0 — eframe 0.35.0
- stable_deref_trait 1.2.1 — eframe 0.35.0
- static_assertions 1.1.0 — eframe 0.35.0
- thiserror 2.0.21 — eframe 0.35.0
- tinystr 0.8.4 — eframe 0.35.0
- tracing 0.1.44 — eframe 0.35.0
- tracing-core 0.1.36 — eframe 0.35.0
- type-map 0.5.1 — eframe 0.35.0
- typeid 1.0.3 — eframe 0.35.0
- unicode-general-category 1.1.0 — eframe 0.35.0
- unicode-ident 1.0.26 — eframe 0.35.0
- unicode-segmentation 1.13.3 — eframe 0.35.0
- unicode-width 0.2.2 — eframe 0.35.0
- url 2.5.8 — eframe 0.35.0
- utf8_iter 1.0.4 — eframe 0.35.0
- uuid 1.26.1 — eframe 0.35.0
- vello_common 0.0.9 — eframe 0.35.0
- vello_cpu 0.0.9 — eframe 0.35.0
- web-time 1.1.0 — eframe 0.35.0
- webbrowser 1.2.4 — eframe 0.35.0
- wgpu 29.0.4 — eframe 0.35.0
- wgpu-core 29.0.4 — eframe 0.35.0
- wgpu-core-deps-windows-linux-android 29.0.4 — eframe 0.35.0
- wgpu-hal 29.0.4 — eframe 0.35.0
- wgpu-naga-bridge 29.0.4 — eframe 0.35.0
- wgpu-types 29.0.4 — eframe 0.35.0
- windows 0.62.2 — eframe 0.35.0
- windows_x86_64_msvc 0.52.6 — eframe 0.35.0
- windows_x86_64_msvc 0.53.1 — eframe 0.35.0
- windows-collections 0.3.2 — eframe 0.35.0
- windows-core 0.62.2 — eframe 0.35.0
- windows-future 0.3.2 — eframe 0.35.0
- windows-link 0.2.1 — eframe 0.35.0
- windows-numerics 0.3.1 — eframe 0.35.0
- windows-result 0.4.1 — eframe 0.35.0
- windows-strings 0.5.1 — eframe 0.35.0
- windows-sys 0.52.0 — eframe 0.35.0
- windows-sys 0.60.2 — eframe 0.35.0
- windows-sys 0.61.2 — eframe 0.35.0
- windows-targets 0.52.6 — eframe 0.35.0
- windows-targets 0.53.5 — eframe 0.35.0
- windows-threading 0.2.1 — eframe 0.35.0
- winit 0.30.13 — eframe 0.35.0
- writeable 0.6.4 — eframe 0.35.0
- yoke 0.8.3 — eframe 0.35.0
- zerocopy 0.8.59 — eframe 0.35.0
- zerofrom 0.1.8 — eframe 0.35.0
- zerotrie 0.2.5 — eframe 0.35.0
- zerovec 0.11.8 — eframe 0.35.0
- zmij 1.0.23 — acad_layer_core 0.1.0 (our crate)

Total: 162 crates.

## NuGet packages (AcLayerStandardizer.dll, net48 build)

Nine packages are copied beside `AcLayerStandardizer.dll` in the net48 (AutoCAD 2021-2024)
payload. The net8.0 and net10.0 payloads ship only `AcLayerStandardizer.dll`; they use the
.NET runtime's own copies. PolySharp is a compile-time package: its polyfill types are
compiled into `AcLayerStandardizer.dll` for net48 and no separate file is shipped.

| Package | Version | License | Repository |
|---|---|---|---|
| Microsoft.Bcl.AsyncInterfaces | 8.0.0 | MIT | https://github.com/dotnet/runtime |
| PolySharp | 1.15.0 | MIT | https://github.com/Sergio0694/PolySharp |
| System.Buffers | 4.5.1 | MIT | https://github.com/dotnet/corefx |
| System.Memory | 4.5.5 | MIT | https://github.com/dotnet/corefx |
| System.Numerics.Vectors | 4.5.0 | MIT | https://github.com/dotnet/corefx |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | MIT | https://github.com/dotnet/runtime |
| System.Text.Encodings.Web | 8.0.0 | MIT | https://github.com/dotnet/runtime |
| System.Text.Json | 8.0.5 | MIT | https://github.com/dotnet/runtime |
| System.Threading.Tasks.Extensions | 4.5.4 | MIT | https://github.com/dotnet/corefx |
| System.ValueTuple | 4.5.0 | MIT | https://github.com/dotnet/corefx |

## Bundled fonts (inside acad_layer_ui.exe)

The Rust mapping window uses egui's default fonts, which eframe's `default_fonts` feature
compiles into the executable (from the `epaint_default_fonts` crate, version 0.35.0).

| Font | License | Notes |
|---|---|---|
| Ubuntu-Light | Ubuntu Font Licence 1.0 | Text font |
| NotoEmoji-Regular | SIL Open Font License 1.1 | Emoji glyphs |
| Hack-Regular | MIT (Source Foundry Authors) | Monospace font |
| emoji-icon-font | MIT (John Slegers) | Icon glyphs |

The SpatialUiKit repository also contains Inter (SIL Open Font License 1.1). It is not
compiled into the shipped executable, so it is not listed here.

## AutoCAD SDK assemblies (not redistributed)

`AcDbMgd.dll`, `AcCoreMgd.dll`, `AcMgd.dll`, `AdWindows.dll`,
`Autodesk.AutoCAD.Interop.dll`, and `Autodesk.AutoCAD.Interop.Common.dll` are
referenced at compile time only (`<Private>False</Private>` in the project
file) and are never copied into the installed bundle. They are proprietary
Autodesk assemblies supplied by the user's own AutoCAD installation, not
part of this software's distribution.
