# Third-Party Notices

DriverCompass is licensed under the MIT license (see [LICENSE](LICENSE)). It includes or uses the following third-party components.

## Bundled data

### pci.ids – The PCI ID Repository

- Source: https://pci-ids.ucw.cz/ (file `data/third-party/pci.ids`, embedded into `DriverCompass.Data`)
- Maintained by Albert Pool, Martin Mares, and other volunteers from the PCI ID Project.
- The file "can be distributed under either the GNU General Public License (version 2 or higher) or the 3-clause BSD License". **DriverCompass uses it under the 3-clause BSD License:**

```
Copyright (c) the PCI ID Project contributors (https://pci-ids.ucw.cz/)
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### usb.ids – not included

The USB ID list (linux-usb.org) does not state a license in its header, so its redistribution terms are unclear. It is therefore **not** bundled. USB vendors come from DriverCompass's own MIT-licensed `data/vendors.json`.

## Data downloaded at runtime (not bundled)

The optional version check (stage 2) reads official catalogs directly from the vendors (Microsoft Windows Update, Dell, Lenovo, HP) when the user turns a source on and clicks "Check versions". These files are not part of DriverCompass and are not redistributed; they are cached locally for the user only.

## Components shipped in the release ZIP

The release ZIP contains the full license and notice texts in the folder `licenses/` (the same folder is in this repository). [`licenses/packages.json`](licenses/packages.json) maps every shipped NuGet package to its folder; a test fails if a new package ships without one.

| Component | License | Texts |
|---|---|---|
| .NET runtime (self-contained), Microsoft.Extensions.DependencyInjection / Logging / Options / Primitives, System.Management, System.CodeDom | MIT | `licenses/dotnet-runtime` (incl. the runtime's third-party notices) |
| Windows Desktop runtime (`WindowsBase.dll`) | MIT | `licenses/windows-desktop-runtime` |
| Windows App SDK: Microsoft.WindowsAppSDK.WinUI, .Base, .Foundation, .InteractiveExperiences | Microsoft Software License Terms (redistributable runtime components) | `licenses/Microsoft.WindowsAppSDK` |
| Microsoft.Web.WebView2 (dependency of WinUI) | BSD-3-Clause-style license | `licenses/Microsoft.Web.WebView2` |
| Windows SDK projection (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`) | Windows SDK license terms; C#/WinRT: MIT | `licenses/windows-sdk-projection` (links) |
| CommunityToolkit.Mvvm | MIT | `licenses/CommunityToolkit.Mvvm` |
| Serilog, Serilog.Extensions.Logging, Serilog.Sinks.File | Apache-2.0 | `licenses/Serilog` |
| JsonSchema.Net, JsonPointer.Net, Json.More.Net (json-everything) | Source: MIT. The published NuGet binaries come with the "Open Source Maintenance Fee" EULA: a fee applies only to users with revenue-generating use and annual gross revenue of at least US$10,000. DriverCompass is free and non-commercial, so no fee applies. | `licenses/json-everything` (MIT and OSMFEULA) |
| Humanizer.Core (dependency of JsonSchema.Net) | MIT | `licenses/Humanizer` |

## Build and test tools (not shipped)

| Package | License |
|---|---|
| Microsoft.Windows.SDK.BuildTools, Microsoft.Windows.SDK.BuildTools.MSIX | Microsoft Software License Terms (build time only) |
| xunit.v3 | Apache-2.0 |

## Trademarks

Manufacturer names are used only to identify hardware and link to official support pages. DriverCompass is not affiliated with or endorsed by any of them.
