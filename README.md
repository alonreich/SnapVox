<p align="center">
  <img src="src/snapvox/SnapVox.ico" width="128" height="128" alt="SnapVox Icon">
</p>

# SnapVox 📸🗣️

**SnapVox** is a modern, premium, and lightning-fast screen capture and annotation utility. Built from the ground up using **Avalonia UI** and **.NET 9**, it brings a beautiful, distraction-free interface to your desktop while packing incredibly powerful features—like fully interactive on-image OCR.

Whether you're snapping a quick region to share with a colleague, building complex annotations for documentation, or extracting text straight from an image without leaving the editor, SnapVox makes it frictionless.

## ✨ Features

- 🎯 **Precision Capture:** Capture your entire screen, a specific window, or a precise rectangular region across multiple monitors effortlessly.
- 🎨 **Beautiful Image Editor:** A sleek, dark-themed image editor that feels natively premium. 
- 🖍️ **Rich Annotations:** Draw shapes, glowing arrows, and add text overlays. 
- 🤖 **Interactive OCR Mode:** Powered by Tesseract, seamlessly toggle into OCR mode directly within the editor. The UI fades away, and detected text is dynamically highlighted—just click and drag to copy exactly what you need.
- 🚀 **Blazing Fast:** Available in a hyper-optimized Native AOT build for instant launch times and low memory footprint.
- ⚙️ **Highly Configurable:** Global hotkeys, startup behaviors, multi-monitor notification overlays, and more.

## 📦 Installation & Releases

SnapVox is distributed as a standalone executable. You can find the latest builds on the [Releases](../../releases) page.

1. **SnapVox.exe**: The core, blazing-fast Native AOT compiled executable.
2. **SnapVox_tesseract.exe**: The standard deployment executable bundled with OCR capabilities.

## 🛠️ Build from Source

Requirements:
- .NET 9.0 SDK
- Windows 10/11

Clone the repository and run the build script to generate the binaries in the `.\compiled\` directory:

```powershell
git clone https://github.com/alonreich/SnapVox.git
cd SnapVox
.\Build.cmd
```

## ⚖️ License & Open Source Compliance

SnapVox is proudly open-source and is licensed under the **GNU General Public License v3.0 (GPLv3)**. 

### Why GPLv3?
To provide you with the most reliable capture engine possible, portions of SnapVox’s low-level native capture logic and bounds calculations were adapted from **[Greenshot](https://getgreenshot.org/)**, an incredible open-source screenshot tool. Because Greenshot is licensed under the GPL, SnapVox inherits this license to ensure full compliance and respect for their hard work.

### Third-Party Acknowledgements
SnapVox stands on the shoulders of giants. We gratefully acknowledge the following open-source projects used in our software:

- **[Avalonia UI](https://github.com/AvaloniaUI/Avalonia):** (MIT License) - The powerhouse driving our beautiful, cross-platform user interface.
- **[SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp):** (Apache 2.0 via Split License) - Used for lightning-fast, cross-platform image processing, cropping, and rendering.
- **[Tesseract OCR](https://github.com/tesseract-ocr/tesseract) & [.NET Wrapper](https://github.com/charlesw/tesseract):** (Apache 2.0) - The engine behind our magical interactive text extraction.
- **[log4net](https://github.com/apache/logging-log4net):** (Apache 2.0) - Robust and reliable application logging.

For full license texts and copyright notices of our third-party dependencies, please see the `THIRDPARTYNOTICES.txt` file included in this repository.
