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
