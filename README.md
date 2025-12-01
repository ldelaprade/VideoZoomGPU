VLC with Zoom demo
==================

This repository is a variant of https://github.com/ldelaprade/VideoZoomCPU
that demonstrates how to use VLC's zoom filter with a video file.
Instead of using CPU for rendering, this version uses GPU via DirectX.

Starting from https://github.com/ldelaprade/VideoZoomCPU, Claude AI made the following changes:

- Added support for DirectX 11
- Implemented GPU-based video processing using SharpDX
- Improved performance and reduced latency
- Enhanced zoom functionality with smoother transitions

A huge bench of iterations with Claude AI were needed to achieve this results.

The final implementation showcases the power of GPU acceleration in video processing,
providing a seamless and efficient user experience.	


##### This project runs Direct3D9 known as obsolete (vs latest Direct3D11)

### Direct3D9 (D3D9)
Released in 2002 with Windows XP/Vista, targetting hardware features from that era.
Driven by the "fixed-function pipeline" (though later versions got shader support).
API design—stateful, less modular, more global (“set render state and draw”).
Maxes out at feature level 9.3 (shader model 3.0).
Legacy: Many older games and apps use D3D9 for broad compatibility and lower system requirements.
#### Limitations:
- No compute shaders.
- No multithreaded rendering support.
- Limited resource formats and GPGPU (general compute) features.
- WPF uses D3D9 for its own composition engine and only supports D3D9 in D3DImage.

### Direct3D11 (D3D11)
Released in 2009 (Windows 7 onwards), targeting modern GPUs.
Fully programmable pipeline: advanced vertex, geometry, pixel and compute shaders.
API supports efficient resource management, multithreading, deferred contexts, and DXGI integration for swapchain/control.
Higher feature levels and shader models (up to 5.0+).
Much better performance on modern hardware.
Greater graphics fidelity and richer compute capabilities.
Vortice, Microsoft.Windows.Direct3D, etc. provide active support for D3D11 and later.
Windows composition APIs, UWP/WinUI3, and new video frameworks leverage D3D11 as their minimum.
Best compatibility/future-proofing for Windows 10/11 and new graphics hardware.

### Why Switch?
#### D3D11 advantages:
- Performance: Improved parallelism, optimized for modern multi-core/multi-threaded CPUs and GPUs.
- Features: Compute shaders, tessellation shaders, advanced resource formats, efficient interrupts/events.
- Support: Modern libraries, drivers, and OS components actively support D3D11 and up.
- Future-proofing: Ongoing development with Windows 10/11, new GPUs.
- Compatibility: Easier integration with modern video APIs, media foundation, WinUI, and modern frameworks.

#### D3D9 disadvantages:
- Obsolete: No new features, hardware acceleration limited.
- **No longer actively supported/maintained in many SDKs and frameworks (e.g., SharpDX, SlimDX, etc.).
- Poor support for modern high-DPI/high-res/gaming/GPU compute.
- WPF’s D3D9-only interop is an artifact of legacy architecture.


#### Conclusion
- D3D11 is recommended for new projects and active maintenance because of better performance, advanced features, and robust modern support.
- D3D9 should only be used for legacy projects or when forced by platform limitations (e.g., classic WPF's D3DImage).
If you have legacy requirements or need WPF D3DImage, D3D9 is required; otherwise, D3D11 is the better choice.
